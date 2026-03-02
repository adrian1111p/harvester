"""
quick_scanner.py — Scanner Selection V2: 20MA Exhaustion Filter.

One-shot scanner: checks ALL strategies on ALL symbols NOW.
Picks the best available signal and enters a 2-share paper trade.

V2 changes (2026-03-02):
  - Added V5 strategies (PullbackVWAP, Tight) to scan list.
  - 20MA exhaustion filter: REJECT any LONG signal when price is > 0.5 ATR
    above 20MA, REJECT any SHORT when price is > 0.5 ATR below 20MA.
    This prevents entries in overextended zones (lesson from AMD loss).
"""
from __future__ import annotations
import asyncio, sys, time
sys.path.insert(0, r"d:\Site\harvester")
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

import numpy as np
import pandas as pd
from ib_insync import IB, Stock, MarketOrder, StopOrder

from backtest.indicators import enrich_with_indicators
from backtest.strategy import ConductStrategyV13, StrategyConfig
from backtest.strategy_v3 import StrategyV3, V3Config
from backtest.strategy_v4 import StrategyV4, V4Config
from backtest.strategy_v5 import StrategyV5, V5Config

POSITION_SIZE = 2
SYMBOLS = ["TSLA", "AAPL", "NVDA", "AMD", "META"]

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
CFG_V4_RUNNER = V4Config(
    risk_per_trade_dollars=50.0, enhanced_min_score=2,
    enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0,
    breakeven_r=1.5, giveback_pct=0.80, max_hold_bars=180,
)
CFG_V4_BASE = V4Config(
    risk_per_trade_dollars=50.0, enhanced_min_score=2,
    enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120,
)
# Looser exhaustion: lower score threshold and shorter lookback
CFG_V4_LOOSE = V4Config(
    risk_per_trade_dollars=50.0, enhanced_min_score=1,
    enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=10, exhaustion_min_move_atr=2.5,
    exhaustion_reversal_bars=2,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120,
)
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

# ── 20MA Exhaustion filter threshold (applied to ALL strategies) ──
MA_EXHAUSTION_MAX_ATR = 0.5   # Reject LONG > 0.5 ATR above 20MA, SHORT > 0.5 below

STRATEGIES = [
    ("Trend-V1.3",       lambda: ConductStrategyV13(CFG_TREND)),
    ("V3-Balanced",      lambda: StrategyV3(CFG_V3)),
    ("V4-Exh-Runner",    lambda: StrategyV4(CFG_V4_RUNNER)),
    ("V4-Exh-Base",      lambda: StrategyV4(CFG_V4_BASE)),
    ("V4-Exh-Loose",     lambda: StrategyV4(CFG_V4_LOOSE)),
    ("V5-PullbackVWAP",  lambda: StrategyV5(CFG_V5_PULLBACK_VWAP)),
    ("V5-Tight",         lambda: StrategyV5(CFG_V5_TIGHT)),
]


def fetch_bars(ib: IB, symbol: str, bar_size: str, duration: str) -> pd.DataFrame:
    contract = Stock(symbol, "SMART", "USD")
    ib.qualifyContracts(contract)
    bars = ib.reqHistoricalData(
        contract, endDateTime="", durationStr=duration,
        barSizeSetting=bar_size, whatToShow="TRADES",
        useRTH=True, formatDate=1,
    )
    if not bars:
        return pd.DataFrame()
    rows = [{"Timestamp": str(b.date), "Open": float(b.open), "High": float(b.high),
             "Low": float(b.low), "Close": float(b.close), "Volume": int(b.volume)}
            for b in bars]
    df = pd.DataFrame(rows)
    df["Timestamp"] = pd.to_datetime(df["Timestamp"], utc=True)
    df.set_index("Timestamp", inplace=True)
    df.sort_index(inplace=True)
    return df


def fetch_multi_tf(ib: IB, symbol: str) -> dict:
    tfs = [("1m","1 min","2 D"),("5m","5 mins","10 D"),
           ("15m","15 mins","20 D"),("1h","1 hour","60 D"),("1D","1 day","365 D")]
    data = {}
    for name, bs, dur in tfs:
        try:
            data[name] = fetch_bars(ib, symbol, bs, dur)
            time.sleep(1)
        except Exception as e:
            print(f"  [WARN] {symbol} {name}: {e}")
            data[name] = None
    return data


def main():
    print("=" * 60)
    print("  QUICK SCANNER — Looking for signals NOW")
    print("=" * 60)

    ib = IB()
    ib.connect("127.0.0.1", 7497, clientId=91, timeout=20)
    print(f"Connected. Account: {ib.managedAccounts()}")

    all_signals = []

    for symbol in SYMBOLS:
        print(f"\n--- Scanning {symbol} ---")
        data = fetch_multi_tf(ib, symbol)
        df_1m = data.get("1m")
        if df_1m is None or len(df_1m) < 60:
            print(f"  Not enough data ({len(df_1m) if df_1m is not None else 0} bars)")
            continue

        last_close = df_1m["Close"].iloc[-1]
        print(f"  Last price: ${last_close:.2f} | Bars: {len(df_1m)}")

        # ── 20MA exhaustion check (global filter for ALL strategies) ──
        enriched_1m = enrich_with_indicators(df_1m)
        sma_20 = enriched_1m["SMA_20"].iloc[-1] if "SMA_20" in enriched_1m.columns else np.nan
        atr_14 = enriched_1m["ATR_14"].iloc[-1] if "ATR_14" in enriched_1m.columns else np.nan
        ma_dist_atr = np.nan
        if pd.notna(sma_20) and pd.notna(atr_14) and atr_14 > 0 and sma_20 > 0:
            ma_dist_atr = (last_close - sma_20) / atr_14
            print(f"  20MA=${sma_20:.2f}  dist={ma_dist_atr:+.2f} ATR")
        else:
            print(f"  20MA/ATR unavailable — skipping exhaustion filter")

        for strat_name, strat_factory in STRATEGIES:
            try:
                strat = strat_factory()
                signals = strat.generate_signals(
                    df_1m, data.get("5m"), data.get("15m"),
                    data.get("1h"), data.get("1D"),
                )
                if signals:
                    # Check last N signals (generous — last 20 bars = ~20 min)
                    recent = [s for s in signals if s.bar_index >= len(df_1m) - 20]
                    if recent:
                        s = recent[-1]
                        freshness = len(df_1m) - 1 - s.bar_index
                        pattern_info = ""
                        score_info = ""
                        if hasattr(s, "pattern") and s.pattern:
                            pattern_info = f" [{s.pattern.value}]"
                        if hasattr(s, "enhanced_score"):
                            score_info = f" Score={s.enhanced_score}"
                        # ── 20MA exhaustion gate ──
                        if pd.notna(ma_dist_atr):
                            if s.side.value == "LONG" and ma_dist_atr > MA_EXHAUSTION_MAX_ATR:
                                print(f"  !!! REJECTED {strat_name} LONG {symbol}: "
                                      f"price {ma_dist_atr:+.1f} ATR above 20MA (exhaustion zone)")
                                continue
                            if s.side.value == "SHORT" and ma_dist_atr < -MA_EXHAUSTION_MAX_ATR:
                                print(f"  !!! REJECTED {strat_name} SHORT {symbol}: "
                                      f"price {ma_dist_atr:+.1f} ATR below 20MA (exhaustion zone)")
                                continue

                        print(f"  >>> SIGNAL: {strat_name} {s.side.value} @ ${s.entry_price:.2f}"
                              f" Stop=${s.stop_price:.2f} Risk=${s.risk_per_share:.2f}"
                              f" (bars ago: {freshness}){pattern_info}{score_info}")
                        all_signals.append({
                            "symbol": symbol,
                            "strategy": strat_name,
                            "side": s.side.value,
                            "entry_price": s.entry_price,
                            "stop_price": s.stop_price,
                            "risk_per_share": s.risk_per_share,
                            "atr": s.atr_value,
                            "freshness": freshness,
                            "score": getattr(s, "enhanced_score", 0),
                        })
                    else:
                        total = len(signals)
                        last = signals[-1]
                        bars_ago = len(df_1m) - 1 - last.bar_index
                        print(f"  {strat_name}: {total} signals total, last was {bars_ago} bars ago")
                else:
                    print(f"  {strat_name}: No signals")
            except Exception as e:
                print(f"  {strat_name}: ERROR - {e}")

    print("\n" + "=" * 60)
    if not all_signals:
        print("  NO RECENT SIGNALS FOUND across any strategy/symbol.")
        print("  The bot is running in background and will trade when conditions align.")
        print("  Strategies are selective by design (high win-rate = fewer but better entries).")
        print("=" * 60)
    else:
        print(f"  FOUND {len(all_signals)} SIGNAL(S)!")
        # Sort by freshness (most recent first), then by score
        all_signals.sort(key=lambda x: (x["freshness"], -x["score"]))
        best = all_signals[0]
        print(f"\n  BEST: {best['symbol']} {best['strategy']} {best['side']}")
        print(f"    Entry: ${best['entry_price']:.2f}")
        print(f"    Stop:  ${best['stop_price']:.2f}")
        print(f"    Risk:  ${best['risk_per_share']:.2f}/share")
        print(f"    Freshness: {best['freshness']} bars ago")

        # ENTER THE TRADE
        print(f"\n  ENTERING PAPER TRADE: {best['side']} {best['symbol']} x{POSITION_SIZE} shares...")
        contract = Stock(best["symbol"], "SMART", "USD")
        ib.qualifyContracts(contract)

        action = "BUY" if best["side"] == "LONG" else "SELL"
        entry_order = MarketOrder(action, POSITION_SIZE)
        trade = ib.placeOrder(contract, entry_order)
        ib.sleep(3)

        fill_price = best["entry_price"]
        if trade.orderStatus.avgFillPrice > 0:
            fill_price = trade.orderStatus.avgFillPrice
        print(f"    FILLED at ${fill_price:.2f}")

        # Place protective stop
        stop_action = "SELL" if best["side"] == "LONG" else "BUY"
        stop_order = StopOrder(stop_action, POSITION_SIZE, best["stop_price"])
        ib.placeOrder(contract, stop_order)
        ib.sleep(1)
        print(f"    STOP placed at ${best['stop_price']:.2f}")

        pnl_target = best["risk_per_share"] * 1.5  # TP1 at 1.5R
        if best["side"] == "LONG":
            tp = fill_price + pnl_target
        else:
            tp = fill_price - pnl_target
        print(f"    TP1 target: ${tp:.2f} (1.5R)")
        print(f"\n    Trade entered! The background bot will manage exits.")
        print("=" * 60)

    ib.disconnect()
    print("Disconnected.")


if __name__ == "__main__":
    main()
