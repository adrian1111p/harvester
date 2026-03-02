"""
strategy_v5.py — V5 "Smart Mean-Reversion" Strategy.

Key lessons from live trading (2026-03-02):
  1. NEVER enter LONG when price is extended ABOVE 20MA, or SHORT when below.
     Extended price = exhaustion → reversal risk. If you're buying, price must
     be BELOW or near 20MA (discount). If shorting, ABOVE or near 20MA.
  2. If you have a small profit, trail VERY tight ($0.02-$0.03).
     Don't give back gains waiting for a big move.
  3. ALWAYS have a stop loss on every position.
  4. Flatten immediately when a reversal print appears (engulfing, wick rejection).

Sub-strategies:
  V5a — Mean-Reversion Pullback:  Buy near/below 20MA when bouncing, sell near/above.
  V5b — VWAP Tag:  Buy first touch of VWAP from below after a dip, sell first tag from above.
  V5c — Micro-Scalp:  Tight $0.02-$0.03 trail on any quick profit.

Exit chain:
  Hard stop → Reversal flatten → Micro-trail → Breakeven → Giveback → TP → Time
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum

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
class V5Config:
    """V5 configuration — focused on safety and quick profits."""
    # Risk
    risk_per_trade_dollars: float = 50.0

    # ── 20MA Exhaustion Filter (THE big lesson) ──
    ma_period: int = 20
    # Max distance from 20MA (in ATR) to allow entry.
    # LONG: price must be within max_dist ABOVE 20MA, or BELOW 20MA (pullback).
    # SHORT: price must be within max_dist BELOW 20MA, or ABOVE 20MA (rally).
    max_ma_dist_atr: float = 0.5     # Only enter within 0.5 ATR of 20MA
    # If distance > exhaustion_dist_atr → OPPOSITE signal (fade the extension)
    exhaustion_dist_atr: float = 2.0  # Extended > 2 ATR → counter-trade signal

    # ── V5a: Mean-Reversion Pullback ──
    pullback_enabled: bool = True
    pullback_rsi_low: float = 40.0   # RSI zone for long entry
    pullback_rsi_high: float = 60.0  # RSI zone for short entry

    # ── V5b: VWAP Tag ──
    vwap_enabled: bool = True
    vwap_touch_atr: float = 0.3     # Within 0.3 ATR of VWAP = "tag"

    # ── V5c: Exhaustion Fade ──
    exhaustion_fade_enabled: bool = True  # Fade extended moves

    # ── Confirmations ──
    require_candle_confirm: bool = True   # Rejection/engulfing pattern
    require_volume: bool = True           # Above-average volume
    rvol_min: float = 0.8

    # ── Micro-Trail (THE big improvement) ──
    micro_trail_cents: float = 3.0    # $0.03 trail once in profit
    micro_trail_activate_cents: float = 5.0  # Activate after $0.05 profit/share

    # ── Standard exit chain ──
    hard_stop_r: float = 1.5
    trail_r: float = 1.0             # Standard trail distance (ATR)
    breakeven_r: float = 0.5         # BE at 0.5R (earlier than before)
    giveback_pct: float = 0.50       # Keep 50% of peak gain
    tp1_r: float = 1.0
    tp2_r: float = 2.5
    max_hold_bars: int = 60          # 60 min max
    reversal_flatten: bool = True    # Flatten on reversal candle

    # Slippage & commission
    slippage_cents: float = 1.0
    commission_per_share: float = 0.005

    # Direction
    allow_long: bool = True
    allow_short: bool = True

    # Price filter
    min_price: float = 8.0
    max_price: float = 1000.0


@dataclass
class V5Signal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    entry_type: str            # "PULLBACK" / "VWAP_TAG" / "EXHAUSTION_FADE"
    ma_distance_atr: float     # How far from 20MA at entry


@dataclass
class V5TradeResult:
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


class StrategyV5:
    """V5 Smart Mean-Reversion with 20MA filter and micro-trailing."""

    def __init__(self, cfg: V5Config | None = None):
        self.cfg = cfg or V5Config()

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[V5Signal]:
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[V5Signal] = []

        # HTF bias (lightweight)
        htf_bias = self._htf_bias(df_1h, df_1d)

        for i in range(50, len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            atr_val = row.get("ATR_14", np.nan)
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            price = row["Close"]
            if price < cfg.min_price or price > cfg.max_price:
                continue

            # ── 20MA distance calculation ──
            sma_20 = row.get("SMA_20", np.nan)
            if pd.isna(sma_20) or sma_20 <= 0:
                continue

            ma_dist = (price - sma_20) / atr_val  # +ve = above MA, -ve = below

            # ── Volume filter ──
            rvol = row.get("RVOL", 1.0)
            if pd.isna(rvol):
                rvol = 1.0
            if cfg.require_volume and rvol < cfg.rvol_min:
                continue

            # ── RSI ──
            rsi_val = row.get("RSI_14", 50.0)
            if pd.isna(rsi_val):
                rsi_val = 50.0

            # ── VWAP ──
            vwap_val = row.get("VWAP", np.nan)

            # ── Candle confirmation ──
            candle_bull = False
            candle_bear = False
            if cfg.require_candle_confirm:
                # Bullish: close > open AND close in upper 60% of range
                bar_range = row["High"] - row["Low"]
                if bar_range > 0:
                    body_pos = (price - row["Low"]) / bar_range
                    candle_bull = (price > row["Open"] and body_pos > 0.4)
                    candle_bear = (price < row["Open"] and body_pos < 0.6)
            else:
                candle_bull = True
                candle_bear = True

            # ═══════════════════════════════════════════════════════════════
            # ENTRY LOGIC — THE KEY RULES
            # ═══════════════════════════════════════════════════════════════

            # ── V5a: Mean-Reversion Pullback to 20MA ──
            if cfg.pullback_enabled:
                # LONG: Price is NEAR or BELOW 20MA (on discount), bouncing up
                if (cfg.allow_long and
                    ma_dist <= cfg.max_ma_dist_atr and  # Not extended above MA
                    ma_dist >= -cfg.exhaustion_dist_atr and  # Not crashed too far
                    rsi_val < cfg.pullback_rsi_low and  # RSI showing pullback
                    candle_bull and  # Bullish candle confirm
                    htf_bias != "BEAR"):

                    sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                           "PULLBACK", ma_dist)
                    if sig:
                        signals.append(sig)
                        continue

                # SHORT: Price is NEAR or ABOVE 20MA (at premium), dropping
                if (cfg.allow_short and
                    ma_dist >= -cfg.max_ma_dist_atr and  # Not extended below MA
                    ma_dist <= cfg.exhaustion_dist_atr and  # Not rallied too far
                    rsi_val > cfg.pullback_rsi_high and  # RSI showing overbought
                    candle_bear and  # Bearish candle confirm
                    htf_bias != "BULL"):

                    sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                           "PULLBACK", ma_dist)
                    if sig:
                        signals.append(sig)
                        continue

            # ── V5b: VWAP Tag ──
            if cfg.vwap_enabled and pd.notna(vwap_val) and vwap_val > 0:
                vwap_dist = abs(price - vwap_val) / atr_val

                if vwap_dist < cfg.vwap_touch_atr:
                    # Price touching VWAP — check direction
                    # LONG: price was below VWAP, now tagging it from below
                    prev_below_vwap = prev["Close"] < prev.get("VWAP", vwap_val)
                    prev_above_vwap = prev["Close"] > prev.get("VWAP", vwap_val)

                    if (cfg.allow_long and prev_below_vwap and
                        price >= vwap_val and  # Crossing up
                        ma_dist <= cfg.max_ma_dist_atr and  # Not extended!
                        candle_bull and htf_bias != "BEAR"):

                        sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                               "VWAP_TAG", ma_dist)
                        if sig:
                            signals.append(sig)
                            continue

                    if (cfg.allow_short and prev_above_vwap and
                        price <= vwap_val and  # Crossing down
                        ma_dist >= -cfg.max_ma_dist_atr and  # Not extended!
                        candle_bear and htf_bias != "BULL"):

                        sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                               "VWAP_TAG", ma_dist)
                        if sig:
                            signals.append(sig)
                            continue

            # ── V5c: Exhaustion Fade ──
            if cfg.exhaustion_fade_enabled:
                # Price extended FAR above 20MA → SHORT the exhaustion
                if (cfg.allow_short and
                    ma_dist > cfg.exhaustion_dist_atr and
                    candle_bear and  # Bearish reversal forming
                    rsi_val > 65 and  # Overbought
                    htf_bias != "BULL"):

                    sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                           "EXHAUSTION_FADE", ma_dist)
                    if sig:
                        signals.append(sig)
                        continue

                # Price extended FAR below 20MA → LONG the exhaustion
                if (cfg.allow_long and
                    ma_dist < -cfg.exhaustion_dist_atr and
                    candle_bull and  # Bullish reversal forming
                    rsi_val < 35 and  # Oversold
                    htf_bias != "BEAR"):

                    sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                           "EXHAUSTION_FADE", ma_dist)
                    if sig:
                        signals.append(sig)
                        continue

        return signals

    def _make_signal(self, i: int, enriched: pd.DataFrame, side: Side,
                     atr_val: float, entry_type: str,
                     ma_dist: float) -> V5Signal | None:
        cfg = self.cfg
        price = enriched.iloc[i]["Close"]
        stop_dist = cfg.hard_stop_r * atr_val

        if side == Side.LONG:
            stop_price = price - stop_dist
        else:
            stop_price = price + stop_dist

        risk_per_share = stop_dist
        if risk_per_share <= 0:
            return None

        pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

        return V5Signal(
            bar_index=i,
            timestamp=enriched.index[i],
            side=side,
            entry_price=price,
            stop_price=stop_price,
            risk_per_share=risk_per_share,
            position_size=pos_size,
            atr_value=atr_val,
            entry_type=entry_type,
            ma_distance_atr=ma_dist,
        )

    def simulate_trade(self, signal: V5Signal,
                       df_trigger: pd.DataFrame) -> V5TradeResult | None:
        """Simulate with micro-trailing and reversal flatten."""
        cfg = self.cfg
        side = signal.side
        entry_price = signal.entry_price + (
            cfg.slippage_cents / 100.0 if side == Side.LONG
            else -cfg.slippage_cents / 100.0
        )
        stop_price = signal.stop_price
        risk_per_share = signal.risk_per_share
        pos_size = signal.position_size

        peak_price = entry_price
        trough_price = entry_price
        breakeven_activated = False
        micro_trail_active = False
        micro_trail_price = 0.0
        trailing_stop = stop_price
        exit_reason = None
        exit_price = entry_price
        exit_bar = signal.bar_index

        for j in range(signal.bar_index + 1,
                       min(signal.bar_index + cfg.max_hold_bars + 1,
                           len(df_trigger))):
            bar = df_trigger.iloc[j]
            price = bar["Close"]
            high = bar["High"]
            low = bar["Low"]

            if side == Side.LONG:
                unrealized_r = (price - entry_price) / risk_per_share
                peak_price = max(peak_price, high)
                peak_r = (peak_price - entry_price) / risk_per_share
                profit_cents = (price - entry_price) * 100
            else:
                unrealized_r = (entry_price - price) / risk_per_share
                trough_price = min(trough_price, low) if trough_price > 0 else low
                peak_price = trough_price  # For shorts, "peak" = lowest
                peak_r = (entry_price - trough_price) / risk_per_share
                profit_cents = (entry_price - price) * 100

            # ── 1. HARD STOP ──
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

            # ── 2. REVERSAL FLATTEN ──
            if cfg.reversal_flatten and unrealized_r > 0.2:
                # Detect reversal candle (engulfing / big wick)
                bar_range = high - low
                if bar_range > 0:
                    if side == Side.LONG:
                        # Bearish engulfing or long upper wick
                        upper_wick = (high - max(price, bar["Open"])) / bar_range
                        is_reversal = (price < bar["Open"] and
                                      bar_range > 1.5 * risk_per_share * 0.5) or upper_wick > 0.6
                    else:
                        lower_wick = (min(price, bar["Open"]) - low) / bar_range
                        is_reversal = (price > bar["Open"] and
                                      bar_range > 1.5 * risk_per_share * 0.5) or lower_wick > 0.6

                    if is_reversal:
                        exit_price = price
                        exit_reason = ExitReason.REVERSAL_FLATTEN
                        exit_bar = j
                        break

            # ── 3. MICRO-TRAIL ($0.02-$0.03) ──
            if profit_cents >= cfg.micro_trail_activate_cents * 100:
                if not micro_trail_active:
                    micro_trail_active = True
                # Update micro trail
                if side == Side.LONG:
                    micro_trail_price = price - (cfg.micro_trail_cents / 100.0)
                    if low <= micro_trail_price:
                        exit_price = micro_trail_price
                        exit_reason = ExitReason.MICRO_TRAIL
                        exit_bar = j
                        break
                else:
                    micro_trail_price = price + (cfg.micro_trail_cents / 100.0)
                    if high >= micro_trail_price:
                        exit_price = micro_trail_price
                        exit_reason = ExitReason.MICRO_TRAIL
                        exit_bar = j
                        break

            # ── 4. TP2 full close ──
            if unrealized_r >= cfg.tp2_r:
                exit_price = price
                exit_reason = ExitReason.TP2
                exit_bar = j
                break

            # ── 5. GIVEBACK ──
            if peak_r > 0.3 and unrealized_r > 0:
                giveback = (peak_r - unrealized_r) / peak_r
                if giveback >= cfg.giveback_pct:
                    exit_price = price
                    exit_reason = ExitReason.GIVEBACK
                    exit_bar = j
                    break

            # ── 6. BREAKEVEN ──
            if not breakeven_activated and unrealized_r >= cfg.breakeven_r:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price + 0.01)
                else:
                    stop_price = min(stop_price, entry_price - 0.01)

            # ── 7. TRAILING (standard ATR trail) ──
            if breakeven_activated:
                trail_dist = cfg.trail_r * signal.atr_value
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
                    trailing_stop = min(trailing_stop, new_trail) if trailing_stop > 0 else new_trail
                    if high >= trailing_stop:
                        exit_price = trailing_stop
                        exit_reason = ExitReason.TRAILING
                        exit_bar = j
                        break

        # Time stop
        if exit_reason is None:
            exit_price = price
            exit_reason = ExitReason.TIME_STOP
            exit_bar = min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)

        # PnL
        if side == Side.LONG:
            pnl_raw = (exit_price - entry_price) * pos_size
        else:
            pnl_raw = (entry_price - exit_price) * pos_size
        commission = cfg.commission_per_share * pos_size * 2
        pnl = pnl_raw - commission
        pnl_r = pnl / (risk_per_share * pos_size) if risk_per_share > 0 else 0

        return V5TradeResult(
            entry_bar=signal.bar_index,
            exit_bar=exit_bar,
            entry_time=df_trigger.index[signal.bar_index],
            exit_time=df_trigger.index[min(exit_bar, len(df_trigger) - 1)],
            side=side,
            entry_price=entry_price,
            exit_price=exit_price,
            stop_price=signal.stop_price,
            position_size=pos_size,
            pnl=pnl,
            pnl_r=pnl_r,
            exit_reason=exit_reason,
            peak_r=peak_r,
            bars_held=exit_bar - signal.bar_index,
            entry_type=signal.entry_type,
        )

    def _htf_bias(self, df_1h, df_1d) -> str:
        """Lightweight higher-TF trend check."""
        scores = []
        for df in [df_1h, df_1d]:
            if df is None or len(df) < 50:
                continue
            enriched = enrich_with_indicators(df)
            last = enriched.iloc[-1]
            prev = enriched.iloc[-2] if len(enriched) > 1 else last
            ema_score = 1 if last["EMA_21"] > prev["EMA_21"] else -1
            macd_score = 1 if last.get("MACD_Hist", 0) > 0 else -1
            scores.append(ema_score + macd_score)
        if not scores:
            return "NEUTRAL"
        avg = sum(scores) / len(scores)
        if avg >= 1:
            return "BULL"
        elif avg <= -1:
            return "BEAR"
        return "NEUTRAL"
