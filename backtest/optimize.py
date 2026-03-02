"""
optimize.py — Parameter sweep to find best V1.3 Conduct configuration.
Tests combinations of trailing stop, giveback, TP, RSI, and RVOL thresholds.
"""
from __future__ import annotations

import asyncio
import sys
import itertools

if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

import pandas as pd
import numpy as np
from tabulate import tabulate

from backtest.data_fetcher import load_data
from backtest.engine import run_backtest, compute_statistics
from backtest.strategy import StrategyConfig, Side

SYMBOLS = ["AAPL", "TSLA", "NVDA", "AMD", "META"]
TRIGGER_TF = "1m"
CONTEXT_TFS = ["5m", "15m", "1h", "1D"]


def load_all_data() -> dict:
    """Load cached data for all symbols."""
    all_data = {}
    for sym in SYMBOLS:
        try:
            trigger = load_data(sym, TRIGGER_TF)
            context = {}
            for tf in CONTEXT_TFS:
                try:
                    context[tf] = load_data(sym, tf)
                except FileNotFoundError:
                    context[tf] = None
            all_data[sym] = {"trigger": trigger, "context": context}
        except FileNotFoundError:
            print(f"  [SKIP] {sym} — no data")
    return all_data


def run_sweep(all_data: dict) -> list[dict]:
    """Run parameter sweep and return ranked results."""
    
    # Parameter grid
    trail_r_values = [0.5, 0.75, 1.0, 1.5]
    giveback_values = [0.40, 0.50, 0.60, 0.70]
    tp1_r_values = [1.5, 2.0, 2.5]
    tp2_r_values = [3.0, 4.0, 5.0]
    rvol_values = [0.8, 1.0, 1.3]
    hard_stop_values = [1.0, 1.5]
    breakeven_values = [0.8, 1.0, 1.5]
    adx_values = [15.0, 20.0, 25.0]
    
    # Full grid is too large — use a smart subset
    configs = []
    
    # Baseline
    configs.append(StrategyConfig())
    
    # Systematic sweep of key parameters
    for trail_r, giveback, tp1, hard_stop in itertools.product(
        trail_r_values, giveback_values, tp1_r_values, hard_stop_values
    ):
        cfg = StrategyConfig(
            trail_r=trail_r,
            giveback_pct=giveback,
            tp1_r=tp1,
            tp2_r=max(tp1 + 1.0, 3.0),
            hard_stop_r=hard_stop,
            breakeven_r=hard_stop * 0.8,
            rvol_min=1.0,
            adx_threshold=20.0,
        )
        configs.append(cfg)
    
    # Additional configs with different RVOL / ADX
    for rvol, adx_t in itertools.product(rvol_values, adx_values):
        cfg = StrategyConfig(
            trail_r=1.0,
            giveback_pct=0.60,
            tp1_r=2.0,
            tp2_r=4.0,
            hard_stop_r=1.5,
            breakeven_r=1.0,
            rvol_min=rvol,
            adx_threshold=adx_t,
        )
        configs.append(cfg)

    # Deduplicate
    seen = set()
    unique_configs = []
    for cfg in configs:
        key = (cfg.trail_r, cfg.giveback_pct, cfg.tp1_r, cfg.tp2_r,
               cfg.hard_stop_r, cfg.breakeven_r, cfg.rvol_min, cfg.adx_threshold)
        if key not in seen:
            seen.add(key)
            unique_configs.append(cfg)

    print(f"Testing {len(unique_configs)} parameter combinations...\n")

    results = []
    for idx, cfg in enumerate(unique_configs):
        all_trades = []
        sym_pnls = {}
        
        for sym, data in all_data.items():
            bt = run_backtest(
                symbol=sym,
                df_trigger=data["trigger"],
                trigger_tf=TRIGGER_TF,
                df_5m=data["context"].get("5m"),
                df_15m=data["context"].get("15m"),
                df_1h=data["context"].get("1h"),
                df_1d=data["context"].get("1D"),
                config=cfg,
            )
            all_trades.extend(bt.trades)
            sym_pnls[sym] = bt.stats["total_pnl"]

        stats = compute_statistics(all_trades, cfg.account_size)
        
        results.append({
            "idx": idx,
            "trail_r": cfg.trail_r,
            "giveback": cfg.giveback_pct,
            "tp1": cfg.tp1_r,
            "tp2": cfg.tp2_r,
            "hard_stop": cfg.hard_stop_r,
            "be_r": cfg.breakeven_r,
            "rvol": cfg.rvol_min,
            "adx": cfg.adx_threshold,
            "trades": stats["total_trades"],
            "win_rate": stats["win_rate"],
            "pf": stats["profit_factor"],
            "exp_r": stats["expectancy_r"],
            "pnl": stats["total_pnl"],
            "max_dd": stats["max_drawdown"],
            "sharpe": stats["sharpe"],
            "sym_pnls": sym_pnls,
        })

        if (idx + 1) % 20 == 0:
            print(f"  {idx + 1}/{len(unique_configs)} tested...", flush=True)

    return results


def main():
    print("=== V1.3 Conduct Strategy Parameter Optimization ===\n")
    all_data = load_all_data()
    if not all_data:
        print("No data available.")
        return

    results = run_sweep(all_data)

    # Sort by Sharpe, then by PnL
    results.sort(key=lambda r: (r["sharpe"], r["pnl"]), reverse=True)

    # Top 20
    print(f"\n{'='*100}")
    print(f"  TOP 20 CONFIGURATIONS (sorted by Sharpe)")
    print(f"{'='*100}")

    rows = []
    for r in results[:20]:
        rows.append([
            r["idx"],
            f"{r['trail_r']:.1f}",
            f"{r['giveback']:.0%}",
            f"{r['tp1']:.1f}",
            f"{r['tp2']:.1f}",
            f"{r['hard_stop']:.1f}",
            f"{r['rvol']:.1f}",
            f"{r['adx']:.0f}",
            r["trades"],
            f"{r['win_rate']:.0%}",
            f"{r['pf']:.2f}",
            f"{r['exp_r']:.2f}R",
            f"${r['pnl']:.0f}",
            f"${r['max_dd']:.0f}",
            f"{r['sharpe']:.2f}",
        ])

    print(tabulate(rows, headers=[
        "#", "Trail", "Give%", "TP1", "TP2", "Stop", "RVOL", "ADX",
        "Trades", "WR", "PF", "Exp(R)", "PnL$", "MaxDD$", "Sharpe"
    ], tablefmt="simple"))

    # Best config details
    best = results[0]
    print(f"\n{'='*60}")
    print(f"  BEST CONFIG #{best['idx']}")
    print(f"{'='*60}")
    print(f"  Trail R:      {best['trail_r']}")
    print(f"  Giveback:     {best['giveback']:.0%}")
    print(f"  TP1:          {best['tp1']}R")
    print(f"  TP2:          {best['tp2']}R")
    print(f"  Hard Stop:    {best['hard_stop']}R")
    print(f"  BE at:        {best['be_r']}R")
    print(f"  RVOL min:     {best['rvol']}")
    print(f"  ADX min:      {best['adx']}")
    print(f"  Total PnL:    ${best['pnl']:.2f}")
    print(f"  Sharpe:       {best['sharpe']:.2f}")
    print(f"  Win Rate:     {best['win_rate']:.1%}")
    print(f"  Profit Factor:{best['pf']:.2f}")
    print(f"  Max DD:       ${best['max_dd']:.2f}")

    # Per-symbol breakdown for best
    print(f"\n  Per-symbol PnL:")
    for sym, pnl in best["sym_pnls"].items():
        tag = "+" if pnl > 0 else ""
        print(f"    {sym}: {tag}${pnl:.2f}")


if __name__ == "__main__":
    main()
