"""
live_paper.py — Live Paper Trading Bot V2 (2 shares).

Runs the BEST HYBRID strategy from V5 sweep optimization:
  AAPL → V5-PullbackVWAP (24tr, 75% WR, +$210)
  TSLA → V5-Tight         (55tr, 55% WR, +$1832)
  NVDA → Trend V1.3        (18tr, 39% WR, +$55)
  AMD  → Trend V1.3        (17tr, 82% WR, +$401)
  META → V5-Tight          (54tr, 54% WR, +$276)

Backtest results: 168 trades, +$2,776.

V2 changes (2026-03-02):
  - V5 strategy integration (20MA filter + micro-trail + reversal flatten)
  - Micro-trailing ($0.02-$0.03) once in small profit
  - Reversal candle detection → immediate flatten
  - Tighter breakeven (0.3R) and giveback (40%)

Uses 2 shares per trade for safety.
Connects to IBKR TWS paper trading (port 7497).
"""
from __future__ import annotations

import asyncio
import logging
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from typing import Optional
from zoneinfo import ZoneInfo

ET = ZoneInfo("America/New_York")

# Python 3.14 workaround
if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

import numpy as np
import pandas as pd
from ib_insync import IB, Stock, MarketOrder, StopOrder, LimitOrder, util

from backtest.indicators import enrich_with_indicators
from backtest.strategy import ConductStrategyV13, StrategyConfig, Side as V1Side
from backtest.strategy_v3 import StrategyV3, V3Config, Side as V3Side
from backtest.strategy_v4 import StrategyV4, V4Config, PatternType, Side as V4Side
from backtest.strategy_v5 import StrategyV5, V5Config, Side as V5Side

# ── Logging ──────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler("live_paper.log", mode="a"),
    ],
)
log = logging.getLogger("LivePaper")

# ── Constants ────────────────────────────────────────────────────────────────
PAPER_PORT = 7497
CLIENT_ID = 90
POSITION_SIZE = 2          # 2 shares per trade
MAX_DAILY_TRADES = 10      # Safety: max trades per day
EOD_FLATTEN_MINUTE = 955   # 15:55 ET (minutes from midnight)
CHECK_INTERVAL_SEC = 60    # Poll every 60 seconds


# ── Strategy assignment ──────────────────────────────────────────────────────

class StrategyType(Enum):
    TREND_V13 = "Trend-V1.3"
    V3_BALANCED = "V3-Balanced"
    V4_EXH_RUNNER = "V4-Exh-Runner"
    V4_EXH_BASE = "V4-Exh-Base"
    V5_PULLBACK_VWAP = "V5-PullbackVWAP"
    V5_TIGHT = "V5-Tight"


# Per-symbol strategy assignment (from V5 sweep hybrid optimization)
SYMBOL_STRATEGY = {
    "AAPL": StrategyType.V5_PULLBACK_VWAP,   # 24tr, 75% WR, +$210
    "TSLA": StrategyType.V5_TIGHT,            # 55tr, 55% WR, +$1832
    "NVDA": StrategyType.TREND_V13,           # 18tr, 39% WR, +$55
    "AMD":  StrategyType.TREND_V13,           # 17tr, 82% WR, +$401
    "META": StrategyType.V5_TIGHT,            # 54tr, 54% WR, +$276
}

# Strategy configs
CFG_TREND = StrategyConfig(
    risk_per_trade_dollars=50.0,
    trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
    hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
)

CFG_V3 = V3Config(
    risk_per_trade_dollars=50.0,
    min_price=8.0, max_price=500.0,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0,
)

CFG_V4_EXH_RUNNER = V4Config(
    risk_per_trade_dollars=50.0,
    enhanced_min_score=2,
    enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0,
    breakeven_r=1.5, giveback_pct=0.80, max_hold_bars=180,
)

CFG_V4_EXH_BASE = V4Config(
    risk_per_trade_dollars=50.0,
    enhanced_min_score=2,
    enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120,
)

# V5 configs (20MA filter + micro-trailing)
CFG_V5_PULLBACK_VWAP = V5Config(
    risk_per_trade_dollars=50.0,
    exhaustion_fade_enabled=False,
)

CFG_V5_TIGHT = V5Config(
    risk_per_trade_dollars=50.0,
    max_ma_dist_atr=0.3,
    micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
    hard_stop_r=1.0, breakeven_r=0.3, giveback_pct=0.40,
    tp1_r=0.8, tp2_r=1.5, max_hold_bars=30,
)


# ── Position Tracker ─────────────────────────────────────────────────────────

@dataclass
class LivePosition:
    symbol: str
    side: str           # "LONG" or "SHORT"
    entry_price: float
    stop_price: float
    shares: int
    entry_time: datetime
    strategy: str
    atr_at_entry: float
    risk_per_share: float
    breakeven_activated: bool = False
    peak_price: float = 0.0
    trailing_stop: float = 0.0
    tp1_hit: bool = False


class LivePaperBot:
    """Live paper trading bot using IBKR TWS."""

    def __init__(self):
        self.ib = IB()
        self.positions: dict[str, LivePosition] = {}
        self.daily_trades = 0
        self.daily_pnl = 0.0
        self.trade_log: list[dict] = []

    def connect(self):
        """Connect to IBKR TWS paper trading."""
        log.info("Connecting to IBKR TWS (paper) on port %d...", PAPER_PORT)
        self.ib.connect("127.0.0.1", PAPER_PORT, clientId=CLIENT_ID, timeout=20)
        log.info("Connected. Account: %s", self.ib.managedAccounts())

    def disconnect(self):
        """Disconnect from IBKR."""
        if self.ib.isConnected():
            self.ib.disconnect()
            log.info("Disconnected from TWS.")

    def _fetch_bars(self, symbol: str, bar_size: str = "1 min",
                    duration: str = "2 D") -> pd.DataFrame:
        """Fetch recent historical bars for signal generation."""
        contract = Stock(symbol, "SMART", "USD")
        self.ib.qualifyContracts(contract)
        bars = self.ib.reqHistoricalData(
            contract,
            endDateTime="",
            durationStr=duration,
            barSizeSetting=bar_size,
            whatToShow="TRADES",
            useRTH=True,
            formatDate=1,
        )
        if not bars:
            return pd.DataFrame()
        rows = []
        for b in bars:
            rows.append({
                "Timestamp": str(b.date),
                "Open": float(b.open),
                "High": float(b.high),
                "Low": float(b.low),
                "Close": float(b.close),
                "Volume": int(b.volume),
            })
        df = pd.DataFrame(rows)
        df["Timestamp"] = pd.to_datetime(df["Timestamp"], utc=True)
        df.set_index("Timestamp", inplace=True)
        df.sort_index(inplace=True)
        return df

    def _fetch_multi_tf(self, symbol: str) -> dict[str, pd.DataFrame]:
        """Fetch multiple timeframes for a symbol."""
        result = {}
        tf_configs = [
            ("1m", "1 min", "2 D"),
            ("5m", "5 mins", "10 D"),
            ("15m", "15 mins", "20 D"),
            ("1h", "1 hour", "60 D"),
            ("1D", "1 day", "365 D"),
        ]
        for tf_name, bar_size, duration in tf_configs:
            try:
                df = self._fetch_bars(symbol, bar_size, duration)
                result[tf_name] = df
                time.sleep(1)  # Pacing
            except Exception as e:
                log.warning("Failed to fetch %s %s: %s", symbol, tf_name, e)
                result[tf_name] = None
        return result

    def _check_for_signal(self, symbol: str) -> Optional[dict]:
        """Check if the assigned strategy generates a signal for this symbol."""
        strat_type = SYMBOL_STRATEGY[symbol]
        data = self._fetch_multi_tf(symbol)
        df_1m = data.get("1m")

        if df_1m is None or len(df_1m) < 60:
            log.debug("Not enough 1m bars for %s (%d)",
                      symbol, len(df_1m) if df_1m is not None else 0)
            return None

        if strat_type == StrategyType.TREND_V13:
            return self._check_trend_v13(symbol, data)
        elif strat_type == StrategyType.V3_BALANCED:
            return self._check_v3(symbol, data)
        elif strat_type in (StrategyType.V4_EXH_RUNNER, StrategyType.V4_EXH_BASE):
            cfg = CFG_V4_EXH_RUNNER if strat_type == StrategyType.V4_EXH_RUNNER else CFG_V4_EXH_BASE
            return self._check_v4_exhaustion(symbol, data, cfg, strat_type.value)
        elif strat_type == StrategyType.V5_PULLBACK_VWAP:
            return self._check_v5(symbol, data, CFG_V5_PULLBACK_VWAP, strat_type.value)
        elif strat_type == StrategyType.V5_TIGHT:
            return self._check_v5(symbol, data, CFG_V5_TIGHT, strat_type.value)
        return None

    def _check_trend_v13(self, symbol: str, data: dict) -> Optional[dict]:
        """Check Trend V1.3 for signal on latest bars."""
        strategy = ConductStrategyV13(CFG_TREND)
        df_1m = data["1m"]
        signals = strategy.generate_signals(
            df_1m, data.get("5m"), data.get("15m"),
            data.get("1h"), data.get("1D"),
        )
        if not signals:
            return None
        # Only use the most recent signal (within last 2 bars)
        last_sig = signals[-1]
        if last_sig.bar_index < len(df_1m) - 2:
            return None
        return {
            "symbol": symbol,
            "side": last_sig.side.value,
            "entry_price": last_sig.entry_price,
            "stop_price": last_sig.stop_price,
            "risk_per_share": last_sig.risk_per_share,
            "atr": last_sig.atr_value,
            "strategy": "Trend-V1.3",
        }

    def _check_v3(self, symbol: str, data: dict) -> Optional[dict]:
        """Check V3 Balanced for signal on latest bars."""
        strategy = StrategyV3(CFG_V3)
        df_1m = data["1m"]
        signals = strategy.generate_signals(
            df_1m, data.get("5m"), data.get("15m"),
            data.get("1h"), data.get("1D"),
        )
        if not signals:
            return None
        last_sig = signals[-1]
        if last_sig.bar_index < len(df_1m) - 2:
            return None
        return {
            "symbol": symbol,
            "side": last_sig.side.value,
            "entry_price": last_sig.entry_price,
            "stop_price": last_sig.stop_price,
            "risk_per_share": last_sig.risk_per_share,
            "atr": last_sig.atr_value,
            "strategy": "V3-Balanced",
            "entry_type": last_sig.entry_type,
        }

    def _check_v4_exhaustion(self, symbol: str, data: dict,
                              cfg: V4Config, strat_name: str) -> Optional[dict]:
        """Check V4 Exhaustion for signal on latest bars."""
        strategy = StrategyV4(cfg)
        df_1m = data["1m"]
        signals = strategy.generate_signals(
            df_1m, data.get("5m"), data.get("15m"),
            data.get("1h"), data.get("1D"),
        )
        if not signals:
            return None
        last_sig = signals[-1]
        if last_sig.bar_index < len(df_1m) - 2:
            return None
        return {
            "symbol": symbol,
            "side": last_sig.side.value,
            "entry_price": last_sig.entry_price,
            "stop_price": last_sig.stop_price,
            "risk_per_share": last_sig.risk_per_share,
            "atr": last_sig.atr_value,
            "strategy": strat_name,
            "pattern": last_sig.pattern.value,
            "score": last_sig.enhanced_score,
        }

    def _check_v5(self, symbol: str, data: dict,
                    cfg: V5Config, strat_name: str) -> Optional[dict]:
        """Check V5 strategy for signal on latest bars."""
        strategy = StrategyV5(cfg)
        df_1m = data["1m"]
        signals = strategy.generate_signals(
            df_1m, data.get("5m"), data.get("15m"),
            data.get("1h"), data.get("1D"),
        )
        if not signals:
            return None
        last_sig = signals[-1]
        if last_sig.bar_index < len(df_1m) - 2:
            return None
        return {
            "symbol": symbol,
            "side": last_sig.side.value,
            "entry_price": last_sig.entry_price,
            "stop_price": last_sig.stop_price,
            "risk_per_share": last_sig.risk_per_share,
            "atr": last_sig.atr_value,
            "strategy": strat_name,
            "entry_type": last_sig.entry_type,
            "ma_distance_atr": last_sig.ma_distance_atr,
        }

    def _enter_trade(self, signal: dict):
        """Execute a paper trade entry."""
        symbol = signal["symbol"]
        side = signal["side"]
        entry_price = signal["entry_price"]
        stop_price = signal["stop_price"]
        strategy = signal["strategy"]

        contract = Stock(symbol, "SMART", "USD")
        self.ib.qualifyContracts(contract)

        # Market order for entry
        action = "BUY" if side == "LONG" else "SELL"
        order = MarketOrder(action, POSITION_SIZE)

        log.info(">>> ENTERING %s %s %d shares @ ~$%.2f (stop: $%.2f) [%s]",
                 side, symbol, POSITION_SIZE, entry_price, stop_price, strategy)

        trade = self.ib.placeOrder(contract, order)
        self.ib.sleep(2)  # Wait for fill

        # Get fill price
        fill_price = entry_price  # Default if no fill yet
        if trade.orderStatus.avgFillPrice > 0:
            fill_price = trade.orderStatus.avgFillPrice
            log.info("    Filled at $%.2f", fill_price)

        # Place stop order
        stop_action = "SELL" if side == "LONG" else "BUY"
        stop_order = StopOrder(stop_action, POSITION_SIZE, stop_price)
        stop_trade = self.ib.placeOrder(contract, stop_order)
        log.info("    Stop order placed at $%.2f", stop_price)

        # Track position
        self.positions[symbol] = LivePosition(
            symbol=symbol,
            side=side,
            entry_price=fill_price,
            stop_price=stop_price,
            shares=POSITION_SIZE,
            entry_time=datetime.now(ET),
            strategy=strategy,
            atr_at_entry=signal["atr"],
            risk_per_share=signal["risk_per_share"],
            peak_price=fill_price,
            trailing_stop=stop_price,
        )
        self.daily_trades += 1
        self.trade_log.append({
            "time": datetime.now(ET).strftime("%H:%M:%S"),
            "symbol": symbol,
            "side": side,
            "entry": fill_price,
            "stop": stop_price,
            "strategy": strategy,
            "status": "OPEN",
        })

    def _manage_position(self, symbol: str):
        """Check and manage an open position (trailing, TP, exit)."""
        pos = self.positions.get(symbol)
        if not pos:
            return

        # Determine the strategy config for exit rules
        strat_type = SYMBOL_STRATEGY[symbol]
        v5_cfg = None  # Will be set for V5 strategies
        if strat_type == StrategyType.TREND_V13:
            cfg_stop = CFG_TREND.hard_stop_r
            cfg_trail = CFG_TREND.trail_r
            cfg_tp1 = CFG_TREND.tp1_r
            cfg_tp2 = CFG_TREND.tp2_r
            cfg_be = CFG_TREND.breakeven_r
            cfg_giveback = CFG_TREND.giveback_pct
        elif strat_type == StrategyType.V3_BALANCED:
            cfg_stop = CFG_V3.hard_stop_r
            cfg_trail = CFG_V3.trail_r
            cfg_tp1 = CFG_V3.tp1_r
            cfg_tp2 = CFG_V3.tp2_r
            cfg_be = CFG_V3.breakeven_r
            cfg_giveback = CFG_V3.giveback_pct
        elif strat_type == StrategyType.V4_EXH_RUNNER:
            cfg_stop = CFG_V4_EXH_RUNNER.hard_stop_r
            cfg_trail = CFG_V4_EXH_RUNNER.trail_r
            cfg_tp1 = CFG_V4_EXH_RUNNER.tp1_r
            cfg_tp2 = CFG_V4_EXH_RUNNER.tp2_r
            cfg_be = CFG_V4_EXH_RUNNER.breakeven_r
            cfg_giveback = CFG_V4_EXH_RUNNER.giveback_pct
        elif strat_type == StrategyType.V5_PULLBACK_VWAP:
            v5_cfg = CFG_V5_PULLBACK_VWAP
            cfg_stop = v5_cfg.hard_stop_r
            cfg_trail = v5_cfg.trail_r
            cfg_tp1 = v5_cfg.tp1_r
            cfg_tp2 = v5_cfg.tp2_r
            cfg_be = v5_cfg.breakeven_r
            cfg_giveback = v5_cfg.giveback_pct
        elif strat_type == StrategyType.V5_TIGHT:
            v5_cfg = CFG_V5_TIGHT
            cfg_stop = v5_cfg.hard_stop_r
            cfg_trail = v5_cfg.trail_r
            cfg_tp1 = v5_cfg.tp1_r
            cfg_tp2 = v5_cfg.tp2_r
            cfg_be = v5_cfg.breakeven_r
            cfg_giveback = v5_cfg.giveback_pct
        else:
            cfg_stop = CFG_V4_EXH_BASE.hard_stop_r
            cfg_trail = CFG_V4_EXH_BASE.trail_r
            cfg_tp1 = CFG_V4_EXH_BASE.tp1_r
            cfg_tp2 = CFG_V4_EXH_BASE.tp2_r
            cfg_be = CFG_V4_EXH_BASE.breakeven_r
            cfg_giveback = CFG_V4_EXH_BASE.giveback_pct

        # Get current price
        contract = Stock(symbol, "SMART", "USD")
        self.ib.qualifyContracts(contract)
        ticker = self.ib.reqMktData(contract, "", False, False)
        self.ib.sleep(2)
        current_price = ticker.marketPrice()
        if current_price <= 0 or np.isnan(current_price):
            current_price = ticker.last if ticker.last > 0 else ticker.close
        if current_price <= 0 or np.isnan(current_price):
            return

        # Calculate R
        rps = pos.risk_per_share
        if rps <= 0:
            return

        if pos.side == "LONG":
            unrealized_r = (current_price - pos.entry_price) / rps
            profit_per_share = current_price - pos.entry_price
            pos.peak_price = max(pos.peak_price, current_price)
            peak_r = (pos.peak_price - pos.entry_price) / rps
        else:
            unrealized_r = (pos.entry_price - current_price) / rps
            profit_per_share = pos.entry_price - current_price
            pos.peak_price = min(pos.peak_price, current_price) if pos.peak_price > 0 else current_price
            peak_r = (pos.entry_price - pos.peak_price) / rps

        log.info("  [%s] Price=$%.2f UnR=%.2fR PeakR=%.2fR",
                 symbol, current_price, unrealized_r, peak_r)

        exit_reason = None

        # ═══ V5 MICRO-TRAIL: $0.02-$0.03 tight trail once in small profit ═══
        if v5_cfg is not None and v5_cfg.micro_trail_cents > 0:
            if profit_per_share >= v5_cfg.micro_trail_activate_cents / 100.0:
                micro_trail_dist = v5_cfg.micro_trail_cents / 100.0
                if pos.side == "LONG":
                    micro_stop = pos.peak_price - micro_trail_dist
                    pos.trailing_stop = max(pos.trailing_stop, micro_stop)
                    if current_price <= pos.trailing_stop:
                        exit_reason = "MICRO_TRAIL"
                        log.info("    Micro-trail triggered: peak=$%.2f trail=$%.2f",
                                 pos.peak_price, pos.trailing_stop)
                else:
                    micro_stop = pos.peak_price + micro_trail_dist
                    pos.trailing_stop = min(pos.trailing_stop, micro_stop)
                    if current_price >= pos.trailing_stop:
                        exit_reason = "MICRO_TRAIL"
                        log.info("    Micro-trail triggered: peak=$%.2f trail=$%.2f",
                                 pos.peak_price, pos.trailing_stop)
                # Update stop in broker if it moved
                if not exit_reason and pos.trailing_stop != pos.stop_price:
                    pos.stop_price = pos.trailing_stop
                    self._update_stop_order(symbol, pos.stop_price)

        # ═══ V5 REVERSAL FLATTEN: exit on engulfing/wick when in profit ═══
        if (v5_cfg is not None and v5_cfg.reversal_flatten and
                exit_reason is None and unrealized_r > 0):
            try:
                bars = self.ib.reqHistoricalData(
                    contract, endDateTime="", durationStr="300 S",
                    barSizeSetting="1 min", whatToShow="TRADES",
                    useRTH=True, formatDate=1,
                )
                if bars and len(bars) >= 2:
                    last_bar = bars[-1]
                    prev_bar = bars[-2]
                    bar_range = last_bar.high - last_bar.low
                    if bar_range > 0:
                        if pos.side == "LONG":
                            # Bearish engulfing or big upper wick
                            upper_wick = (last_bar.high - max(last_bar.open, last_bar.close)) / bar_range
                            is_engulfing = (last_bar.close < last_bar.open and
                                           last_bar.close < prev_bar.open)
                            if is_engulfing or upper_wick > 0.6:
                                exit_reason = "REVERSAL_FLATTEN"
                                log.info("    Reversal candle detected! Flattening LONG.")
                        else:
                            # Bullish engulfing or big lower wick
                            lower_wick = (min(last_bar.open, last_bar.close) - last_bar.low) / bar_range
                            is_engulfing = (last_bar.close > last_bar.open and
                                           last_bar.close > prev_bar.open)
                            if is_engulfing or lower_wick > 0.6:
                                exit_reason = "REVERSAL_FLATTEN"
                                log.info("    Reversal candle detected! Flattening SHORT.")
            except Exception as e:
                log.warning("    Reversal check failed: %s", e)

        # TP2: full close
        if exit_reason is None and unrealized_r >= cfg_tp2:
            exit_reason = "TP2"

        # Giveback
        elif exit_reason is None and peak_r > 0 and unrealized_r > 0:
            giveback = (peak_r - unrealized_r) / peak_r
            if giveback >= cfg_giveback:
                exit_reason = "GIVEBACK"

        # Trailing
        elif exit_reason is None and pos.breakeven_activated:
            trail_dist = cfg_trail * rps
            if pos.side == "LONG":
                new_trail = pos.peak_price - trail_dist
                pos.trailing_stop = max(pos.trailing_stop, new_trail)
                if current_price <= pos.trailing_stop:
                    exit_reason = "TRAIL"
            else:
                new_trail = pos.peak_price + trail_dist
                pos.trailing_stop = min(pos.trailing_stop, new_trail)
                if current_price >= pos.trailing_stop:
                    exit_reason = "TRAIL"

        # TP1: tighten stop to breakeven
        if unrealized_r >= cfg_tp1 and not pos.tp1_hit:
            pos.tp1_hit = True
            pos.breakeven_activated = True
            new_stop = pos.entry_price
            if pos.side == "LONG":
                pos.stop_price = max(pos.stop_price, new_stop)
            else:
                pos.stop_price = min(pos.stop_price, new_stop)
            pos.trailing_stop = pos.stop_price
            log.info("    TP1 hit! Stop moved to breakeven: $%.2f", pos.stop_price)
            self._update_stop_order(symbol, pos.stop_price)

        # Breakeven
        if not pos.breakeven_activated and unrealized_r >= cfg_be:
            pos.breakeven_activated = True
            new_stop = pos.entry_price
            if pos.side == "LONG":
                pos.stop_price = max(pos.stop_price, new_stop)
            else:
                pos.stop_price = min(pos.stop_price, new_stop)
            pos.trailing_stop = pos.stop_price
            log.info("    Break-even activated! Stop: $%.2f", pos.stop_price)
            self._update_stop_order(symbol, pos.stop_price)

        # Time stop: max hold
        elapsed_bars = (datetime.now(ET) - pos.entry_time).total_seconds() / 60
        if v5_cfg is not None:
            max_minutes = v5_cfg.max_hold_bars  # V5 uses bars = minutes for 1m
        elif strat_type in (StrategyType.V4_EXH_RUNNER,):
            max_minutes = 120
        else:
            max_minutes = 90
        if exit_reason is None and elapsed_bars > max_minutes:
            exit_reason = "TIME"

        if exit_reason:
            self._exit_trade(symbol, current_price, exit_reason)

    def _update_stop_order(self, symbol: str, new_stop: float):
        """Cancel old stop and place new one."""
        contract = Stock(symbol, "SMART", "USD")
        self.ib.qualifyContracts(contract)

        # Cancel existing orders for this symbol
        for order in self.ib.openOrders():
            # Check if it's our stop order for this symbol
            for trade in self.ib.openTrades():
                if (trade.contract.symbol == symbol and
                    trade.order.orderType == "STP"):
                    self.ib.cancelOrder(trade.order)
                    self.ib.sleep(1)
                    break

        # Place new stop
        pos = self.positions[symbol]
        stop_action = "SELL" if pos.side == "LONG" else "BUY"
        stop_order = StopOrder(stop_action, POSITION_SIZE, new_stop)
        self.ib.placeOrder(contract, stop_order)
        log.info("    Updated stop to $%.2f", new_stop)

    def _exit_trade(self, symbol: str, price: float, reason: str):
        """Close a position."""
        pos = self.positions.get(symbol)
        if not pos:
            return

        contract = Stock(symbol, "SMART", "USD")
        self.ib.qualifyContracts(contract)

        # Cancel any open orders
        for trade in self.ib.openTrades():
            if trade.contract.symbol == symbol:
                self.ib.cancelOrder(trade.order)
                self.ib.sleep(0.5)

        # Market order to close
        close_action = "SELL" if pos.side == "LONG" else "BUY"
        order = MarketOrder(close_action, pos.shares)
        trade = self.ib.placeOrder(contract, order)
        self.ib.sleep(2)

        exit_price = price
        if trade.orderStatus.avgFillPrice > 0:
            exit_price = trade.orderStatus.avgFillPrice

        # PnL
        if pos.side == "LONG":
            pnl = (exit_price - pos.entry_price) * pos.shares
        else:
            pnl = (pos.entry_price - exit_price) * pos.shares
        pnl_r = pnl / (pos.risk_per_share * pos.shares) if pos.risk_per_share > 0 else 0

        self.daily_pnl += pnl

        log.info("<<< EXITED %s %s @ $%.2f (%s) PnL=$%.2f (%.2fR) [%s]",
                 pos.side, symbol, exit_price, reason, pnl, pnl_r, pos.strategy)

        # Update trade log
        for entry in reversed(self.trade_log):
            if entry["symbol"] == symbol and entry["status"] == "OPEN":
                entry["exit"] = exit_price
                entry["pnl"] = pnl
                entry["pnl_r"] = pnl_r
                entry["exit_reason"] = reason
                entry["status"] = "CLOSED"
                break

        del self.positions[symbol]

    def _flatten_all(self, reason: str = "EOD"):
        """Close all open positions."""
        for symbol in list(self.positions.keys()):
            log.info("Flattening %s (%s)", symbol, reason)
            contract = Stock(symbol, "SMART", "USD")
            self.ib.qualifyContracts(contract)
            ticker = self.ib.reqMktData(contract, "", False, False)
            self.ib.sleep(1)
            price = ticker.marketPrice()
            if price <= 0 or np.isnan(price):
                price = ticker.last
            self._exit_trade(symbol, price, reason)

    def _sync_positions(self):
        """Detect existing IBKR positions and register them for management."""
        for p in self.ib.positions():
            sym = p.contract.symbol
            if sym not in SYMBOL_STRATEGY or sym in self.positions:
                continue
            qty = int(abs(p.position))
            if qty == 0:
                continue
            side = "LONG" if p.position > 0 else "SHORT"
            avg_cost = p.avgCost  # per-share cost
            # Estimate ATR from recent bars
            contract = Stock(sym, "SMART", "USD")
            self.ib.qualifyContracts(contract)
            bars = self.ib.reqHistoricalData(
                contract, endDateTime="", durationStr="2 D",
                barSizeSetting="1 min", whatToShow="TRADES",
                useRTH=True, formatDate=1,
            )
            atr_est = 1.0
            if bars and len(bars) > 14:
                trs = []
                for i in range(1, min(15, len(bars))):
                    tr = max(bars[i].high - bars[i].low,
                             abs(bars[i].high - bars[i-1].close),
                             abs(bars[i].low - bars[i-1].close))
                    trs.append(tr)
                atr_est = sum(trs) / len(trs) if trs else 1.0
            risk_ps = atr_est  # default risk = 1 ATR
            stop_price = avg_cost - risk_ps if side == "LONG" else avg_cost + risk_ps
            self.positions[sym] = LivePosition(
                symbol=sym, side=side, entry_price=avg_cost,
                stop_price=stop_price, shares=qty,
                entry_time=datetime.now(ET), strategy=SYMBOL_STRATEGY[sym].value,
                atr_at_entry=atr_est, risk_per_share=risk_ps,
                peak_price=avg_cost, trailing_stop=stop_price,
            )
            log.info("  Synced existing position: %s %s %d shares @ $%.2f",
                     side, sym, qty, avg_cost)

    def run(self):
        """Main trading loop."""
        try:
            self.connect()
            log.info("=" * 60)
            log.info("  LIVE PAPER TRADING BOT STARTED")
            log.info("  Symbols: %s", ", ".join(SYMBOL_STRATEGY.keys()))
            log.info("  Position size: %d shares", POSITION_SIZE)
            log.info("  Strategy assignments:")
            for sym, stype in SYMBOL_STRATEGY.items():
                log.info("    %s -> %s", sym, stype.value)
            log.info("=" * 60)

            # Sync any existing IBKR positions
            self._sync_positions()

            while True:
                now = datetime.now(ET)
                current_minute = now.hour * 60 + now.minute

                # Market hours check (9:30 - 16:00 ET)
                market_open = 9 * 60 + 30    # 570
                market_close = 16 * 60        # 960

                if current_minute < market_open:
                    log.info("Pre-market. Waiting for 9:30 ET...")
                    time.sleep(60)
                    continue

                if current_minute >= market_close:
                    if self.positions:
                        self._flatten_all("MARKET_CLOSE")
                    log.info("Market closed. Daily PnL: $%.2f (%d trades)",
                             self.daily_pnl, self.daily_trades)
                    self._print_summary()
                    break

                # EOD flatten at 15:55
                if current_minute >= EOD_FLATTEN_MINUTE and self.positions:
                    self._flatten_all("EOD_FLATTEN")

                # Manage existing positions
                for symbol in list(self.positions.keys()):
                    try:
                        self._manage_position(symbol)
                    except Exception as e:
                        log.error("Error managing %s: %s", symbol, e)

                # Look for new entries (only if under daily limit)
                if (self.daily_trades < MAX_DAILY_TRADES and
                    current_minute < EOD_FLATTEN_MINUTE):
                    for symbol in SYMBOL_STRATEGY:
                        if symbol in self.positions:
                            continue  # Already in a trade
                        try:
                            signal = self._check_for_signal(symbol)
                            if signal:
                                log.info("Signal detected for %s: %s %s",
                                         symbol, signal["side"],
                                         signal["strategy"])
                                self._enter_trade(signal)
                        except Exception as e:
                            log.error("Error checking %s: %s", symbol, e)

                log.info("-- Cycle done. Positions: %d | Daily trades: %d | "
                         "Daily PnL: $%.2f | Time: %s",
                         len(self.positions), self.daily_trades,
                         self.daily_pnl, now.strftime("%H:%M:%S"))

                time.sleep(CHECK_INTERVAL_SEC)

        except KeyboardInterrupt:
            log.info("Interrupted by user.")
            if self.positions:
                self._flatten_all("USER_INTERRUPT")
        except Exception as e:
            log.error("Fatal error: %s", e, exc_info=True)
            if self.positions:
                self._flatten_all("ERROR")
        finally:
            self._print_summary()
            self.disconnect()

    def _print_summary(self):
        """Print end-of-day summary."""
        log.info("\n" + "=" * 60)
        log.info("  END-OF-DAY SUMMARY")
        log.info("=" * 60)
        log.info("  Total trades: %d", self.daily_trades)
        log.info("  Daily PnL: $%.2f", self.daily_pnl)
        if self.trade_log:
            wins = sum(1 for t in self.trade_log
                      if t.get("status") == "CLOSED" and t.get("pnl", 0) > 0)
            total = sum(1 for t in self.trade_log
                       if t.get("status") == "CLOSED")
            if total > 0:
                log.info("  Win rate: %.0f%% (%d/%d)",
                         100 * wins / total, wins, total)
            for t in self.trade_log:
                pnl_str = f"${t.get('pnl', 0):.2f}" if "pnl" in t else "OPEN"
                log.info("    %s %s %s Entry=$%.2f %s [%s]",
                         t.get("time", ""), t["side"], t["symbol"],
                         t["entry"], pnl_str, t["strategy"])
        log.info("=" * 60)


# ── Entry Point ──────────────────────────────────────────────────────────────

if __name__ == "__main__":
    bot = LivePaperBot()
    bot.run()
