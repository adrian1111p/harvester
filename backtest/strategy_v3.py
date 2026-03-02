"""
strategy_v3.py — V3 "VWAP Reversion + L2 Liquidity" Strategy.

Designed for $10–$50 stocks with good volume.  Uses price-structure entries
(not just oscillators) and wider ATR-based stops calibrated for cheaper stocks.

Three sub-strategies in one:
  V3a  VWAP Mean Reversion   — buy/sell when stretched > 1.5 ATR from VWAP
  V3b  Bollinger Band Bounce  — buy lower band touch, sell upper band touch
  V3c  Keltner Squeeze Break  — trade the expansion after a squeeze

All share L2-proxy filters and the same exit engine.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

import numpy as np
import pandas as pd

from backtest.indicators import (
    atr,
    bollinger_bands,
    donchian_channels,
    ema,
    enrich_with_indicators,
    keltner_channels,
    l2_liquidity_score,
    mfi,
    order_flow_imbalance,
    relative_volume,
    rsi,
    spread_proxy,
    stochastic,
    volume_acceleration,
    vwap,
    williams_r,
)


class Side(Enum):
    LONG = "LONG"
    SHORT = "SHORT"


class ExitReason(Enum):
    HARD_STOP = "HARD_STOP"
    TRAILING = "TRAILING"
    TP1 = "TP1"
    TP2 = "TP2"
    TIME_STOP = "TIME_STOP"
    VWAP_TARGET = "VWAP_TARGET"
    MIDLINE = "MIDLINE"


@dataclass
class V3Config:
    """V3 Strategy parameters — calibrated for $10-$50 stocks."""
    # Risk
    risk_per_trade_dollars: float = 50.0
    account_size: float = 25_000.0

    # Price filter
    min_price: float = 8.0
    max_price: float = 50.0

    # ── V3a: VWAP Reversion ──
    vwap_stretch_atr: float = 1.5          # Entry when price is >N ATR from VWAP
    vwap_enabled: bool = True

    # ── V3b: BB Bounce ──
    bb_entry_pctb_low: float = 0.05        # BB %B below this = long entry zone
    bb_entry_pctb_high: float = 0.95       # BB %B above this = short entry zone
    bb_enabled: bool = True

    # ── V3c: Keltner Squeeze ──
    squeeze_enabled: bool = True           # Enable squeeze-breakout entries
    squeeze_bars: int = 10                 # BB inside KC for N bars = squeeze

    # ── L2 Proxy Filters ──
    l2_liquidity_min: float = 25.0
    spread_z_max: float = 2.0             # More lenient than V2
    vol_accel_min: float = -0.3            # Only avoid severe volume drops
    rvol_min: float = 0.5                  # Low threshold — cycles happen on normal vol

    # ── Confirmations ──
    rsi_oversold: float = 35.0             # More lenient (not 30)
    rsi_overbought: float = 65.0
    require_volume_confirm: bool = True    # Volume must be above average

    # ── Exit rules — WIDER stops for cheap stocks ──
    hard_stop_r: float = 1.5              # 1.5 ATR stop (wider for noise)
    trail_r: float = 1.0                  # Trail at 1 ATR from peak
    giveback_pct: float = 0.60
    tp1_r: float = 1.0                    # Quick 1R TP
    tp1_scale_pct: float = 0.50
    tp2_r: float = 2.5                    # TP2 at 2.5R
    breakeven_r: float = 0.8
    max_hold_bars: int = 90               # 90 min

    # Slippage & commission (higher for cheap stocks)
    slippage_cents: float = 1.5
    commission_per_share: float = 0.005

    # Direction
    allow_long: bool = True
    allow_short: bool = True


@dataclass
class V3Signal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    entry_type: str           # "VWAP" / "BB" / "SQUEEZE"
    l2_liquidity: float
    ofi_signal: float


@dataclass
class V3TradeResult:
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


class StrategyV3:
    """V3 VWAP Reversion + BB Bounce + Keltner Squeeze with L2 proxy."""

    def __init__(self, cfg: V3Config | None = None):
        self.cfg = cfg or V3Config()

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[V3Signal]:
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[V3Signal] = []

        # HTF guard (light)
        htf_bias = self._htf_guard(df_1h, df_1d)

        # Track squeeze state (BB inside KC)
        squeeze_count = 0

        for i in range(50, len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            atr_val = row.get("ATR_14", np.nan)
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            price = row["Close"]

            # ── Price filter ──
            if price < cfg.min_price or price > cfg.max_price:
                continue

            # ── L2 proxy filters ──
            l2_liq = row.get("L2_Liquidity", 50.0)
            if pd.isna(l2_liq):
                l2_liq = 50.0
            if l2_liq < cfg.l2_liquidity_min:
                continue

            spread_z = row.get("Spread_Z", 0.0)
            if pd.isna(spread_z):
                spread_z = 0.0
            if spread_z > cfg.spread_z_max:
                continue

            rvol = row.get("RVOL", 1.0)
            if pd.notna(rvol) and rvol < cfg.rvol_min:
                continue

            ofi_sig = row.get("OFI_Signal", 0.0)
            if pd.isna(ofi_sig):
                ofi_sig = 0.0

            # ── Read key indicators ──
            vwap_val = row.get("VWAP", np.nan)
            bb_pctb = row.get("BB_PctB", 0.5)
            rsi_val = row.get("RSI_14", 50.0)
            stoch_k = row.get("Stoch_K", 50.0)
            kc_upper = row.get("KC_Upper", np.nan)
            kc_lower = row.get("KC_Lower", np.nan)
            bb_upper = row.get("BB_Upper", np.nan)
            bb_lower = row.get("BB_Lower", np.nan)

            # Track squeeze (BB inside KC)
            if (pd.notna(bb_upper) and pd.notna(kc_upper) and
                bb_upper < kc_upper and bb_lower > kc_lower):
                squeeze_count += 1
            else:
                was_squeezed = squeeze_count >= cfg.squeeze_bars
                squeeze_count = 0

                # ── V3c: Squeeze breakout ──
                if cfg.squeeze_enabled and was_squeezed:
                    # Breakout direction from close vs KC mid
                    kc_mid = row.get("KC_Mid", np.nan)
                    if pd.notna(kc_mid):
                        if price > kc_mid and cfg.allow_long and htf_bias != "STRONG_BEAR":
                            sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                                     "SQUEEZE", l2_liq, ofi_sig)
                            if sig:
                                signals.append(sig)
                                continue
                        elif price < kc_mid and cfg.allow_short and htf_bias != "STRONG_BULL":
                            sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                                     "SQUEEZE", l2_liq, ofi_sig)
                            if sig:
                                signals.append(sig)
                                continue

            # ── V3a: VWAP Reversion ──
            if cfg.vwap_enabled and pd.notna(vwap_val) and vwap_val > 0:
                dist_from_vwap = (price - vwap_val) / atr_val

                # Stretched below VWAP → LONG (price will revert up)
                if (dist_from_vwap < -cfg.vwap_stretch_atr and
                    cfg.allow_long and htf_bias != "STRONG_BEAR"):
                    # Confirm: RSI oversold zone + OFI positive
                    if (pd.notna(rsi_val) and rsi_val < cfg.rsi_oversold and
                        ofi_sig > 0):
                        sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                                 "VWAP", l2_liq, ofi_sig)
                        if sig:
                            signals.append(sig)
                            continue

                # Stretched above VWAP → SHORT
                if (dist_from_vwap > cfg.vwap_stretch_atr and
                    cfg.allow_short and htf_bias != "STRONG_BULL"):
                    if (pd.notna(rsi_val) and rsi_val > cfg.rsi_overbought and
                        ofi_sig < 0):
                        sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                                 "VWAP", l2_liq, ofi_sig)
                        if sig:
                            signals.append(sig)
                            continue

            # ── V3b: BB Bounce ──
            if cfg.bb_enabled and pd.notna(bb_pctb):
                # Bounce off lower band → LONG
                if (bb_pctb < cfg.bb_entry_pctb_low and
                    cfg.allow_long and htf_bias != "STRONG_BEAR"):
                    # Confirm: price starting to recover (close > open) or stoch turning up
                    if (price > row["Open"] or
                        (pd.notna(stoch_k) and stoch_k < 25 and
                         pd.notna(row.get("Stoch_D")) and stoch_k > row["Stoch_D"])):
                        sig = self._make_signal(i, enriched, Side.LONG, atr_val,
                                                 "BB", l2_liq, ofi_sig)
                        if sig:
                            signals.append(sig)
                            continue

                # Bounce off upper band → SHORT
                if (bb_pctb > cfg.bb_entry_pctb_high and
                    cfg.allow_short and htf_bias != "STRONG_BULL"):
                    if (price < row["Open"] or
                        (pd.notna(stoch_k) and stoch_k > 75 and
                         pd.notna(row.get("Stoch_D")) and stoch_k < row["Stoch_D"])):
                        sig = self._make_signal(i, enriched, Side.SHORT, atr_val,
                                                 "BB", l2_liq, ofi_sig)
                        if sig:
                            signals.append(sig)
                            continue

        return signals

    def _make_signal(self, i, enriched, side, atr_val, entry_type, l2_liq, ofi_sig):
        """Construct a signal with proper stop/sizing."""
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

        return V3Signal(
            bar_index=i,
            timestamp=enriched.index[i],
            side=side,
            entry_price=price,
            stop_price=stop_price,
            risk_per_share=risk_per_share,
            position_size=pos_size,
            atr_value=atr_val,
            entry_type=entry_type,
            l2_liquidity=l2_liq,
            ofi_signal=ofi_sig,
        )

    def simulate_trade(self, signal: V3Signal, df_trigger: pd.DataFrame) -> V3TradeResult | None:
        """Simulate trade with wider stops and mean-reversion targets."""
        cfg = self.cfg
        side = signal.side
        entry_price = signal.entry_price + (
            cfg.slippage_cents / 100.0 if side == Side.LONG else -cfg.slippage_cents / 100.0
        )
        stop_price = signal.stop_price
        risk_per_share = signal.risk_per_share
        pos_size = signal.position_size

        peak_price = entry_price
        trough_price = entry_price
        breakeven_activated = False
        trailing_stop = stop_price
        exit_reason = None
        exit_price = entry_price
        exit_bar = signal.bar_index

        for j in range(signal.bar_index + 1,
                       min(signal.bar_index + cfg.max_hold_bars + 1, len(df_trigger))):
            bar = df_trigger.iloc[j]
            price = bar["Close"]
            high = bar["High"]
            low = bar["Low"]

            if side == Side.LONG:
                peak_price = max(peak_price, high)
            else:
                trough_price = min(trough_price, low)

            # Hard stop
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

            unrealized_r = (
                (price - entry_price) / risk_per_share if side == Side.LONG
                else (entry_price - price) / risk_per_share
            )
            peak_r = (
                (peak_price - entry_price) / risk_per_share if side == Side.LONG
                else (entry_price - trough_price) / risk_per_share
            )

            # TP2
            if unrealized_r >= cfg.tp2_r:
                exit_price = price
                exit_reason = ExitReason.TP2
                exit_bar = j
                break

            # TP1 → tighten to BE
            if unrealized_r >= cfg.tp1_r and not breakeven_activated:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                    trailing_stop = max(trailing_stop, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)
                    trailing_stop = min(trailing_stop, entry_price)

            # Break-even
            if not breakeven_activated and unrealized_r >= cfg.breakeven_r:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)

            # Trailing
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

            # Giveback
            if peak_r > 0.5:
                giveback = (peak_r - unrealized_r) / peak_r
                if giveback >= cfg.giveback_pct and unrealized_r > 0:
                    exit_price = price
                    exit_reason = ExitReason.TRAILING
                    exit_bar = j
                    break

            # Time stop
            if (j - signal.bar_index) >= cfg.max_hold_bars:
                exit_price = price
                exit_reason = ExitReason.TIME_STOP
                exit_bar = j
                break
        else:
            last_idx = min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)
            exit_price = df_trigger.iloc[last_idx]["Close"]
            exit_reason = ExitReason.TIME_STOP
            exit_bar = last_idx

        # Slippage
        if side == Side.LONG:
            exit_price -= cfg.slippage_cents / 100.0
        else:
            exit_price += cfg.slippage_cents / 100.0

        pnl_per_share = (
            (exit_price - entry_price) if side == Side.LONG
            else (entry_price - exit_price)
        )
        commission = cfg.commission_per_share * pos_size * 2
        pnl = pnl_per_share * pos_size - commission
        pnl_r = pnl_per_share / risk_per_share if risk_per_share > 0 else 0.0

        return V3TradeResult(
            entry_bar=signal.bar_index,
            exit_bar=exit_bar,
            entry_time=signal.timestamp,
            exit_time=(
                df_trigger.index[exit_bar] if exit_bar < len(df_trigger)
                else signal.timestamp
            ),
            side=side,
            entry_price=entry_price,
            exit_price=exit_price,
            stop_price=signal.stop_price,
            position_size=pos_size,
            pnl=pnl,
            pnl_r=pnl_r,
            exit_reason=exit_reason,
            peak_r=(
                (peak_price - entry_price) / risk_per_share if side == Side.LONG
                else (entry_price - trough_price) / risk_per_share
            ),
            bars_held=exit_bar - signal.bar_index,
            entry_type=signal.entry_type,
        )

    def _htf_guard(self, df_1h, df_1d) -> str:
        """Light guard — avoid fighting extreme macro trends."""
        scores = []
        for df in [df_1h, df_1d]:
            if df is None or len(df) < 30:
                continue
            enriched = enrich_with_indicators(df)
            last = enriched.iloc[-1]
            prev = enriched.iloc[-2]
            slope = 1 if last["EMA_21"] > prev["EMA_21"] else -1
            rsi_val = last.get("RSI_14", 50.0)
            if pd.notna(rsi_val):
                if rsi_val > 70:
                    slope += 1
                elif rsi_val < 30:
                    slope -= 1
            scores.append(slope)
        if not scores:
            return "NEUTRAL"
        avg = sum(scores) / len(scores)
        if avg >= 2:
            return "STRONG_BULL"
        elif avg <= -2:
            return "STRONG_BEAR"
        return "NEUTRAL"
