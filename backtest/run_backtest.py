"""
run_backtest.py — Main runner: fetch IBKR data → run V1.3 Conduct backtest → print results.

Usage:
    python -m backtest.run_backtest                  # defaults: AAPL TSLA NVDA AMD META
    python -m backtest.run_backtest AAPL MSFT GOOG   # custom symbols
"""
from __future__ import annotations

import asyncio
import sys
import os

# Python 3.14 ib_insync workaround (must be before any ib_insync import)
if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

from pathlib import Path

import pandas as pd

from backtest.data_fetcher import (
    DATA_DIR,
    connect_ib,
    fetch_all_timeframes,
    load_data,
    save_data,
)
from backtest.engine import BacktestResult, run_backtest
from backtest.strategy import StrategyConfig

# ── Defaults ─────────────────────────────────────────────────────────────────

DEFAULT_SYMBOLS = ["AAPL", "TSLA", "NVDA", "AMD", "META"]
TRIGGER_TF = "1m"
CONTEXT_TFS = ["5m", "15m", "1h", "1D"]
ALL_TFS = [TRIGGER_TF] + CONTEXT_TFS

PORT = 7497
CLIENT_ID = 81


def data_is_cached(symbol: str) -> bool:
    """Check if we already have CSVs for all timeframes."""
    sym_dir = DATA_DIR / symbol.upper()
    if not sym_dir.exists():
        return False
    for tf in ALL_TFS:
        if not (sym_dir / f"{tf}.csv").exists():
            return False
    return True


def fetch_data_if_needed(symbols: list[str]) -> None:
    """Connect to IBKR and fetch any missing data."""
    need_fetch = [s for s in symbols if not data_is_cached(s)]
    if not need_fetch:
        print("All data cached. Skipping IBKR fetch.\n")
        return

    print(f"Fetching data from IBKR for: {', '.join(need_fetch)}")
    ib = connect_ib(port=PORT, client_id=CLIENT_ID)
    try:
        for sym in need_fetch:
            print(f"\n=== {sym} ===")
            data = fetch_all_timeframes(ib, sym, ALL_TFS, pacing_delay=2.5)
            save_data(sym, data)
    finally:
        ib.disconnect()
    print("\nData fetch complete.\n")


def optimized_config() -> StrategyConfig:
    """Best config from parameter sweep (Sharpe=1.57, PF=1.36)."""
    return StrategyConfig(
        trail_r=1.5,
        giveback_pct=0.70,
        tp1_r=2.0,
        tp2_r=4.0,
        hard_stop_r=1.5,
        breakeven_r=1.2,
        rvol_min=1.3,          # default — optimizer used this
        adx_threshold=20.0,
        risk_per_trade_dollars=50.0,
        account_size=25_000.0,
    )


def run_all(symbols: list[str], config: StrategyConfig | None = None) -> list[BacktestResult]:
    """Run backtest for all symbols and print results."""
    cfg = config or optimized_config()
    results: list[BacktestResult] = []

    for sym in symbols:
        print(f"\n{'='*60}")
        print(f"  BACKTEST: {sym}  (trigger={TRIGGER_TF})")
        print(f"{'='*60}")

        try:
            df_trigger = load_data(sym, TRIGGER_TF)
        except FileNotFoundError:
            print(f"  [SKIP] No {TRIGGER_TF} data for {sym}")
            continue

        # Load context timeframes
        context: dict[str, pd.DataFrame | None] = {}
        for tf in CONTEXT_TFS:
            try:
                context[tf] = load_data(sym, tf)
            except FileNotFoundError:
                context[tf] = None

        result = run_backtest(
            symbol=sym,
            df_trigger=df_trigger,
            trigger_tf=TRIGGER_TF,
            df_5m=context.get("5m"),
            df_15m=context.get("15m"),
            df_1h=context.get("1h"),
            df_1d=context.get("1D"),
            config=cfg,
        )
        results.append(result)

        # Print report
        print(f"\n{result.summary_table()}")
        print(f"\nLast 15 trades:")
        print(result.trades_table(15))

    # ── Aggregate summary ────────────────────────────────────────────────
    if len(results) > 1:
        print(f"\n{'='*60}")
        print(f"  AGGREGATE SUMMARY  ({len(results)} symbols)")
        print(f"{'='*60}")

        total_trades = sum(r.stats["total_trades"] for r in results)
        total_winners = sum(r.stats["winners"] for r in results)
        total_pnl = sum(r.stats["total_pnl"] for r in results)
        all_pnl_r = []
        for r in results:
            all_pnl_r.extend([t.pnl_r for t in r.trades])

        import numpy as np
        from tabulate import tabulate

        agg_rows = [
            ["Symbols", ", ".join(r.symbol for r in results)],
            ["Total Trades", total_trades],
            ["Total Winners", total_winners],
            ["Overall Win Rate", f"{total_winners/total_trades:.1%}" if total_trades > 0 else "N/A"],
            ["Total PnL ($)", f"${total_pnl:.2f}"],
            ["Avg Expectancy (R)", f"{np.mean(all_pnl_r):.2f}R" if all_pnl_r else "N/A"],
            ["Max Single DD ($)", f"${max(r.stats['max_drawdown'] for r in results):.2f}"],
        ]

        # Per-symbol summary
        for r in results:
            agg_rows.append([
                f"  {r.symbol}",
                f"{r.stats['total_trades']} trades | ${r.stats['total_pnl']:.2f} | "
                f"WR {r.stats['win_rate']:.0%} | PF {r.stats['profit_factor']:.2f} | "
                f"Sharpe {r.stats['sharpe']:.2f}"
            ])

        print(tabulate(agg_rows, headers=["Metric", "Value"], tablefmt="simple"))

    return results


# ── Main ─────────────────────────────────────────────────────────────────────

def main() -> None:
    symbols = sys.argv[1:] if len(sys.argv) > 1 else DEFAULT_SYMBOLS
    print(f"Harvester Backtest Engine V1.0")
    print(f"V1.3 Conduct Strategy — Multi-TF Trend (OPTIMIZED: Trail=1.5R Give=70% TP1=2R Stop=1.5R)")
    print(f"Symbols: {', '.join(symbols)}")
    print(f"Trigger: {TRIGGER_TF} | Context: {', '.join(CONTEXT_TFS)}")
    print(f"IBKR Port: {PORT} (paper)\n")

    # Step 1: Fetch data
    fetch_data_if_needed(symbols)

    # Step 2: Run backtest
    results = run_all(symbols)

    if not results:
        print("\nNo results. Check TWS connection and data availability.")
        return

    # Final verdict
    total_pnl = sum(r.stats["total_pnl"] for r in results)
    total_trades = sum(r.stats["total_trades"] for r in results)
    print(f"\n{'='*60}")
    verdict = "PROFITABLE" if total_pnl > 0 else "UNPROFITABLE"
    print(f"  VERDICT: {verdict} — ${total_pnl:.2f} over {total_trades} trades")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
