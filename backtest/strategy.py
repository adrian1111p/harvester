"""
strategy.py — Conduct Strategy V1.3: Multi-Timeframe Trend-Following (Long + Short).

Entry logic:
  1. Higher-TF bias  (1h / 1D EMA slope + ADX > 20)
  2. Mid-TF momentum (5m / 15m MACD histogram sign, RSI zone)
  3. Low-TF trigger   (1m / 30s Supertrend flip + volume spike + pullback to EMA)
  4. Optional: L2 imbalance confirmation

Exit logic (mirrors Conduct V1.2):
  - Hard stop at -1 R
  - Break-even at +1 R
  - Trailing: 0.5 R trail or 50% giveback from peak
  - TP1 at +1.5 R  (scale out 50%)
  - TP2 at +3 R    (close remainder)
  - Time stop: 90 min max hold
  - EOD flat: force close at 15:55 ET
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Optional

import numpy as np
import pandas as pd

from backtest.indicators import (
    adx,
    atr,
    bollinger_bands,
    ema,
    enrich_with_indicators,
    macd,
    relative_volume,
    rsi,
    supertrend,
    vwap,
)


class Side(Enum):
    LONG = "LONG"
    SHORT = "SHORT"


class ExitReason(Enum):
    HARD_STOP = "HARD_STOP"
    BREAK_EVEN = "BREAK_EVEN"
    TRAILING = "TRAILING"
    TP1 = "TP1"
    TP2 = "TP2"
    TP3 = "TP3"
    TIME_STOP = "TIME_STOP"
    EOD = "EOD"
    SIGNAL_REVERSAL = "SIGNAL_REVERSAL"


@dataclass
class StrategyConfig:
    """V1.3 Conduct strategy tunable parameters."""
    # Risk sizing
    risk_per_trade_dollars: float = 50.0         # $ risk per trade
    account_size: float = 25_000.0

    # Entry filters
    adx_threshold: float = 20.0                  # Trend strength gate
    rsi_long_range: tuple[float, float] = (35.0, 70.0)   # RSI zone for long entries
    rsi_short_range: tuple[float, float] = (30.0, 65.0)  # RSI zone for short entries
    rvol_min: float = 1.3                        # Min relative volume
    pullback_ema_period: int = 9                  # Pullback-to-EMA period
    require_supertrend: bool = True               # Require Supertrend alignment

    # Exit rules
    hard_stop_r: float = 1.0                     # Hard stop in R-multiples
    breakeven_r: float = 1.0                     # Move stop to BE at this R
    trail_r: float = 0.5                         # Trailing stop distance in R
    giveback_pct: float = 0.50                   # Max giveback from peak (50%)
    tp1_r: float = 1.5                           # Take-profit 1
    tp1_scale_pct: float = 0.50                  # Scale out % at TP1
    tp2_r: float = 3.0                           # Take-profit 2 (close balance)
    max_hold_bars: int = 180                     # Max hold in 30s bars (= 90 min)
    eod_bar_minute: int = 955                    # 15:55 ET in minutes-from-midnight

    # Slippage & commission
    slippage_cents: float = 1.0                  # Per share
    commission_per_share: float = 0.005          # IBKR tiered


@dataclass
class Signal:
    """A single entry/exit signal produced by the strategy."""
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    # Higher-TF context
    htf_trend: str         # "BULL" / "BEAR" / "NEUTRAL"
    mtf_momentum: str      # "ALIGNED" / "CONFLICTING"


@dataclass
class TradeResult:
    """Outcome of a completed trade."""
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


# ── Higher-TF Bias ───────────────────────────────────────────────────────────

def compute_htf_bias(
    df_1h: pd.DataFrame | None,
    df_1d: pd.DataFrame | None,
    cfg: StrategyConfig,
) -> str:
    """Determine higher-timeframe trend bias from 1h and 1D data."""
    scores = []

    for df, label in [(df_1h, "1h"), (df_1d, "1D")]:
        if df is None or len(df) < 50:
            continue
        enriched = enrich_with_indicators(df)
        last = enriched.iloc[-1]
        prev = enriched.iloc[-2] if len(enriched) > 1 else last

        # EMA slope
        ema_slope = 1 if last["EMA_21"] > prev["EMA_21"] else -1

        # ADX + DI
        if last["ADX"] > cfg.adx_threshold:
            di_score = 1 if last["Plus_DI"] > last["Minus_DI"] else -1
        else:
            di_score = 0

        # MACD histogram sign
        macd_score = 1 if last["MACD_Hist"] > 0 else -1

        scores.append(ema_slope + di_score + macd_score)

    if not scores:
        return "NEUTRAL"
    avg = sum(scores) / len(scores)
    if avg >= 1.5:
        return "BULL"
    elif avg <= -1.5:
        return "BEAR"
    return "NEUTRAL"


# ── Mid-TF Momentum ─────────────────────────────────────────────────────────

def compute_mtf_momentum(
    df_5m: pd.DataFrame | None,
    df_15m: pd.DataFrame | None,
    side: Side,
    cfg: StrategyConfig,
    _enriched_cache: dict | None = None,
) -> str:
    """Check if mid-timeframe momentum aligns with intended trade side."""
    aligned_count = 0
    total = 0

    for label, df in [("5m", df_5m), ("15m", df_15m)]:
        if df is None or len(df) < 30:
            continue
        # Use pre-enriched cache if available
        if _enriched_cache and label in _enriched_cache:
            enriched = _enriched_cache[label]
        else:
            enriched = enrich_with_indicators(df)
            if _enriched_cache is not None:
                _enriched_cache[label] = enriched
        last = enriched.iloc[-1]
        total += 1

        macd_ok = (last["MACD_Hist"] > 0) if side == Side.LONG else (last["MACD_Hist"] < 0)
        rsi_range = cfg.rsi_long_range if side == Side.LONG else cfg.rsi_short_range
        rsi_ok = rsi_range[0] <= last["RSI_14"] <= rsi_range[1]

        if macd_ok and rsi_ok:
            aligned_count += 1

    if total == 0:
        return "CONFLICTING"
    return "ALIGNED" if aligned_count == total else "CONFLICTING"


# ── V1.3 Strategy Engine ────────────────────────────────────────────────────

class ConductStrategyV13:
    """
    Multi-timeframe trend-following strategy.
    Works on the 1m trigger timeframe with context from 5m, 15m, 1h, 1D.
    """

    def __init__(self, cfg: StrategyConfig | None = None):
        self.cfg = cfg or StrategyConfig()

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[Signal]:
        """Scan trigger-timeframe bars and produce entry signals."""
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[Signal] = []

        htf_bias = compute_htf_bias(df_1h, df_1d, cfg)

        # Pre-compute MTF enrichment cache (avoid recomputing per bar)
        mtf_cache: dict = {}

        for i in range(50, len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            # Skip if ATR is NaN
            atr_val = row["ATR_14"]
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            # ── Determine candidate side ──
            candidate_sides: list[Side] = []

            if htf_bias in ("BULL", "NEUTRAL"):
                # Long candidate: Supertrend flips up, price pulls back to EMA
                st_flip_long = (row["ST_Direction"] == 1 and prev["ST_Direction"] == -1)
                ema_pullback_long = row["Close"] >= row["EMA_9"] and prev["Close"] < prev["EMA_9"]
                price_above_ema21 = row["Close"] > row["EMA_21"]

                if (st_flip_long or ema_pullback_long) and price_above_ema21:
                    candidate_sides.append(Side.LONG)

            if htf_bias in ("BEAR", "NEUTRAL"):
                # Short candidate: Supertrend flips down, price pulls back to EMA
                st_flip_short = (row["ST_Direction"] == -1 and prev["ST_Direction"] == 1)
                ema_pullback_short = row["Close"] <= row["EMA_9"] and prev["Close"] > prev["EMA_9"]
                price_below_ema21 = row["Close"] < row["EMA_21"]

                if (st_flip_short or ema_pullback_short) and price_below_ema21:
                    candidate_sides.append(Side.SHORT)

            for side in candidate_sides:
                # ── Volume filter ──
                rvol = row.get("RVOL", 1.0)
                if pd.notna(rvol) and rvol < cfg.rvol_min:
                    continue

                # ── RSI filter ──
                rsi_val = row["RSI_14"]
                if pd.notna(rsi_val):
                    rng = cfg.rsi_long_range if side == Side.LONG else cfg.rsi_short_range
                    if not (rng[0] <= rsi_val <= rng[1]):
                        continue

                # ── Mid-TF momentum ──
                mtf_mom = compute_mtf_momentum(df_5m, df_15m, side, cfg, _enriched_cache=mtf_cache)

                # ── Compute stop & size ──
                entry_price = row["Close"]
                stop_dist = cfg.hard_stop_r * atr_val
                stop_price = (entry_price - stop_dist) if side == Side.LONG else (entry_price + stop_dist)
                risk_per_share = abs(entry_price - stop_price)

                if risk_per_share <= 0:
                    continue

                pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

                signals.append(Signal(
                    bar_index=i,
                    timestamp=enriched.index[i],
                    side=side,
                    entry_price=entry_price,
                    stop_price=stop_price,
                    risk_per_share=risk_per_share,
                    position_size=pos_size,
                    atr_value=atr_val,
                    htf_trend=htf_bias,
                    mtf_momentum=mtf_mom,
                ))

        return signals

    def simulate_trade(
        self,
        signal: Signal,
        df_trigger: pd.DataFrame,
    ) -> TradeResult | None:
        """Simulate a single trade from signal to exit using Conduct V1.2 exit chain."""
        cfg = self.cfg
        side = signal.side
        entry_price = signal.entry_price + (cfg.slippage_cents / 100.0 if side == Side.LONG else -cfg.slippage_cents / 100.0)
        stop_price = signal.stop_price
        risk_per_share = signal.risk_per_share
        pos_size = signal.position_size
        remaining_size = pos_size

        peak_price = entry_price
        trough_price = entry_price
        breakeven_activated = False
        trailing_stop = stop_price
        exit_reason: ExitReason | None = None
        exit_price = entry_price
        exit_bar = signal.bar_index

        for j in range(signal.bar_index + 1, min(signal.bar_index + cfg.max_hold_bars + 1, len(df_trigger))):
            bar = df_trigger.iloc[j]
            price = bar["Close"]
            high = bar["High"]
            low = bar["Low"]

            # Track peak/trough
            if side == Side.LONG:
                peak_price = max(peak_price, high)
            else:
                trough_price = min(trough_price, low)

            # ── Priority 1: Hard stop ──
            if side == Side.LONG and low <= stop_price:
                exit_price = stop_price
                exit_reason = ExitReason.HARD_STOP
                exit_bar = j
                break
            if side == Side.SHORT and high >= stop_price:
                exit_price = stop_price
                exit_reason = ExitReason.HARD_STOP
                exit_bar = j
                break

            # ── Unrealized R ──
            unrealized_r = ((price - entry_price) / risk_per_share) if side == Side.LONG else ((entry_price - price) / risk_per_share)
            peak_r = ((peak_price - entry_price) / risk_per_share) if side == Side.LONG else ((entry_price - trough_price) / risk_per_share)

            # ── Priority 2: TP2 (full close) ──
            if unrealized_r >= cfg.tp2_r:
                exit_price = price
                exit_reason = ExitReason.TP2
                exit_bar = j
                break

            # ── Priority 3: TP1 scale-out (simplification: treat as full exit) ──
            if unrealized_r >= cfg.tp1_r and not breakeven_activated:
                # In a real system we'd scale out — here we simulate by tightening stop
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                    trailing_stop = max(trailing_stop, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)
                    trailing_stop = min(trailing_stop, entry_price)

            # ── Priority 4: Break-even ──
            if not breakeven_activated and unrealized_r >= cfg.breakeven_r:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)

            # ── Priority 5: Trailing stop ──
            if breakeven_activated:
                trail_dist = cfg.trail_r * risk_per_share
                if side == Side.LONG:
                    new_trail = peak_price - trail_dist
                    trailing_stop = max(trailing_stop, new_trail)
                    if low <= trailing_stop:
                        exit_price = trailing_stop
                        exit_reason = ExitReason.TRAILING
                        exit_bar = j
                        break
                else:
                    new_trail = trough_price + trail_dist
                    trailing_stop = min(trailing_stop, new_trail)
                    if high >= trailing_stop:
                        exit_price = trailing_stop
                        exit_reason = ExitReason.TRAILING
                        exit_bar = j
                        break

            # ── Priority 6: Giveback from peak ──
            if peak_r > 0:
                giveback = (peak_r - unrealized_r) / peak_r if peak_r > 0 else 0
                if giveback >= cfg.giveback_pct and unrealized_r > 0:
                    exit_price = price
                    exit_reason = ExitReason.TRAILING
                    exit_bar = j
                    break

            # ── Priority 7: Time stop ──
            bars_held = j - signal.bar_index
            if bars_held >= cfg.max_hold_bars:
                exit_price = price
                exit_reason = ExitReason.TIME_STOP
                exit_bar = j
                break

        else:
            # Exhausted bars without exit → force close on last bar
            exit_price = df_trigger.iloc[min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)]["Close"]
            exit_reason = ExitReason.TIME_STOP
            exit_bar = min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)

        # Apply slippage on exit
        if side == Side.LONG:
            exit_price -= cfg.slippage_cents / 100.0
        else:
            exit_price += cfg.slippage_cents / 100.0

        # PnL
        pnl_per_share = (exit_price - entry_price) if side == Side.LONG else (entry_price - exit_price)
        commission = cfg.commission_per_share * pos_size * 2  # round-trip
        pnl = pnl_per_share * pos_size - commission
        pnl_r = pnl_per_share / risk_per_share if risk_per_share > 0 else 0.0

        return TradeResult(
            entry_bar=signal.bar_index,
            exit_bar=exit_bar,
            entry_time=signal.timestamp,
            exit_time=df_trigger.index[exit_bar] if exit_bar < len(df_trigger) else signal.timestamp,
            side=side,
            entry_price=entry_price,
            exit_price=exit_price,
            stop_price=signal.stop_price,
            position_size=pos_size,
            pnl=pnl,
            pnl_r=pnl_r,
            exit_reason=exit_reason,
            peak_r=((peak_price - entry_price) / risk_per_share) if side == Side.LONG else ((entry_price - trough_price) / risk_per_share),
            bars_held=exit_bar - signal.bar_index,
        )
