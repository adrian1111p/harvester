"""
strategy_v6.py — V6 "Opening Range Breakout" (ORB) Strategy.

Research basis:
  The ORB is one of the most proven and widely-used day trading strategies.
  It was popularized by Toby Crabel and refined by modern momentum traders.

How it works:
  1. Mark the HIGH and LOW of the first N minutes after market open (9:30 ET).
     Default: first 15 minutes (9:30–9:45), configurable to 5 or 30 min.
  2. BUY when price breaks ABOVE the opening range high with volume confirmation.
  3. SHORT when price breaks BELOW the opening range low with volume confirmation.
  4. Stop: opposite side of the opening range (or midpoint for tighter risk).
  5. Targets: 1R, 2R based on the range size.

Safety filters (lessons from live trading):
  - 20MA exhaustion filter: reject LONG if price is extended above 20MA,
    reject SHORT if extended below.
  - VWAP confirmation: prefer LONG above VWAP, SHORT below VWAP.
  - Minimum range size: the OR must be at least 0.3 ATR (avoid tight chop).
  - Maximum range size: the OR must be less than 3.0 ATR (avoid gap days).
  - Volume gate: breakout bar must have RVOL > threshold.
  - Time window: ORB signals valid 9:45–11:30 and 14:00–15:30 (avoid dead zone).
  - One entry per direction per day (no re-entries after stop-out).

Exit chain:
  Hard stop → Micro-trail → Breakeven → Giveback → TP1 → TP2 → Time stop
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from datetime import time as dtime

import numpy as np
import pandas as pd

from backtest.indicators import enrich_with_indicators


class Side(Enum):
    LONG = "LONG"
    SHORT = "SHORT"


class ExitReason(Enum):
    HARD_STOP = "HARD_STOP"
    MICRO_TRAIL = "MICRO_TRAIL"
    REVERSAL_FLATTEN = "REVERSAL_FLATTEN"
    TRAILING = "TRAILING"
    BREAKEVEN = "BREAKEVEN"
    GIVEBACK = "GIVEBACK"
    TP1 = "TP1"
    TP2 = "TP2"
    TIME_STOP = "TIME_STOP"


@dataclass
class V6Config:
    """V6 ORB configuration."""
    risk_per_trade_dollars: float = 50.0

    # ── Opening Range params ──
    or_minutes: int = 15          # First N minutes define the range (5, 15, or 30)
    min_range_atr: float = 0.3    # Min OR size in ATR (avoid chop)
    max_range_atr: float = 10.0   # Max OR size in ATR (avoid extreme gap days)

    # ── 20MA Exhaustion Filter ──
    ma_period: int = 20
    max_ma_dist_atr: float = 0.5  # Max distance from 20MA for entry

    # ── VWAP confirmation ──
    require_vwap_align: bool = True   # LONG above VWAP, SHORT below

    # ── Volume gate ──
    rvol_min: float = 0.8         # Min RVOL on breakout bar

    # ── Stop placement ──
    stop_at_opposite: bool = True    # Stop at opposite side of OR
    stop_at_midpoint: bool = False   # Alternative: stop at OR midpoint (tighter)

    # ── Micro-trail ──
    micro_trail_cents: float = 3.0
    micro_trail_activate_cents: float = 5.0

    # ── Standard exits ──
    hard_stop_r: float = 1.5
    trail_r: float = 1.0
    breakeven_r: float = 0.5
    giveback_pct: float = 0.50
    tp1_r: float = 1.0
    tp2_r: float = 2.5
    max_hold_bars: int = 60

    # ── Time windows (minutes from midnight ET) ──
    # Valid entry windows: 9:45-11:30 and 14:00-15:30
    entry_windows: list = None

    # ── Slippage & commission ──
    slippage_cents: float = 1.0
    commission_per_share: float = 0.005

    # ── Direction ──
    allow_long: bool = True
    allow_short: bool = True

    # ── Price filter ──
    min_price: float = 8.0
    max_price: float = 1000.0

    # ── Reversal flatten ──
    reversal_flatten: bool = True

    def __post_init__(self):
        if self.entry_windows is None:
            self.entry_windows = [
                (9 * 60 + 45, 11 * 60 + 30),    # 9:45 - 11:30
                (14 * 60, 15 * 60 + 30),          # 14:00 - 15:30
            ]


@dataclass
class V6Signal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    entry_type: str           # "ORB_BREAKOUT_HIGH" / "ORB_BREAKOUT_LOW"
    or_high: float
    or_low: float
    or_range: float
    ma_distance_atr: float


@dataclass
class V6TradeResult:
    entry_bar: int
    exit_bar: int
    entry_time: pd.Timestamp
    exit_time: pd.Timestamp
    side: Side
    entry_price: float
    exit_price: float
    stop_price: float
    position_size: int
    pnl: float
    pnl_r: float
    exit_reason: ExitReason
    peak_r: float
    bars_held: int
    entry_type: str


class StrategyV6:
    """V6 Opening Range Breakout Strategy."""

    def __init__(self, cfg: V6Config | None = None):
        self.cfg = cfg or V6Config()
        try:
            from zoneinfo import ZoneInfo
            self._et = ZoneInfo("America/New_York")
        except Exception:
            self._et = None

    def _get_minute_of_day(self, ts: pd.Timestamp) -> int:
        """Get minutes from midnight in ET for a timestamp (handles UTC data)."""
        if self._et is not None and ts.tzinfo is not None:
            ts = ts.astimezone(self._et)
        return ts.hour * 60 + ts.minute

    def _in_entry_window(self, minute_of_day: int) -> bool:
        """Check if current time is in a valid entry window."""
        for start, end in self.cfg.entry_windows:
            if start <= minute_of_day <= end:
                return True
        return False

    def _compute_opening_range(self, df: pd.DataFrame) -> tuple[float, float, int]:
        """
        Find the high and low of the first N minutes after 9:30 ET.
        Returns (or_high, or_low, or_end_index) or (nan, nan, -1) if not enough data.
        Handles multi-day data by computing OR for the LAST trading day.
        """
        cfg = self.cfg
        market_open_minute = 9 * 60 + 30  # 570
        or_end_minute = market_open_minute + cfg.or_minutes

        # Group bars by trading day (in ET)
        day_bars: dict[str, list[int]] = {}
        for i in range(len(df)):
            ts = df.index[i] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[i])
            if self._et is not None and ts.tzinfo is not None:
                ts = ts.astimezone(self._et)
            day_key = ts.strftime("%Y-%m-%d")
            if day_key not in day_bars:
                day_bars[day_key] = []
            day_bars[day_key].append(i)

        # For each day, compute OR and generate signals from that day
        # Store all per-day ORs so generate_signals can use them
        self._per_day_or: dict[str, tuple[float, float, int]] = {}
        for day_key, bar_indices in day_bars.items():
            or_bars = []
            or_end_idx = -1
            for idx in bar_indices:
                ts = df.index[idx] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[idx])
                mod = self._get_minute_of_day(ts)
                if market_open_minute <= mod < or_end_minute:
                    or_bars.append(idx)
                elif mod >= or_end_minute and or_end_idx == -1:
                    or_end_idx = idx
            if or_bars and or_end_idx > 0:
                or_high = df.iloc[or_bars]["High"].max()
                or_low = df.iloc[or_bars]["Low"].min()
                self._per_day_or[day_key] = (or_high, or_low, or_end_idx)

        # Return something for compatibility (last day)
        if not self._per_day_or:
            return np.nan, np.nan, -1
        last_day = list(self._per_day_or.values())[-1]
        return last_day

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[V6Signal]:
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[V6Signal] = []

        # Compute opening ranges (per-day)
        self._compute_opening_range(enriched)
        if not self._per_day_or:
            return signals

        # HTF bias
        htf_bias = self._htf_bias(df_1h, df_1d)

        # Iterate per-day
        for day_key, (or_high, or_low, or_end_idx) in self._per_day_or.items():
            or_range = or_high - or_low
            if or_range <= 0:
                continue

            signaled_long = False
            signaled_short = False

            for i in range(or_end_idx, len(enriched)):
                row = enriched.iloc[i]
                prev = enriched.iloc[i - 1]

                # Check we're still on the same day
                ts = enriched.index[i] if isinstance(enriched.index, pd.DatetimeIndex) else pd.Timestamp(enriched.index[i])
                if self._et is not None and ts.tzinfo is not None:
                    ts_et = ts.astimezone(self._et)
                else:
                    ts_et = ts
                bar_day = ts_et.strftime("%Y-%m-%d")
                if bar_day != day_key:
                    break  # Next day — stop scanning this day's OR

                atr_val = row.get("ATR_14", np.nan)
                if pd.isna(atr_val) or atr_val <= 0:
                    continue

                price = row["Close"]
                if price < cfg.min_price or price > cfg.max_price:
                    continue

                # Check OR range size vs ATR
                or_range_atr = or_range / atr_val
                if or_range_atr < cfg.min_range_atr or or_range_atr > cfg.max_range_atr:
                    continue

                # Time window check
                mod = self._get_minute_of_day(ts)
                if not self._in_entry_window(mod):
                    continue

                # 20MA distance
                sma_20 = row.get("SMA_20", np.nan)
                if pd.isna(sma_20) or sma_20 <= 0:
                    continue
                ma_dist = (price - sma_20) / atr_val

                # Volume
                rvol = row.get("RVOL", 1.0)
                if pd.isna(rvol):
                    rvol = 1.0
                if rvol < cfg.rvol_min:
                    continue

                # VWAP
                vwap_val = row.get("VWAP", np.nan)

                # ── LONG: break above OR high ──
                if (cfg.allow_long and not signaled_long and
                    price > or_high and prev["Close"] <= or_high and
                    ma_dist <= cfg.max_ma_dist_atr and
                    htf_bias != "BEAR"):

                    if cfg.require_vwap_align and pd.notna(vwap_val) and price < vwap_val:
                        continue

                    sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                            "ORB_BREAKOUT_HIGH", or_high, or_low,
                                            or_range, ma_dist)
                    if sig:
                        signals.append(sig)
                        signaled_long = True
                        continue

                # ── SHORT: break below OR low ──
                if (cfg.allow_short and not signaled_short and
                    price < or_low and prev["Close"] >= or_low and
                    ma_dist >= -cfg.max_ma_dist_atr and
                    htf_bias != "BULL"):

                    if cfg.require_vwap_align and pd.notna(vwap_val) and price > vwap_val:
                        continue

                    sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                            "ORB_BREAKOUT_LOW", or_high, or_low,
                                            or_range, ma_dist)
                    if sig:
                        signals.append(sig)
                        signaled_short = True
                        continue

        return signals

    def _make_signal(self, idx: int, df: pd.DataFrame, side: Side,
                     atr_val: float, entry_type: str,
                     or_high: float, or_low: float,
                     or_range: float, ma_dist: float) -> V6Signal | None:
        cfg = self.cfg
        row = df.iloc[idx]
        price = row["Close"]

        # Stop placement
        if side == Side.LONG:
            if cfg.stop_at_midpoint:
                stop = (or_high + or_low) / 2
            else:
                stop = or_low  # Opposite side of OR
            stop -= cfg.slippage_cents / 100.0
        else:
            if cfg.stop_at_midpoint:
                stop = (or_high + or_low) / 2
            else:
                stop = or_high  # Opposite side of OR
            stop += cfg.slippage_cents / 100.0

        risk_ps = abs(price - stop)
        if risk_ps <= 0:
            return None

        size = max(1, int(cfg.risk_per_trade_dollars / risk_ps))
        ts = df.index[idx] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[idx])

        return V6Signal(
            bar_index=idx,
            timestamp=ts,
            side=side,
            entry_price=price + (cfg.slippage_cents / 100.0 if side == Side.LONG else -cfg.slippage_cents / 100.0),
            stop_price=stop,
            risk_per_share=risk_ps,
            position_size=size,
            atr_value=atr_val,
            entry_type=entry_type,
            or_high=or_high,
            or_low=or_low,
            or_range=or_range,
            ma_distance_atr=ma_dist,
        )

    def _htf_bias(self, df_1h: pd.DataFrame | None, df_1d: pd.DataFrame | None) -> str:
        """Simple HTF bias from 1h and 1D data."""
        bias_votes = []
        for htf in (df_1h, df_1d):
            if htf is not None and len(htf) >= 20:
                c = htf["Close"].iloc[-1]
                ema20 = htf["Close"].ewm(span=20, adjust=False).mean().iloc[-1]
                if c > ema20:
                    bias_votes.append("BULL")
                else:
                    bias_votes.append("BEAR")
        if not bias_votes:
            return "NEUTRAL"
        bulls = bias_votes.count("BULL")
        bears = bias_votes.count("BEAR")
        if bulls > bears:
            return "BULL"
        elif bears > bulls:
            return "BEAR"
        return "NEUTRAL"

    # ══════════════════════════════════════════════════════════════════════════
    # TRADE SIMULATION (for backtesting)
    # ══════════════════════════════════════════════════════════════════════════

    def simulate_trade(self, sig: V6Signal, df_trigger: pd.DataFrame) -> V6TradeResult | None:
        """Simulate a single trade from signal through exit."""
        cfg = self.cfg
        entry_bar = sig.bar_index
        entry_price = sig.entry_price
        stop_price = sig.stop_price
        rps = sig.risk_per_share
        side = sig.side
        size = sig.position_size

        peak_r = 0.0
        peak_price = entry_price
        be_activated = False
        micro_trail_active = False
        trail_stop = stop_price

        for j in range(entry_bar + 1, min(entry_bar + cfg.max_hold_bars + 1, len(df_trigger))):
            row = df_trigger.iloc[j]
            hi, lo, cl = row["High"], row["Low"], row["Close"]

            if side == Side.LONG:
                unrealized_r = (cl - entry_price) / rps
                intra_high_r = (hi - entry_price) / rps
                peak_price = max(peak_price, hi)
                peak_r = max(peak_r, intra_high_r)
                profit_ps = cl - entry_price

                # Hard stop check (intra-bar)
                if lo <= stop_price:
                    exit_price = stop_price - cfg.slippage_cents / 100.0
                    pnl = (exit_price - entry_price) * size
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        ExitReason.HARD_STOP, peak_r)

                # Trail stop check
                if trail_stop > stop_price and lo <= trail_stop:
                    exit_price = trail_stop - cfg.slippage_cents / 100.0
                    pnl = (exit_price - entry_price) * size
                    reason = ExitReason.MICRO_TRAIL if micro_trail_active else ExitReason.TRAILING
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size, reason, peak_r)

            else:  # SHORT
                unrealized_r = (entry_price - cl) / rps
                intra_low_r = (entry_price - lo) / rps
                peak_price = min(peak_price, lo)
                peak_r = max(peak_r, intra_low_r)
                profit_ps = entry_price - cl

                # Hard stop (intra-bar)
                if hi >= stop_price:
                    exit_price = stop_price + cfg.slippage_cents / 100.0
                    pnl = (entry_price - exit_price) * size
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        ExitReason.HARD_STOP, peak_r)

                if trail_stop < stop_price and hi >= trail_stop:
                    exit_price = trail_stop + cfg.slippage_cents / 100.0
                    pnl = (entry_price - exit_price) * size
                    reason = ExitReason.MICRO_TRAIL if micro_trail_active else ExitReason.TRAILING
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size, reason, peak_r)

            # ── TP2 ──
            if unrealized_r >= cfg.tp2_r:
                exit_price = cl
                pnl = (cl - entry_price) * size if side == Side.LONG else (entry_price - cl) * size
                return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                    ExitReason.TP2, peak_r)

            # ── Reversal flatten ──
            if cfg.reversal_flatten and unrealized_r > 0:
                prev_row = df_trigger.iloc[j - 1]
                bar_range = hi - lo
                if bar_range > 0:
                    if side == Side.LONG:
                        upper_wick = (hi - max(row["Open"], cl)) / bar_range
                        is_engulfing = (cl < row["Open"] and cl < prev_row["Open"])
                        if is_engulfing or upper_wick > 0.6:
                            exit_price = cl
                            pnl = (cl - entry_price) * size
                            return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                                ExitReason.REVERSAL_FLATTEN, peak_r)
                    else:
                        lower_wick = (min(row["Open"], cl) - lo) / bar_range
                        is_engulfing = (cl > row["Open"] and cl > prev_row["Open"])
                        if is_engulfing or lower_wick > 0.6:
                            exit_price = cl
                            pnl = (entry_price - cl) * size
                            return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                                ExitReason.REVERSAL_FLATTEN, peak_r)

            # ── Giveback ──
            if peak_r > 0 and unrealized_r > 0:
                giveback = (peak_r - unrealized_r) / peak_r
                if giveback >= cfg.giveback_pct:
                    exit_price = cl
                    pnl = (cl - entry_price) * size if side == Side.LONG else (entry_price - cl) * size
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        ExitReason.GIVEBACK, peak_r)

            # ── Micro-trail update ──
            if cfg.micro_trail_cents > 0 and profit_ps >= cfg.micro_trail_activate_cents / 100.0:
                micro_trail_active = True
                micro_dist = cfg.micro_trail_cents / 100.0
                if side == Side.LONG:
                    new_trail = peak_price - micro_dist
                    trail_stop = max(trail_stop, new_trail)
                else:
                    new_trail = peak_price + micro_dist
                    trail_stop = min(trail_stop, new_trail) if trail_stop > 0 else new_trail

            # ── Breakeven ──
            if not be_activated and unrealized_r >= cfg.breakeven_r:
                be_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                    trail_stop = max(trail_stop, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)
                    trail_stop = min(trail_stop, entry_price) if trail_stop > 0 else entry_price

            # ── Standard trailing ──
            if be_activated and not micro_trail_active:
                trail_dist = cfg.trail_r * rps
                if side == Side.LONG:
                    new_trail = peak_price - trail_dist
                    trail_stop = max(trail_stop, new_trail)
                else:
                    new_trail = peak_price + trail_dist
                    trail_stop = min(trail_stop, new_trail) if trail_stop > 0 else new_trail

        # Time stop: still holding
        if entry_bar + 1 < len(df_trigger):
            last_idx = min(entry_bar + cfg.max_hold_bars, len(df_trigger) - 1)
            cl = df_trigger.iloc[last_idx]["Close"]
            pnl = (cl - entry_price) * size if side == Side.LONG else (entry_price - cl) * size
            return self._result(sig, last_idx, df_trigger, cl, pnl, rps, size,
                                ExitReason.TIME_STOP, peak_r)
        return None

    def _result(self, sig: V6Signal, exit_idx: int, df: pd.DataFrame,
                exit_price: float, pnl: float, rps: float, size: int,
                reason: ExitReason, peak_r: float) -> V6TradeResult:
        ts_exit = df.index[exit_idx] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[exit_idx])
        pnl_r = pnl / (rps * size) if rps > 0 and size > 0 else 0
        return V6TradeResult(
            entry_bar=sig.bar_index,
            exit_bar=exit_idx,
            entry_time=sig.timestamp,
            exit_time=ts_exit,
            side=sig.side,
            entry_price=sig.entry_price,
            exit_price=exit_price,
            stop_price=sig.stop_price,
            position_size=size,
            pnl=pnl,
            pnl_r=pnl_r,
            exit_reason=reason,
            peak_r=peak_r,
            bars_held=exit_idx - sig.bar_index,
            entry_type=sig.entry_type,
        )
