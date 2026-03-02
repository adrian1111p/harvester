"""
strategy_cycle.py — V2 Cycle Strategy: Buy Low / Sell High with L1/L2 Proxy.

Core idea:
  Price oscillates in short-term cycles.  We buy near the bottom of each cycle
  (oversold + volume absorption + L2 bid strength) and sell near the top
  (overbought + L2 ask pressure weakening).

Designed for sub-$50 stocks with good volume and movement.

Entry logic (LONG — buy the dip):
  1. Stochastic %K < 20 AND Williams %R < -80 (oversold)
  2. BB %B < 0.1 OR price near KC lower band (band squeeze / washout)
  3. Order Flow Imbalance turning positive (L2 proxy: buyers stepping in)
  4. Volume acceleration > 0 (L2 proxy: volume surge at bottom)
  5. MFI divergence or recovering from < 20
  6. L2 Liquidity score > 40 (avoid thin-book stocks)

Entry logic (SHORT — sell the rip):
  1. Stochastic %K > 80 AND Williams %R > -20 (overbought)
  2. BB %B > 0.9 OR price near KC upper band
  3. Order Flow Imbalance turning negative (sellers stepping in)
  4. Volume acceleration > 0 at top
  5. MFI > 80 (money flowing out)
  6. L2 Liquidity score > 40

Exit logic (tight mean-reversion exits):
  - Hard stop: 1.0R (tight — we want to be wrong fast)
  - TP1 at midline (SMA20 / VWAP / Keltner mid) — scale 50%
  - TP2 at opposite band (BB upper for longs, lower for shorts)
  - Trailing: 0.8R from peak
  - Time stop: 60 bars max (1h on 1m chart)
  - Giveback: 50% of peak unrealized R
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Optional

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
    EOD = "EOD"
    MIDLINE = "MIDLINE"


@dataclass
class CycleConfig:
    """Tunable parameters for the Cycle Strategy."""
    # Risk
    risk_per_trade_dollars: float = 50.0
    account_size: float = 25_000.0

    # Entry — oversold / overbought thresholds
    stoch_oversold: float = 20.0
    stoch_overbought: float = 80.0
    willr_oversold: float = -80.0
    willr_overbought: float = -20.0
    bb_low_pctb: float = 0.15          # BB %B ≤ this = oversold
    bb_high_pctb: float = 0.85         # BB %B ≥ this = overbought
    mfi_oversold: float = 25.0
    mfi_overbought: float = 75.0

    # L2 proxy filters
    ofi_min_signal: float = 0.05       # min OFI signal for entry (positive = buying)
    vol_accel_min: float = 0.0         # min volume acceleration
    l2_liquidity_min: float = 30.0     # min L2 liquidity score
    spread_z_max: float = 1.5          # max spread z-score (avoid wide-spread bars)

    # RVOL filter
    rvol_min: float = 0.8              # Looser than trend strategy — cycles happen on normal vol

    # Price filter
    max_price: float = 50.0            # $30 rule — stay under this

    # Exit rules
    hard_stop_r: float = 1.0
    trail_r: float = 0.8
    giveback_pct: float = 0.50
    tp1_r: float = 1.0                 # Quick TP1 at 1R (mean reversion — grab it)
    tp1_scale_pct: float = 0.50
    tp2_r: float = 2.0                 # TP2 at opposite band
    max_hold_bars: int = 60            # 60 min max hold (on 1m bars)
    breakeven_r: float = 0.7           # Tighter BE trigger

    # Slippage & commission
    slippage_cents: float = 1.0
    commission_per_share: float = 0.005

    # Direction control
    allow_long: bool = True
    allow_short: bool = True


@dataclass
class CycleSignal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    # L2 proxy context
    ofi_signal: float
    l2_liquidity: float
    stoch_k: float
    bb_pctb: float


@dataclass
class CycleTradeResult:
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


class CycleStrategyV2:
    """
    Buy Low / Sell High cycle-exploitation strategy with L2 proxies.
    Designed for liquid sub-$50 stocks with predictable microstructure.
    """

    def __init__(self, cfg: CycleConfig | None = None):
        self.cfg = cfg or CycleConfig()

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[CycleSignal]:
        """Scan for cycle turning points."""
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[CycleSignal] = []

        # Pre-compute higher-TF context for trend filter
        htf_trend = self._compute_htf_context(df_1h, df_1d)

        for i in range(50, len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            # Skip if ATR is NaN
            atr_val = row.get("ATR_14", np.nan)
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            # ── Price filter ──
            if row["Close"] > cfg.max_price:
                continue

            # ── L2 proxy checks ──
            l2_liq = row.get("L2_Liquidity", 50.0)
            if pd.isna(l2_liq) or l2_liq < cfg.l2_liquidity_min:
                continue

            spread_z = row.get("Spread_Z", 0.0)
            if pd.isna(spread_z):
                spread_z = 0.0
            if spread_z > cfg.spread_z_max:
                continue  # Spread too wide — bad L2

            # ── RVOL filter ──
            rvol = row.get("RVOL", 1.0)
            if pd.notna(rvol) and rvol < cfg.rvol_min:
                continue

            # ── Read oscillators ──
            stoch_k = row.get("Stoch_K", 50.0)
            stoch_d = row.get("Stoch_D", 50.0)
            willr = row.get("WillR_14", -50.0)
            bb_pctb = row.get("BB_PctB", 0.5)
            mfi_val = row.get("MFI_14", 50.0)
            ofi_signal = row.get("OFI_Signal", 0.0)
            vol_accel = row.get("Vol_Accel", 0.0)

            if any(pd.isna(v) for v in [stoch_k, willr, bb_pctb]):
                continue

            # ── LONG: Buy the dip ──
            if cfg.allow_long:
                oversold_stoch = stoch_k < cfg.stoch_oversold
                oversold_willr = willr < cfg.willr_oversold
                oversold_bb = bb_pctb < cfg.bb_low_pctb
                oversold_mfi = pd.notna(mfi_val) and mfi_val < cfg.mfi_oversold

                # Need at least 2 of 3 oscillator confirmations
                osc_score = sum([oversold_stoch, oversold_willr, oversold_bb])

                # L2 proxy: Order flow turning positive (buyers stepping in at bottom)
                ofi_ok = pd.notna(ofi_signal) and ofi_signal > cfg.ofi_min_signal

                # Don't fight the macro trend (skip longs in strong downtrend)
                trend_ok = htf_trend != "STRONG_BEAR"

                if osc_score >= 2 and ofi_ok and trend_ok:
                    entry_price = row["Close"]
                    stop_dist = cfg.hard_stop_r * atr_val
                    stop_price = entry_price - stop_dist
                    risk_per_share = stop_dist
                    pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

                    signals.append(CycleSignal(
                        bar_index=i,
                        timestamp=enriched.index[i],
                        side=Side.LONG,
                        entry_price=entry_price,
                        stop_price=stop_price,
                        risk_per_share=risk_per_share,
                        position_size=pos_size,
                        atr_value=atr_val,
                        ofi_signal=ofi_signal if pd.notna(ofi_signal) else 0.0,
                        l2_liquidity=l2_liq if pd.notna(l2_liq) else 0.0,
                        stoch_k=stoch_k,
                        bb_pctb=bb_pctb,
                    ))

            # ── SHORT: Sell the rip ──
            if cfg.allow_short:
                overbought_stoch = stoch_k > cfg.stoch_overbought
                overbought_willr = willr > cfg.willr_overbought
                overbought_bb = bb_pctb > cfg.bb_high_pctb
                overbought_mfi = pd.notna(mfi_val) and mfi_val > cfg.mfi_overbought

                osc_score = sum([overbought_stoch, overbought_willr, overbought_bb])

                # L2 proxy: Order flow turning negative (sellers stepping in at top)
                ofi_ok = pd.notna(ofi_signal) and ofi_signal < -cfg.ofi_min_signal

                trend_ok = htf_trend != "STRONG_BULL"

                if osc_score >= 2 and ofi_ok and trend_ok:
                    entry_price = row["Close"]
                    stop_dist = cfg.hard_stop_r * atr_val
                    stop_price = entry_price + stop_dist
                    risk_per_share = stop_dist
                    pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

                    signals.append(CycleSignal(
                        bar_index=i,
                        timestamp=enriched.index[i],
                        side=Side.SHORT,
                        entry_price=entry_price,
                        stop_price=stop_price,
                        risk_per_share=risk_per_share,
                        position_size=pos_size,
                        atr_value=atr_val,
                        ofi_signal=ofi_signal if pd.notna(ofi_signal) else 0.0,
                        l2_liquidity=l2_liq if pd.notna(l2_liq) else 0.0,
                        stoch_k=stoch_k,
                        bb_pctb=bb_pctb,
                    ))

        return signals

    def simulate_trade(
        self,
        signal: CycleSignal,
        df_trigger: pd.DataFrame,
    ) -> CycleTradeResult | None:
        """Simulate a single cycle trade with mean-reversion exit logic."""
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
        exit_reason: ExitReason | None = None
        exit_price = entry_price
        exit_bar = signal.bar_index

        for j in range(signal.bar_index + 1,
                       min(signal.bar_index + cfg.max_hold_bars + 1, len(df_trigger))):
            bar = df_trigger.iloc[j]
            price = bar["Close"]
            high = bar["High"]
            low = bar["Low"]

            # Track extremes
            if side == Side.LONG:
                peak_price = max(peak_price, high)
            else:
                trough_price = min(trough_price, low)

            # ── Hard stop ──
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
            unrealized_r = (
                (price - entry_price) / risk_per_share if side == Side.LONG
                else (entry_price - price) / risk_per_share
            )
            peak_r = (
                (peak_price - entry_price) / risk_per_share if side == Side.LONG
                else (entry_price - trough_price) / risk_per_share
            )

            # ── TP2: full close at opposite band ──
            if unrealized_r >= cfg.tp2_r:
                exit_price = price
                exit_reason = ExitReason.TP2
                exit_bar = j
                break

            # ── TP1: quick profit at 1R — tighten stop to BE ──
            if unrealized_r >= cfg.tp1_r and not breakeven_activated:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                    trailing_stop = max(trailing_stop, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)
                    trailing_stop = min(trailing_stop, entry_price)

            # ── Break-even ──
            if not breakeven_activated and unrealized_r >= cfg.breakeven_r:
                breakeven_activated = True
                if side == Side.LONG:
                    stop_price = max(stop_price, entry_price)
                else:
                    stop_price = min(stop_price, entry_price)

            # ── Trailing stop ──
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

            # ── Giveback ──
            if peak_r > 0:
                giveback = (peak_r - unrealized_r) / peak_r
                if giveback >= cfg.giveback_pct and unrealized_r > 0:
                    exit_price = price
                    exit_reason = ExitReason.TRAILING
                    exit_bar = j
                    break

            # ── Time stop ──
            bars_held = j - signal.bar_index
            if bars_held >= cfg.max_hold_bars:
                exit_price = price
                exit_reason = ExitReason.TIME_STOP
                exit_bar = j
                break
        else:
            exit_price = df_trigger.iloc[
                min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)
            ]["Close"]
            exit_reason = ExitReason.TIME_STOP
            exit_bar = min(signal.bar_index + cfg.max_hold_bars, len(df_trigger) - 1)

        # Slippage on exit
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

        return CycleTradeResult(
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
        )

    def _compute_htf_context(
        self,
        df_1h: pd.DataFrame | None,
        df_1d: pd.DataFrame | None,
    ) -> str:
        """Light HTF guard — just avoid fighting extreme macro trends."""
        scores = []
        for df in [df_1h, df_1d]:
            if df is None or len(df) < 30:
                continue
            enriched = enrich_with_indicators(df)
            last = enriched.iloc[-1]
            # Simple: EMA21 slope + RSI
            prev = enriched.iloc[-2]
            slope = 1 if last["EMA_21"] > prev["EMA_21"] else -1
            rsi_val = last["RSI_14"]
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
