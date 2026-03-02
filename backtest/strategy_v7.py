"""
strategy_v7.py — V7 "9 EMA Momentum Scalp" Strategy.

Research basis:
  "Ride the 9" is a proven day trading technique used by momentum traders.
  The 9 EMA acts as a dynamic support/resistance in trending stocks.
  Combined with micro-pullback entries and tight trailing, it captures
  the "meat" of intraday momentum moves.

How it works:
  1. Identify trend direction via 9/20 EMA alignment:
     - Bullish: EMA_9 > EMA_20 (both rising)
     - Bearish: EMA_9 < EMA_20 (both falling)
  2. Wait for a micro-pullback to the 9 EMA:
     - LONG: price dips to within 0.2 ATR of EMA_9 from above, then bounces
     - SHORT: price rallies to within 0.2 ATR of EMA_9 from below, then rejects
  3. Confirmation: candle closes back in the direction of the trend
  4. Volume: pullback on lower volume, bounce on higher volume
  5. Stop: below the pullback low (LONG) or above pullback high (SHORT)
  6. Trail: the 9 EMA itself becomes the trailing stop

Safety filters:
  - 20MA exhaustion filter: same as V5/V6
  - RSI filter: avoid overbought entries for LONG (>75), oversold for SHORT (<25)
  - First 10 min after open: skip (let trend establish)
  - Max 1 ATR risk per share

Exit chain:
  Hard stop → 9EMA trail → Micro-trail → Reversal flatten → TP → Time stop
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

import numpy as np
import pandas as pd

from backtest.indicators import enrich_with_indicators


class Side(Enum):
    LONG = "LONG"
    SHORT = "SHORT"


class ExitReason(Enum):
    HARD_STOP = "HARD_STOP"
    EMA_TRAIL = "EMA_TRAIL"
    MICRO_TRAIL = "MICRO_TRAIL"
    REVERSAL_FLATTEN = "REVERSAL_FLATTEN"
    TRAILING = "TRAILING"
    BREAKEVEN = "BREAKEVEN"
    GIVEBACK = "GIVEBACK"
    TP1 = "TP1"
    TP2 = "TP2"
    TIME_STOP = "TIME_STOP"


@dataclass
class V7Config:
    """V7 9EMA Momentum Scalp configuration."""
    risk_per_trade_dollars: float = 50.0

    # ── EMA params ──
    ema_fast: int = 9
    ema_slow: int = 20
    # Pullback proximity: price must be within this many ATRs of EMA_9
    pullback_atr_proximity: float = 0.2
    # EMA must be trending: slope over N bars
    ema_slope_bars: int = 5
    ema_min_slope_atr: float = 0.02   # Minimum EMA slope in ATR/bar

    # ── 20MA exhaustion filter ──
    ma_period: int = 20
    max_ma_dist_atr: float = 0.5

    # ── RSI filter ──
    rsi_max_long: float = 75.0    # Skip LONG if RSI > 75
    rsi_min_short: float = 25.0   # Skip SHORT if RSI < 25

    # ── Volume ──
    rvol_min: float = 0.8
    # Volume contraction on pullback: pullback bar volume < prev bar
    require_volume_contraction: bool = True
    # Volume expansion on entry: entry bar volume > pullback bar
    require_volume_expansion: bool = True

    # ── Candle confirmation ──
    require_candle_confirm: bool = True

    # ── 9 EMA trailing ──
    use_ema_trail: bool = True     # Trail using 9 EMA as dynamic stop
    ema_trail_buffer_atr: float = 0.1  # Buffer below EMA for LONG trail

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
    max_hold_bars: int = 45       # 45 min — scalps should be quick

    # ── Time filter ──
    skip_first_n_minutes: int = 10  # Skip first 10 min after open

    # ── Reversal flatten ──
    reversal_flatten: bool = True

    # ── Slippage ──
    slippage_cents: float = 1.0
    commission_per_share: float = 0.005

    # ── Direction ──
    allow_long: bool = True
    allow_short: bool = True

    # ── Price filter ──
    min_price: float = 8.0
    max_price: float = 1000.0


@dataclass
class V7Signal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    entry_type: str           # "9EMA_PULLBACK_LONG" / "9EMA_PULLBACK_SHORT"
    ema_9_at_entry: float
    ema_20_at_entry: float
    ma_distance_atr: float


@dataclass
class V7TradeResult:
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


class StrategyV7:
    """V7 9 EMA Momentum Scalp with micro-trailing."""

    def __init__(self, cfg: V7Config | None = None):
        self.cfg = cfg or V7Config()
        try:
            from zoneinfo import ZoneInfo
            self._et = ZoneInfo("America/New_York")
        except Exception:
            self._et = None

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[V7Signal]:
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[V7Signal] = []

        # HTF bias (lightweight)
        htf_bias = self._htf_bias(df_1h, df_1d)

        # Compute EMA_9 and EMA_20 (should already be in enriched from indicators)
        ema_9_col = enriched.get("EMA_9")
        sma_20_col = enriched.get("SMA_20")

        if ema_9_col is None or sma_20_col is None:
            return signals

        # Also compute EMA_20 for crossover
        ema_20 = enriched["Close"].ewm(span=cfg.ema_slow, adjust=False).mean()

        # Market open minute (for time filter)
        market_open_minute = 9 * 60 + 30

        for i in range(max(50, cfg.ema_slope_bars + 1), len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            atr_val = row.get("ATR_14", np.nan)
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            price = row["Close"]
            if price < cfg.min_price or price > cfg.max_price:
                continue

            # Time filter: skip first N minutes
            ts = enriched.index[i] if isinstance(enriched.index, pd.DatetimeIndex) else pd.Timestamp(enriched.index[i])
            if self._et is not None and ts.tzinfo is not None:
                ts = ts.astimezone(self._et)
            mod = ts.hour * 60 + ts.minute
            if mod < market_open_minute + cfg.skip_first_n_minutes:
                continue

            # ── EMA values ──
            ema_9_val = ema_9_col.iloc[i]
            ema_20_val = ema_20.iloc[i]
            sma_20_val = sma_20_col.iloc[i]

            if pd.isna(ema_9_val) or pd.isna(ema_20_val) or pd.isna(sma_20_val):
                continue

            # ── 20MA exhaustion filter ──
            ma_dist = (price - sma_20_val) / atr_val

            # ── EMA alignment and slope ──
            ema_9_prev = ema_9_col.iloc[i - cfg.ema_slope_bars]
            ema_20_prev = ema_20.iloc[i - cfg.ema_slope_bars]

            ema_9_slope = (ema_9_val - ema_9_prev) / (cfg.ema_slope_bars * atr_val)
            ema_20_slope = (ema_20_val - ema_20_prev) / (cfg.ema_slope_bars * atr_val)

            is_bullish_ema = (ema_9_val > ema_20_val and
                              ema_9_slope > cfg.ema_min_slope_atr)
            is_bearish_ema = (ema_9_val < ema_20_val and
                              ema_9_slope < -cfg.ema_min_slope_atr)

            # ── Pullback proximity to 9 EMA ──
            dist_to_ema9 = (price - ema_9_val) / atr_val

            # ── RSI ──
            rsi_val = row.get("RSI_14", 50.0)
            if pd.isna(rsi_val):
                rsi_val = 50.0

            # ── Volume ──
            rvol = row.get("RVOL", 1.0)
            if pd.isna(rvol):
                rvol = 1.0
            if rvol < cfg.rvol_min:
                continue

            vol = row.get("Volume", 0)
            prev_vol = prev.get("Volume", 0)

            # ── Candle confirmation ──
            candle_bull = (price > row["Open"])  # Green candle
            candle_bear = (price < row["Open"])  # Red candle

            # ═══════════════════════════════════════════
            # LONG: 9EMA pullback in uptrend
            # ═══════════════════════════════════════════
            if (cfg.allow_long and is_bullish_ema and
                -cfg.pullback_atr_proximity <= dist_to_ema9 <= cfg.pullback_atr_proximity and
                ma_dist <= cfg.max_ma_dist_atr and  # Not extended above 20MA
                rsi_val < cfg.rsi_max_long and
                htf_bias != "BEAR"):

                # Candle confirmation: green candle bouncing off 9 EMA
                if cfg.require_candle_confirm and not candle_bull:
                    continue

                # Volume: expansion on bounce preferred
                if cfg.require_volume_expansion and vol <= prev_vol * 0.8:
                    continue

                sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                        "9EMA_PULLBACK_LONG",
                                        ema_9_val, ema_20_val, ma_dist)
                if sig:
                    signals.append(sig)
                    continue

            # ═══════════════════════════════════════════
            # SHORT: 9EMA rejection in downtrend
            # ═══════════════════════════════════════════
            if (cfg.allow_short and is_bearish_ema and
                -cfg.pullback_atr_proximity <= dist_to_ema9 <= cfg.pullback_atr_proximity and
                ma_dist >= -cfg.max_ma_dist_atr and  # Not extended below 20MA
                rsi_val > cfg.rsi_min_short and
                htf_bias != "BULL"):

                if cfg.require_candle_confirm and not candle_bear:
                    continue

                if cfg.require_volume_expansion and vol <= prev_vol * 0.8:
                    continue

                sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                        "9EMA_PULLBACK_SHORT",
                                        ema_9_val, ema_20_val, ma_dist)
                if sig:
                    signals.append(sig)
                    continue

        return signals

    def _make_signal(self, idx: int, df: pd.DataFrame, side: Side,
                     atr_val: float, entry_type: str,
                     ema_9: float, ema_20: float, ma_dist: float) -> V7Signal | None:
        cfg = self.cfg
        row = df.iloc[idx]
        price = row["Close"]

        # Stop: below recent swing low (LONG) or above recent swing high (SHORT)
        # Use the low/high of the last 3 bars as the swing reference
        lookback = min(3, idx)
        if side == Side.LONG:
            recent_low = df.iloc[idx - lookback:idx + 1]["Low"].min()
            stop = recent_low - cfg.slippage_cents / 100.0
        else:
            recent_high = df.iloc[idx - lookback:idx + 1]["High"].max()
            stop = recent_high + cfg.slippage_cents / 100.0

        risk_ps = abs(price - stop)
        # Cap risk at 1 ATR
        if risk_ps > atr_val:
            if side == Side.LONG:
                stop = price - atr_val
            else:
                stop = price + atr_val
            risk_ps = atr_val

        if risk_ps <= 0:
            return None

        size = max(1, int(cfg.risk_per_trade_dollars / risk_ps))
        ts = df.index[idx] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[idx])

        return V7Signal(
            bar_index=idx,
            timestamp=ts,
            side=side,
            entry_price=price + (cfg.slippage_cents / 100.0 if side == Side.LONG else -cfg.slippage_cents / 100.0),
            stop_price=stop,
            risk_per_share=risk_ps,
            position_size=size,
            atr_value=atr_val,
            entry_type=entry_type,
            ema_9_at_entry=ema_9,
            ema_20_at_entry=ema_20,
            ma_distance_atr=ma_dist,
        )

    def _htf_bias(self, df_1h: pd.DataFrame | None, df_1d: pd.DataFrame | None) -> str:
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
    # TRADE SIMULATION
    # ══════════════════════════════════════════════════════════════════════════

    def simulate_trade(self, sig: V7Signal, df_trigger: pd.DataFrame) -> V7TradeResult | None:
        """Simulate a single trade with 9 EMA trailing."""
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

        # Pre-compute EMA_9 for trailing (reuse from enriched data)
        ema_9_series = df_trigger["Close"].ewm(span=cfg.ema_fast, adjust=False).mean()

        for j in range(entry_bar + 1, min(entry_bar + cfg.max_hold_bars + 1, len(df_trigger))):
            row = df_trigger.iloc[j]
            hi, lo, cl = row["High"], row["Low"], row["Close"]

            if side == Side.LONG:
                unrealized_r = (cl - entry_price) / rps
                intra_high_r = (hi - entry_price) / rps
                peak_price = max(peak_price, hi)
                peak_r = max(peak_r, intra_high_r)
                profit_ps = cl - entry_price

                # Hard stop check
                if lo <= stop_price:
                    exit_price = stop_price - cfg.slippage_cents / 100.0
                    pnl = (exit_price - entry_price) * size
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        ExitReason.HARD_STOP, peak_r)

                # Trail stop check (combined: micro + EMA + standard)
                if trail_stop > stop_price and lo <= trail_stop:
                    exit_price = trail_stop - cfg.slippage_cents / 100.0
                    pnl = (exit_price - entry_price) * size
                    reason = ExitReason.MICRO_TRAIL if micro_trail_active else (
                        ExitReason.EMA_TRAIL if cfg.use_ema_trail else ExitReason.TRAILING)
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        reason, peak_r)

            else:  # SHORT
                unrealized_r = (entry_price - cl) / rps
                intra_low_r = (entry_price - lo) / rps
                peak_price = min(peak_price, lo)
                peak_r = max(peak_r, intra_low_r)
                profit_ps = entry_price - cl

                if hi >= stop_price:
                    exit_price = stop_price + cfg.slippage_cents / 100.0
                    pnl = (entry_price - exit_price) * size
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        ExitReason.HARD_STOP, peak_r)

                if trail_stop < stop_price and hi >= trail_stop:
                    exit_price = trail_stop + cfg.slippage_cents / 100.0
                    pnl = (entry_price - exit_price) * size
                    reason = ExitReason.MICRO_TRAIL if micro_trail_active else (
                        ExitReason.EMA_TRAIL if cfg.use_ema_trail else ExitReason.TRAILING)
                    return self._result(sig, j, df_trigger, exit_price, pnl, rps, size,
                                        reason, peak_r)

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

            # ── 9 EMA Trailing (the signature of V7) ──
            if cfg.use_ema_trail and be_activated and not micro_trail_active:
                ema_9_now = ema_9_series.iloc[j]
                if pd.notna(ema_9_now):
                    atr_now = row.get("ATR_14", rps) if "ATR_14" in df_trigger.columns else rps
                    if pd.isna(atr_now):
                        atr_now = rps
                    buffer = cfg.ema_trail_buffer_atr * atr_now
                    if side == Side.LONG:
                        ema_trail = ema_9_now - buffer
                        trail_stop = max(trail_stop, ema_trail)
                    else:
                        ema_trail = ema_9_now + buffer
                        trail_stop = min(trail_stop, ema_trail) if trail_stop > 0 else ema_trail

            # ── Micro-trail ──
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

        # Time stop
        if entry_bar + 1 < len(df_trigger):
            last_idx = min(entry_bar + cfg.max_hold_bars, len(df_trigger) - 1)
            cl = df_trigger.iloc[last_idx]["Close"]
            pnl = (cl - entry_price) * size if side == Side.LONG else (entry_price - cl) * size
            return self._result(sig, last_idx, df_trigger, cl, pnl, rps, size,
                                ExitReason.TIME_STOP, peak_r)
        return None

    def _result(self, sig: V7Signal, exit_idx: int, df: pd.DataFrame,
                exit_price: float, pnl: float, rps: float, size: int,
                reason: ExitReason, peak_r: float) -> V7TradeResult:
        ts_exit = df.index[exit_idx] if isinstance(df.index, pd.DatetimeIndex) else pd.Timestamp(df.index[exit_idx])
        pnl_r = pnl / (rps * size) if rps > 0 and size > 0 else 0
        return V7TradeResult(
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
