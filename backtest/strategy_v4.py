"""
strategy_v4.py — V4 "Image Pattern" Strategy: Buy-Setup / Sell-Setup / 123 / Breakout / Exhaustion.

Directly implements the 6 pattern families documented in docs/img/:
  1. Buy Setup  (1-buy-setup): Rally → Pullback → Continuation entry
  2. Sell Setup  (2-sell-setup): Drop → Pullup → Breakdown entry (mirror)
  3. 123 Pattern (123-pattern): Higher-low reversal after pullback
  4. Breakout    (3-breakout):  Volume-confirmed break above resistance
  5. Breakdown   (4-breakdown): Volume-confirmed break below support (mirror)
  6. Exhaustion  (exhaustion_trade): Counter-trend entry after extended run

Uses L2 proxy indicators (OFI, spread-Z, volume accel, liquidity score)
and Conduct risk-management rules (risk-management images).

"Buy low, sell high" cycle approach:
  - Buy Setup + 123 + Exhaustion  = Buy the dip / reversal entries
  - Sell Setup + Breakdown        = Sell the rally / continuation entries
  - Breakout / Breakdown          = Momentum entries on expansion
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
    donchian_channels,
    ema,
    enrich_with_indicators,
    keltner_channels,
    l2_liquidity_score,
    macd,
    mfi,
    order_flow_imbalance,
    relative_volume,
    rsi,
    sma,
    spread_proxy,
    stochastic,
    supertrend,
    volume_acceleration,
    vwap,
    williams_r,
)


# ── Enums ────────────────────────────────────────────────────────────────────

class Side(Enum):
    LONG = "LONG"
    SHORT = "SHORT"


class ExitReason(Enum):
    HARD_STOP = "HARD_STOP"
    TRAILING = "TRAILING"
    TP1 = "TP1"
    TP2 = "TP2"
    TIME_STOP = "TIME_STOP"
    EXHAUSTION_EXIT = "EXHAUSTION_EXIT"
    SIGNAL_REVERSAL = "SIGNAL_REVERSAL"


class PatternType(Enum):
    BUY_SETUP = "BUY_SETUP"
    SELL_SETUP = "SELL_SETUP"
    PATTERN_123 = "123_PATTERN"
    BREAKOUT = "BREAKOUT"
    BREAKDOWN = "BREAKDOWN"
    EXHAUSTION = "EXHAUSTION"


# ── Config ───────────────────────────────────────────────────────────────────

@dataclass
class V4Config:
    """V4 Image Pattern Strategy configuration."""
    # Risk sizing
    risk_per_trade_dollars: float = 50.0
    account_size: float = 25_000.0

    # ── Pattern enables ──
    enable_buy_setup: bool = True
    enable_sell_setup: bool = True
    enable_123_pattern: bool = True
    enable_breakout: bool = True
    enable_breakdown: bool = True
    enable_exhaustion: bool = True

    # Direction
    allow_long: bool = True
    allow_short: bool = True

    # ── Buy/Sell Setup params (from C# AnalyzeBuySetupSignals) ──
    setup_lookback: int = 30              # Bars to look back for rally/drop structure
    retracement_min: float = 0.30         # Min pullback retracement (40% in images)
    retracement_max: float = 0.70         # Max pullback retracement (60% in images)
    pullback_bars_min: int = 3            # Min consecutive pullback bars
    sma_period: int = 20                  # SMA for trend direction
    require_volume_spike: bool = True     # Volume >= 1.5x avg at entry
    volume_spike_mult: float = 1.5        # Volume multiplier threshold
    min_rr_ratio: float = 1.5            # Min reward:risk ratio

    # ── 123 Pattern params ──
    p123_lookback: int = 30               # Lookback for swing points
    p123_higher_low_pct: float = 0.02     # Point 3 must be > Point 1 by this %

    # ── Breakout/Breakdown params ──
    breakout_lookback: int = 20           # Bars for resistance/support
    breakout_volume_mult: float = 1.2     # Volume >= 1.2x avg for confirmation
    breakout_atr_buffer: float = 0.3      # Close must exceed level by 0.3 ATR

    # ── Exhaustion params (from C# Tmg027) ──
    exhaustion_lookback: int = 15         # Bars of extended move
    exhaustion_min_move_atr: float = 3.0  # Min move in ATR multiples
    exhaustion_reversal_bars: int = 3     # Reversal bars needed to confirm

    # ── L2 Proxy Filters ──
    l2_liquidity_min: float = 20.0
    spread_z_max: float = 2.5
    rvol_min: float = 0.5
    ofi_confirm: bool = True              # Require OFI alignment

    # ── Enhanced score threshold ──
    enhanced_min_score: int = 3           # Min sub-criteria score (out of 7)

    # ── Exit rules (from risk-management images) ──
    hard_stop_r: float = 1.5
    breakeven_r: float = 1.0
    trail_r: float = 1.0
    giveback_pct: float = 0.60
    tp1_r: float = 1.5
    tp1_scale_pct: float = 0.50
    tp2_r: float = 3.0
    max_hold_bars: int = 120

    # Costs
    slippage_cents: float = 1.0
    commission_per_share: float = 0.005


# ── Signal / Result ──────────────────────────────────────────────────────────

@dataclass
class V4Signal:
    bar_index: int
    timestamp: pd.Timestamp
    side: Side
    entry_price: float
    stop_price: float
    risk_per_share: float
    position_size: int
    atr_value: float
    pattern: PatternType
    enhanced_score: int      # How many sub-criteria matched (0-7)
    l2_liquidity: float
    ofi_signal: float


@dataclass
class V4TradeResult:
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
    pattern: PatternType
    enhanced_score: int


# ── Strategy ─────────────────────────────────────────────────────────────────

class StrategyV4:
    """
    V4 Image Pattern Strategy.

    Implements all 6 pattern families from docs/img with L2 proxy filters
    and Conduct risk management.
    """

    def __init__(self, cfg: V4Config | None = None):
        self.cfg = cfg or V4Config()

    # ── Signal Generation ────────────────────────────────────────────────

    def generate_signals(
        self,
        df_trigger: pd.DataFrame,
        df_5m: pd.DataFrame | None = None,
        df_15m: pd.DataFrame | None = None,
        df_1h: pd.DataFrame | None = None,
        df_1d: pd.DataFrame | None = None,
    ) -> list[V4Signal]:
        cfg = self.cfg
        enriched = enrich_with_indicators(df_trigger)
        signals: list[V4Signal] = []

        # HTF bias for directional filter
        htf_bias = self._compute_htf_bias(df_1h, df_1d)

        lookback = max(cfg.setup_lookback, cfg.p123_lookback,
                       cfg.breakout_lookback, cfg.exhaustion_lookback)
        start_bar = max(50, lookback + 5)

        for i in range(start_bar, len(enriched)):
            row = enriched.iloc[i]
            prev = enriched.iloc[i - 1]

            atr_val = row.get("ATR_14", np.nan)
            if pd.isna(atr_val) or atr_val <= 0:
                continue

            price = row["Close"]

            # ── L2 Proxy gate ──
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

            # ── 1. BUY SETUP ──
            if cfg.enable_buy_setup and cfg.allow_long and htf_bias != "STRONG_BEAR":
                sig = self._check_buy_setup(i, enriched, atr_val, l2_liq, ofi_sig)
                if sig:
                    signals.append(sig)
                    continue

            # ── 2. SELL SETUP ──
            if cfg.enable_sell_setup and cfg.allow_short and htf_bias != "STRONG_BULL":
                sig = self._check_sell_setup(i, enriched, atr_val, l2_liq, ofi_sig)
                if sig:
                    signals.append(sig)
                    continue

            # ── 3. 123 PATTERN ──
            if cfg.enable_123_pattern:
                sig = self._check_123_pattern(i, enriched, atr_val, l2_liq,
                                               ofi_sig, htf_bias)
                if sig:
                    signals.append(sig)
                    continue

            # ── 4. BREAKOUT ──
            if cfg.enable_breakout and cfg.allow_long and htf_bias != "STRONG_BEAR":
                sig = self._check_breakout(i, enriched, atr_val, l2_liq, ofi_sig)
                if sig:
                    signals.append(sig)
                    continue

            # ── 5. BREAKDOWN ──
            if cfg.enable_breakdown and cfg.allow_short and htf_bias != "STRONG_BULL":
                sig = self._check_breakdown(i, enriched, atr_val, l2_liq, ofi_sig)
                if sig:
                    signals.append(sig)
                    continue

            # ── 6. EXHAUSTION ──
            if cfg.enable_exhaustion:
                sig = self._check_exhaustion(i, enriched, atr_val, l2_liq,
                                              ofi_sig, htf_bias)
                if sig:
                    signals.append(sig)
                    continue

        return signals

    # ── Pattern: Buy Setup ───────────────────────────────────────────────

    def _check_buy_setup(self, i: int, df: pd.DataFrame, atr_val: float,
                          l2_liq: float, ofi_sig: float) -> V4Signal | None:
        """
        Buy Setup from images: Rally → Pullback → Continuation.
        Replicated from C# AnalyzeBuySetupSignals.
        """
        cfg = self.cfg
        lb = cfg.setup_lookback
        if i < lb + 1:
            return None

        window = df.iloc[i - lb:i + 1]
        row = df.iloc[i]
        prev = df.iloc[i - 1]

        # Find peak high in lookback
        peak_idx = window["High"].idxmax()
        peak_pos = window.index.get_loc(peak_idx)
        peak_high = window["High"].iloc[peak_pos]

        # Need rally before peak and pullback after
        if peak_pos < 5 or peak_pos > lb - 3:
            return None

        rally_low = window["Low"].iloc[:peak_pos].min()
        rally_range = peak_high - rally_low
        if rally_range <= 0:
            return None

        # Pullback segment (after peak)
        pullback_seg = window.iloc[peak_pos:]
        pullback_low = pullback_seg["Low"].min()

        # Retracement check: pullback must retrace 30-70% of rally
        retracement = (peak_high - pullback_low) / rally_range
        if not (cfg.retracement_min <= retracement <= cfg.retracement_max):
            return None

        # Pullback structure: check for red bars or lower highs
        pb_bars = pullback_seg.iloc[1:]  # After peak
        if len(pb_bars) < cfg.pullback_bars_min:
            return None

        red_count = sum(1 for _, b in pb_bars.iterrows()
                       if b["Close"] < b["Open"])
        lower_highs = sum(1 for j in range(1, len(pb_bars))
                         if pb_bars["High"].iloc[j] < pb_bars["High"].iloc[j - 1])

        has_pullback_structure = (red_count >= cfg.pullback_bars_min or
                                  lower_highs >= cfg.pullback_bars_min - 1)
        if not has_pullback_structure:
            return None

        # SMA(20) rising and price above SMA(20)
        sma_val = row.get("SMA_20", np.nan)
        prev_sma = prev.get("SMA_20", np.nan)
        if pd.isna(sma_val) or pd.isna(prev_sma):
            return None
        if sma_val <= prev_sma:  # SMA must be rising
            return None
        if row["Close"] < sma_val:  # Price above SMA
            return None

        # Pullback completed: current bar closes green and above prior high
        is_green = row["Close"] > row["Open"]
        closes_above_prior = row["Close"] > prev["High"]
        if not (is_green and closes_above_prior):
            return None

        # ── Enhanced sub-criteria scoring (7 signals) ──
        score = 0

        # 1) Entry bar quality: narrow range or doji or bottoming tail
        bar_range = row["High"] - row["Low"]
        avg_range = atr_val
        if bar_range <= avg_range * 0.85:
            score += 1
        lower_wick = min(row["Open"], row["Close"]) - row["Low"]
        if bar_range > 0 and lower_wick / bar_range >= 0.35:
            score += 1

        # 2) Contracting pullback ranges
        if len(pb_bars) >= 3:
            ranges = [(pb_bars["High"].iloc[j] - pb_bars["Low"].iloc[j])
                      for j in range(len(pb_bars))]
            contracting = all(ranges[j] <= ranges[j - 1] for j in range(1, min(4, len(ranges))))
            if contracting:
                score += 1

        # 3) R:R ratio >= 1.5
        stop_price = pullback_low - 0.02 * rally_range  # Tiny buffer below pullback low
        target = peak_high
        risk = row["Close"] - stop_price
        reward = target - row["Close"]
        if risk > 0 and reward / risk >= cfg.min_rr_ratio:
            score += 1

        # 4) Volume spike at entry
        vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
        if vol_avg > 0 and row["Volume"] >= vol_avg * cfg.volume_spike_mult:
            score += 1

        # 5) OFI positive (buy pressure)
        if cfg.ofi_confirm and ofi_sig > 0:
            score += 1

        # 6) RSI not overbought (room to run)
        rsi_val = row.get("RSI_14", 50.0)
        if pd.notna(rsi_val) and 30 <= rsi_val <= 65:
            score += 1

        if score < cfg.enhanced_min_score:
            return None

        # ── Build signal ──
        entry_price = row["Close"]
        risk_per_share = abs(entry_price - stop_price)
        if risk_per_share <= 0:
            return None
        pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

        return V4Signal(
            bar_index=i, timestamp=df.index[i], side=Side.LONG,
            entry_price=entry_price, stop_price=stop_price,
            risk_per_share=risk_per_share, position_size=pos_size,
            atr_value=atr_val, pattern=PatternType.BUY_SETUP,
            enhanced_score=score, l2_liquidity=l2_liq, ofi_signal=ofi_sig,
        )

    # ── Pattern: Sell Setup (Mirror of Buy Setup) ────────────────────────

    def _check_sell_setup(self, i: int, df: pd.DataFrame, atr_val: float,
                           l2_liq: float, ofi_sig: float) -> V4Signal | None:
        """
        Sell Setup from images: Drop → Pullup → Breakdown entry.
        Mirror of Buy Setup.
        """
        cfg = self.cfg
        lb = cfg.setup_lookback
        if i < lb + 1:
            return None

        window = df.iloc[i - lb:i + 1]
        row = df.iloc[i]
        prev = df.iloc[i - 1]

        # Find trough low in lookback
        trough_idx = window["Low"].idxmin()
        trough_pos = window.index.get_loc(trough_idx)
        trough_low = window["Low"].iloc[trough_pos]

        # Need drop before trough and pullup after
        if trough_pos < 5 or trough_pos > lb - 3:
            return None

        drop_high = window["High"].iloc[:trough_pos].max()
        drop_range = drop_high - trough_low
        if drop_range <= 0:
            return None

        # Pullup segment (after trough)
        pullup_seg = window.iloc[trough_pos:]
        pullup_high = pullup_seg["High"].max()

        # Retracement check
        retracement = (pullup_high - trough_low) / drop_range
        if not (cfg.retracement_min <= retracement <= cfg.retracement_max):
            return None

        # Pullup structure: green bars or higher lows
        pu_bars = pullup_seg.iloc[1:]
        if len(pu_bars) < cfg.pullback_bars_min:
            return None

        green_count = sum(1 for _, b in pu_bars.iterrows()
                         if b["Close"] > b["Open"])
        higher_lows = sum(1 for j in range(1, len(pu_bars))
                         if pu_bars["Low"].iloc[j] > pu_bars["Low"].iloc[j - 1])

        has_pullup_structure = (green_count >= cfg.pullback_bars_min or
                                higher_lows >= cfg.pullback_bars_min - 1)
        if not has_pullup_structure:
            return None

        # SMA(20) falling and price below SMA(20)
        sma_val = row.get("SMA_20", np.nan)
        prev_sma = prev.get("SMA_20", np.nan)
        if pd.isna(sma_val) or pd.isna(prev_sma):
            return None
        if sma_val >= prev_sma:  # SMA must be falling
            return None
        if row["Close"] > sma_val:  # Price below SMA
            return None

        # Breakdown bar: current closes red and below prior bar's low
        is_red = row["Close"] < row["Open"]
        closes_below_prior = row["Close"] < prev["Low"]
        if not (is_red and closes_below_prior):
            return None

        # ── Enhanced scoring ──
        score = 0

        # 1) Breakdown bar quality
        bar_range = row["High"] - row["Low"]
        upper_wick = row["High"] - max(row["Open"], row["Close"])
        if bar_range > 0 and upper_wick / bar_range >= 0.35:
            score += 1

        # 2) Contracting pullup ranges
        if len(pu_bars) >= 3:
            ranges = [(pu_bars["High"].iloc[j] - pu_bars["Low"].iloc[j])
                      for j in range(len(pu_bars))]
            contracting = all(ranges[j] <= ranges[j - 1]
                             for j in range(1, min(4, len(ranges))))
            if contracting:
                score += 1

        # 3) R:R ratio
        stop_price = pullup_high + 0.02 * drop_range
        target = trough_low
        risk = stop_price - row["Close"]
        reward = row["Close"] - target
        if risk > 0 and reward / risk >= cfg.min_rr_ratio:
            score += 1

        # 4) Volume spike
        vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
        if vol_avg > 0 and row["Volume"] >= vol_avg * cfg.volume_spike_mult:
            score += 1

        # 5) OFI negative (sell pressure)
        if cfg.ofi_confirm and ofi_sig < 0:
            score += 1

        # 6) RSI not oversold (room to drop)
        rsi_val = row.get("RSI_14", 50.0)
        if pd.notna(rsi_val) and 35 <= rsi_val <= 70:
            score += 1

        if score < cfg.enhanced_min_score:
            return None

        entry_price = row["Close"]
        risk_per_share = abs(stop_price - entry_price)
        if risk_per_share <= 0:
            return None
        pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

        return V4Signal(
            bar_index=i, timestamp=df.index[i], side=Side.SHORT,
            entry_price=entry_price, stop_price=stop_price,
            risk_per_share=risk_per_share, position_size=pos_size,
            atr_value=atr_val, pattern=PatternType.SELL_SETUP,
            enhanced_score=score, l2_liquidity=l2_liq, ofi_signal=ofi_sig,
        )

    # ── Pattern: 123 ────────────────────────────────────────────────────

    def _check_123_pattern(self, i: int, df: pd.DataFrame, atr_val: float,
                            l2_liq: float, ofi_sig: float,
                            htf_bias: str) -> V4Signal | None:
        """
        123 Pattern from images:
        Long: (1) swing low → (2) rally to peak → (3) pullback to higher low → break above
        Short: (1) swing high → (2) drop to trough → (3) pullup to lower high → break below
        """
        cfg = self.cfg
        lb = cfg.p123_lookback
        if i < lb + 1:
            return None

        window = df.iloc[i - lb:i + 1]
        row = df.iloc[i]
        prev = df.iloc[i - 1]

        # ── LONG 123 ──
        if cfg.allow_long and htf_bias != "STRONG_BEAR":
            # Point 1: swing low in first half of lookback
            first_half = window.iloc[:lb // 2]
            point1_pos = first_half["Low"].idxmin()
            point1_low = first_half["Low"].loc[point1_pos]

            # Point 2: peak high after point 1
            after_p1 = window.loc[point1_pos:]
            if len(after_p1) > 3:
                point2_pos = after_p1["High"].idxmax()
                point2_high = after_p1["High"].loc[point2_pos]

                # Point 3: pullback low after point 2, must be HIGHER than point 1
                after_p2 = window.loc[point2_pos:]
                if len(after_p2) > 2:
                    point3_pos = after_p2["Low"].idxmin()
                    point3_low = after_p2["Low"].loc[point3_pos]

                    # Higher low check
                    if point3_low > point1_low * (1 + cfg.p123_higher_low_pct):
                        # Current bar breaks above prior bar's high
                        if row["Close"] > prev["High"] and row["Close"] > row["Open"]:
                            # Confirm: price above SMA(20)
                            sma_val = row.get("SMA_20", np.nan)
                            if pd.notna(sma_val) and row["Close"] > sma_val:
                                stop_price = point3_low - 0.5 * atr_val
                                entry_price = row["Close"]
                                risk_per_share = abs(entry_price - stop_price)
                                if risk_per_share > 0:
                                    # R:R check
                                    target = point2_high
                                    rr = (target - entry_price) / risk_per_share
                                    if rr >= cfg.min_rr_ratio:
                                        score = self._score_123(row, prev, df, i,
                                                                 ofi_sig, atr_val,
                                                                 Side.LONG)
                                        if score >= cfg.enhanced_min_score:
                                            pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))
                                            return V4Signal(
                                                bar_index=i, timestamp=df.index[i],
                                                side=Side.LONG, entry_price=entry_price,
                                                stop_price=stop_price,
                                                risk_per_share=risk_per_share,
                                                position_size=pos_size,
                                                atr_value=atr_val,
                                                pattern=PatternType.PATTERN_123,
                                                enhanced_score=score,
                                                l2_liquidity=l2_liq,
                                                ofi_signal=ofi_sig,
                                            )

        # ── SHORT 123 (mirror) ──
        if cfg.allow_short and htf_bias != "STRONG_BULL":
            first_half = window.iloc[:lb // 2]
            point1_pos = first_half["High"].idxmax()
            point1_high = first_half["High"].loc[point1_pos]

            after_p1 = window.loc[point1_pos:]
            if len(after_p1) > 3:
                point2_pos = after_p1["Low"].idxmin()
                point2_low = after_p1["Low"].loc[point2_pos]

                after_p2 = window.loc[point2_pos:]
                if len(after_p2) > 2:
                    point3_pos = after_p2["High"].idxmax()
                    point3_high = after_p2["High"].loc[point3_pos]

                    # Lower high check
                    if point3_high < point1_high * (1 - cfg.p123_higher_low_pct):
                        if row["Close"] < prev["Low"] and row["Close"] < row["Open"]:
                            sma_val = row.get("SMA_20", np.nan)
                            if pd.notna(sma_val) and row["Close"] < sma_val:
                                stop_price = point3_high + 0.5 * atr_val
                                entry_price = row["Close"]
                                risk_per_share = abs(stop_price - entry_price)
                                if risk_per_share > 0:
                                    target = point2_low
                                    rr = (entry_price - target) / risk_per_share
                                    if rr >= cfg.min_rr_ratio:
                                        score = self._score_123(row, prev, df, i,
                                                                 ofi_sig, atr_val,
                                                                 Side.SHORT)
                                        if score >= cfg.enhanced_min_score:
                                            pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))
                                            return V4Signal(
                                                bar_index=i, timestamp=df.index[i],
                                                side=Side.SHORT, entry_price=entry_price,
                                                stop_price=stop_price,
                                                risk_per_share=risk_per_share,
                                                position_size=pos_size,
                                                atr_value=atr_val,
                                                pattern=PatternType.PATTERN_123,
                                                enhanced_score=score,
                                                l2_liquidity=l2_liq,
                                                ofi_signal=ofi_sig,
                                            )
        return None

    def _score_123(self, row, prev, df, i, ofi_sig, atr_val, side: Side) -> int:
        """Score 123 pattern quality (0-7)."""
        score = 0
        # 1) Green/red confirmation bar
        if side == Side.LONG and row["Close"] > row["Open"]:
            score += 1
        elif side == Side.SHORT and row["Close"] < row["Open"]:
            score += 1
        # 2) Volume above average
        vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
        if vol_avg > 0 and row["Volume"] >= vol_avg * 1.2:
            score += 1
        # 3) OFI aligned
        if (side == Side.LONG and ofi_sig > 0) or (side == Side.SHORT and ofi_sig < 0):
            score += 1
        # 4) RSI in healthy zone
        rsi_val = row.get("RSI_14", 50.0)
        if pd.notna(rsi_val):
            if side == Side.LONG and 30 <= rsi_val <= 65:
                score += 1
            elif side == Side.SHORT and 35 <= rsi_val <= 70:
                score += 1
        # 5) Stochastic confirmation
        stoch_k = row.get("Stoch_K", 50.0)
        if pd.notna(stoch_k):
            if side == Side.LONG and stoch_k < 70:
                score += 1
            elif side == Side.SHORT and stoch_k > 30:
                score += 1
        # 6) Bar range decent (not too narrow, not too wide)
        br = row["High"] - row["Low"]
        if 0.3 * atr_val <= br <= 1.5 * atr_val:
            score += 1
        # 7) Price near EMA (not too extended)
        ema9 = row.get("EMA_9", np.nan)
        if pd.notna(ema9) and abs(row["Close"] - ema9) < 1.5 * atr_val:
            score += 1
        return score

    # ── Pattern: Breakout ────────────────────────────────────────────────

    def _check_breakout(self, i: int, df: pd.DataFrame, atr_val: float,
                         l2_liq: float, ofi_sig: float) -> V4Signal | None:
        """
        Breakout from images: Close breaks above lookback high with volume.
        """
        cfg = self.cfg
        lb = cfg.breakout_lookback
        if i < lb + 1:
            return None

        row = df.iloc[i]
        prev = df.iloc[i - 1]

        # Lookback peak (excluding current bar)
        lookback_high = df["High"].iloc[i - lb:i].max()

        # Must close above resistance + ATR buffer
        threshold = lookback_high + cfg.breakout_atr_buffer * atr_val
        if row["Close"] <= threshold:
            return None

        # Volume confirmation
        vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
        if vol_avg > 0 and row["Volume"] < vol_avg * cfg.breakout_volume_mult:
            return None

        # Score
        score = 0
        if row["Close"] > row["Open"]:
            score += 1  # Green bar
        if ofi_sig > 0:
            score += 1  # Buy pressure
        rsi_val = row.get("RSI_14", 50.0)
        if pd.notna(rsi_val) and rsi_val < 80:
            score += 1  # Not overbought
        stoch_k = row.get("Stoch_K", 50.0)
        if pd.notna(stoch_k) and stoch_k > 50:
            score += 1  # Momentum up
        adx_val = row.get("ADX", 0.0)
        if pd.notna(adx_val) and adx_val > 20:
            score += 1  # Trend strength
        # Check for increasing volume over last 3 bars
        if i >= 3:
            vols = [df["Volume"].iloc[i - 2], df["Volume"].iloc[i - 1], row["Volume"]]
            if vols[2] >= vols[1] >= vols[0]:
                score += 1

        if score < cfg.enhanced_min_score:
            return None

        # Stop below the breakout level
        stop_price = lookback_high - 0.5 * atr_val
        entry_price = row["Close"]
        risk_per_share = abs(entry_price - stop_price)
        if risk_per_share <= 0:
            return None
        pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

        return V4Signal(
            bar_index=i, timestamp=df.index[i], side=Side.LONG,
            entry_price=entry_price, stop_price=stop_price,
            risk_per_share=risk_per_share, position_size=pos_size,
            atr_value=atr_val, pattern=PatternType.BREAKOUT,
            enhanced_score=score, l2_liquidity=l2_liq, ofi_signal=ofi_sig,
        )

    # ── Pattern: Breakdown (Mirror of Breakout) ──────────────────────────

    def _check_breakdown(self, i: int, df: pd.DataFrame, atr_val: float,
                          l2_liq: float, ofi_sig: float) -> V4Signal | None:
        """
        Breakdown from images: Close breaks below lookback low with volume.
        """
        cfg = self.cfg
        lb = cfg.breakout_lookback
        if i < lb + 1:
            return None

        row = df.iloc[i]

        # Lookback trough
        lookback_low = df["Low"].iloc[i - lb:i].min()

        # Must close below support - ATR buffer
        threshold = lookback_low - cfg.breakout_atr_buffer * atr_val
        if row["Close"] >= threshold:
            return None

        # Volume confirmation
        vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
        if vol_avg > 0 and row["Volume"] < vol_avg * cfg.breakout_volume_mult:
            return None

        # Score
        score = 0
        if row["Close"] < row["Open"]:
            score += 1
        if ofi_sig < 0:
            score += 1
        rsi_val = row.get("RSI_14", 50.0)
        if pd.notna(rsi_val) and rsi_val > 20:
            score += 1
        stoch_k = row.get("Stoch_K", 50.0)
        if pd.notna(stoch_k) and stoch_k < 50:
            score += 1
        adx_val = row.get("ADX", 0.0)
        if pd.notna(adx_val) and adx_val > 20:
            score += 1
        if i >= 3:
            vols = [df["Volume"].iloc[i - 2], df["Volume"].iloc[i - 1], row["Volume"]]
            if vols[2] >= vols[1] >= vols[0]:
                score += 1

        if score < cfg.enhanced_min_score:
            return None

        stop_price = lookback_low + 0.5 * atr_val
        entry_price = row["Close"]
        risk_per_share = abs(stop_price - entry_price)
        if risk_per_share <= 0:
            return None
        pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))

        return V4Signal(
            bar_index=i, timestamp=df.index[i], side=Side.SHORT,
            entry_price=entry_price, stop_price=stop_price,
            risk_per_share=risk_per_share, position_size=pos_size,
            atr_value=atr_val, pattern=PatternType.BREAKDOWN,
            enhanced_score=score, l2_liquidity=l2_liq, ofi_signal=ofi_sig,
        )

    # ── Pattern: Exhaustion ──────────────────────────────────────────────

    def _check_exhaustion(self, i: int, df: pd.DataFrame, atr_val: float,
                           l2_liq: float, ofi_sig: float,
                           htf_bias: str) -> V4Signal | None:
        """
        Exhaustion Trade from images: Counter-trend entry after extended move.
        Based on C# Tmg027TrendExhaustionExitStrategy applied as ENTRY.
        """
        cfg = self.cfg
        lb = cfg.exhaustion_lookback
        rb = cfg.exhaustion_reversal_bars
        if i < lb + rb + 1:
            return None

        row = df.iloc[i]

        # Check for extended move in lookback
        fav_seg = df.iloc[i - lb - rb:i - rb]
        rev_seg = df.iloc[i - rb:i + 1]

        # ── Extended move UP → Short exhaustion ──
        if cfg.allow_short and htf_bias != "STRONG_BULL":
            move_up = fav_seg["Close"].iloc[-1] - fav_seg["Close"].iloc[0]
            if move_up > cfg.exhaustion_min_move_atr * atr_val:
                # All reversal bars must be bearish (close < open)
                all_bearish = all(rev_seg["Close"].iloc[j] < rev_seg["Open"].iloc[j]
                                  for j in range(len(rev_seg)))
                if all_bearish:
                    # Additional: Stochastic overbought, RSI rolling over
                    stoch_k = row.get("Stoch_K", 50.0)
                    rsi_val = row.get("RSI_14", 50.0)
                    willr = row.get("WillR_14", -50.0)

                    exh_score = 0
                    if pd.notna(stoch_k) and stoch_k > 70:
                        exh_score += 1
                    if pd.notna(rsi_val) and rsi_val > 60:
                        exh_score += 1
                    if pd.notna(willr) and willr > -30:
                        exh_score += 1
                    if ofi_sig < 0:
                        exh_score += 1
                    # Volume declining (exhaustion)
                    vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
                    if vol_avg > 0 and row["Volume"] < vol_avg * 0.8:
                        exh_score += 1
                    # BB upper band touch
                    bb_pctb = row.get("BB_PctB", 0.5)
                    if pd.notna(bb_pctb) and bb_pctb > 0.85:
                        exh_score += 1

                    if exh_score >= cfg.enhanced_min_score:
                        high_of_run = fav_seg["High"].max()
                        stop_price = high_of_run + 0.3 * atr_val
                        entry_price = row["Close"]
                        risk_per_share = abs(stop_price - entry_price)
                        if risk_per_share > 0:
                            pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))
                            return V4Signal(
                                bar_index=i, timestamp=df.index[i],
                                side=Side.SHORT, entry_price=entry_price,
                                stop_price=stop_price,
                                risk_per_share=risk_per_share,
                                position_size=pos_size,
                                atr_value=atr_val,
                                pattern=PatternType.EXHAUSTION,
                                enhanced_score=exh_score,
                                l2_liquidity=l2_liq,
                                ofi_signal=ofi_sig,
                            )

        # ── Extended move DOWN → Long exhaustion ──
        if cfg.allow_long and htf_bias != "STRONG_BEAR":
            move_down = fav_seg["Close"].iloc[0] - fav_seg["Close"].iloc[-1]
            if move_down > cfg.exhaustion_min_move_atr * atr_val:
                all_bullish = all(rev_seg["Close"].iloc[j] > rev_seg["Open"].iloc[j]
                                  for j in range(len(rev_seg)))
                if all_bullish:
                    stoch_k = row.get("Stoch_K", 50.0)
                    rsi_val = row.get("RSI_14", 50.0)
                    willr = row.get("WillR_14", -50.0)

                    exh_score = 0
                    if pd.notna(stoch_k) and stoch_k < 30:
                        exh_score += 1
                    if pd.notna(rsi_val) and rsi_val < 40:
                        exh_score += 1
                    if pd.notna(willr) and willr < -70:
                        exh_score += 1
                    if ofi_sig > 0:
                        exh_score += 1
                    vol_avg = df["Volume"].iloc[max(0, i - 8):i].mean()
                    if vol_avg > 0 and row["Volume"] < vol_avg * 0.8:
                        exh_score += 1
                    bb_pctb = row.get("BB_PctB", 0.5)
                    if pd.notna(bb_pctb) and bb_pctb < 0.15:
                        exh_score += 1

                    if exh_score >= cfg.enhanced_min_score:
                        low_of_run = fav_seg["Low"].min()
                        stop_price = low_of_run - 0.3 * atr_val
                        entry_price = row["Close"]
                        risk_per_share = abs(entry_price - stop_price)
                        if risk_per_share > 0:
                            pos_size = max(1, int(cfg.risk_per_trade_dollars / risk_per_share))
                            return V4Signal(
                                bar_index=i, timestamp=df.index[i],
                                side=Side.LONG, entry_price=entry_price,
                                stop_price=stop_price,
                                risk_per_share=risk_per_share,
                                position_size=pos_size,
                                atr_value=atr_val,
                                pattern=PatternType.EXHAUSTION,
                                enhanced_score=exh_score,
                                l2_liquidity=l2_liq,
                                ofi_signal=ofi_sig,
                            )
        return None

    # ── HTF Bias ─────────────────────────────────────────────────────────

    def _compute_htf_bias(self, df_1h, df_1d) -> str:
        """Light HTF bias: STRONG_BULL / STRONG_BEAR / NEUTRAL."""
        scores = []
        for df in [df_1h, df_1d]:
            if df is None or len(df) < 50:
                continue
            e = enrich_with_indicators(df)
            last = e.iloc[-1]
            prev = e.iloc[-2]
            s = 0
            s += 1 if last["EMA_21"] > prev["EMA_21"] else -1
            if last["ADX"] > 25:
                s += 1 if last["Plus_DI"] > last["Minus_DI"] else -1
            s += 1 if last["MACD_Hist"] > 0 else -1
            scores.append(s)
        if not scores:
            return "NEUTRAL"
        avg = sum(scores) / len(scores)
        if avg >= 2.0:
            return "STRONG_BULL"
        elif avg <= -2.0:
            return "STRONG_BEAR"
        return "NEUTRAL"

    # ── Trade Simulation (Conduct Risk Management) ───────────────────────

    def simulate_trade(self, signal: V4Signal, df_trigger: pd.DataFrame) -> V4TradeResult | None:
        """Simulate a trade with Conduct V1.2 risk management rules."""
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
        trailing_stop = stop_price
        exit_reason: ExitReason | None = None
        exit_price = entry_price
        exit_bar = signal.bar_index

        for j in range(signal.bar_index + 1,
                       min(signal.bar_index + cfg.max_hold_bars + 1,
                           len(df_trigger))):
            bar = df_trigger.iloc[j]
            price = bar["Close"]
            high = bar["High"]
            low = bar["Low"]

            # Track peak/trough
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
            if side == Side.LONG:
                unrealized_r = (price - entry_price) / risk_per_share
                peak_r = (peak_price - entry_price) / risk_per_share
            else:
                unrealized_r = (entry_price - price) / risk_per_share
                peak_r = (entry_price - trough_price) / risk_per_share

            # ── TP2 ──
            if unrealized_r >= cfg.tp2_r:
                exit_price = price
                exit_reason = ExitReason.TP2
                exit_bar = j
                break

            # ── TP1 → tighten stop ──
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

            # ── Giveback from peak ──
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
                min(signal.bar_index + cfg.max_hold_bars,
                    len(df_trigger) - 1)
            ]["Close"]
            exit_reason = ExitReason.TIME_STOP
            exit_bar = min(signal.bar_index + cfg.max_hold_bars,
                           len(df_trigger) - 1)

        # Slippage on exit
        if side == Side.LONG:
            exit_price -= cfg.slippage_cents / 100.0
        else:
            exit_price += cfg.slippage_cents / 100.0

        # PnL
        pnl_per_share = ((exit_price - entry_price) if side == Side.LONG
                         else (entry_price - exit_price))
        commission = cfg.commission_per_share * pos_size * 2
        pnl = pnl_per_share * pos_size - commission
        pnl_r = pnl_per_share / risk_per_share if risk_per_share > 0 else 0.0

        final_peak_r = (
            (peak_price - entry_price) / risk_per_share if side == Side.LONG
            else (entry_price - trough_price) / risk_per_share
        )

        return V4TradeResult(
            entry_bar=signal.bar_index, exit_bar=exit_bar,
            entry_time=signal.timestamp,
            exit_time=(df_trigger.index[exit_bar]
                       if exit_bar < len(df_trigger)
                       else signal.timestamp),
            side=side, entry_price=entry_price, exit_price=exit_price,
            stop_price=signal.stop_price, position_size=pos_size,
            pnl=pnl, pnl_r=pnl_r, exit_reason=exit_reason,
            peak_r=final_peak_r,
            bars_held=exit_bar - signal.bar_index,
            pattern=signal.pattern,
            enhanced_score=signal.enhanced_score,
        )
