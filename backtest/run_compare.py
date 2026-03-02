"""
run_compare.py -- Fetch sub-$50 data from IBKR, run BOTH strategies, compare results.

Strategies tested:
  A) V1.3 Conduct (trend-following, optimized)
  B) V2 Cycle   (buy-low-sell-high, L2 proxy)
  C) V2 Cycle   (long-only variant)
  D) V2 Cycle   (aggressive -- lower thresholds)

Symbols: liquid sub-$50 stocks with good day-trading movement.
"""
from __future__ import annotations

import asyncio
import sys
import os

# Python 3.14 ib_insync workaround
if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

from pathlib import Path
import numpy as np
import pandas as pd
from tabulate import tabulate

from backtest.data_fetcher import (
    DATA_DIR,
    connect_ib,
    fetch_all_timeframes,
    load_data,
    save_data,
)
from backtest.engine import compute_statistics, build_equity_curve, BacktestResult, run_backtest
from backtest.strategy import StrategyConfig, ConductStrategyV13, TradeResult, Side, ExitReason
from backtest.strategy_cycle import CycleConfig, CycleStrategyV2, CycleTradeResult
from backtest.strategy_v3 import V3Config, StrategyV3, V3TradeResult

# -- Symbols: sub-$50 with volume + movement ---------------------------------
# Good day-trading characteristics: high RVOL, ATR > 2%, good L2 book
SYMBOLS = ["SOFI", "F", "PLTR", "SNAP", "HOOD", "RIVN", "MARA", "NIO"]

TRIGGER_TF = "1m"
CONTEXT_TFS = ["5m", "15m", "1h", "1D"]
ALL_TFS = [TRIGGER_TF] + CONTEXT_TFS

PORT = 7497
CLIENT_ID = 82


def data_is_cached(symbol: str) -> bool:
    sym_dir = DATA_DIR / symbol.upper()
    if not sym_dir.exists():
        return False
    for tf in ALL_TFS:
        if not (sym_dir / f"{tf}.csv").exists():
            return False
    return True


def fetch_data_if_needed(symbols: list[str]) -> list[str]:
    """Fetch missing data. Returns list of symbols with data available."""
    need_fetch = [s for s in symbols if not data_is_cached(s)]
    available = [s for s in symbols if data_is_cached(s)]

    if not need_fetch:
        print(f"All data cached for: {', '.join(symbols)}")
        return symbols

    print(f"Fetching from IBKR: {', '.join(need_fetch)}")
    try:
        ib = connect_ib(port=PORT, client_id=CLIENT_ID)
    except Exception as e:
        print(f"  [WARN] TWS not available ({e}). Using cached data only.")
        return available

    try:
        for sym in need_fetch:
            print(f"  {sym}...", end=" ", flush=True)
            try:
                data = fetch_all_timeframes(ib, sym, ALL_TFS, pacing_delay=3.0)
                save_data(sym, data)
                available.append(sym)
                rows = {tf: len(df) for tf, df in data.items()}
                print(f"OK {rows}", flush=True)
            except Exception as e:
                print(f"FAILED ({e})", flush=True)
    finally:
        ib.disconnect()

    return available


def load_symbol_data(sym: str):
    """Load trigger + context DataFrames for a symbol."""
    df_trigger = load_data(sym, TRIGGER_TF)
    context = {}
    for tf in CONTEXT_TFS:
        try:
            context[tf] = load_data(sym, tf)
        except FileNotFoundError:
            context[tf] = None
    return df_trigger, context


# -- Strategy Configs ---------------------------------------------------------

def config_a_trend_optimized() -> StrategyConfig:
    """V1.3 Conduct -- optimized trend-following."""
    return StrategyConfig(
        trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
        hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
    )


def config_b_cycle_default() -> CycleConfig:
    """V2 Cycle -- default balanced config."""
    return CycleConfig(
        max_price=50.0,
        hard_stop_r=1.0, trail_r=0.8, tp1_r=1.0, tp2_r=2.0,
        breakeven_r=0.7, giveback_pct=0.50, max_hold_bars=60,
        stoch_oversold=20.0, stoch_overbought=80.0,
        bb_low_pctb=0.15, bb_high_pctb=0.85,
        ofi_min_signal=0.05, l2_liquidity_min=30.0,
    )


def config_c_cycle_longonly() -> CycleConfig:
    """V2 Cycle -- long-only (buy dips only)."""
    cfg = config_b_cycle_default()
    cfg.allow_short = False
    return cfg


def config_d_cycle_aggressive() -> CycleConfig:
    """V2 Cycle -- aggressive: lower entry thresholds, wider targets."""
    return CycleConfig(
        max_price=50.0,
        hard_stop_r=1.2, trail_r=1.0, tp1_r=1.5, tp2_r=3.0,
        breakeven_r=0.8, giveback_pct=0.60, max_hold_bars=90,
        stoch_oversold=25.0, stoch_overbought=75.0,
        bb_low_pctb=0.20, bb_high_pctb=0.80,
        ofi_min_signal=0.03, l2_liquidity_min=25.0,
        rvol_min=0.6,
    )


def config_e_cycle_tight() -> CycleConfig:
    """V2 Cycle -- tight scalp: fast in, fast out."""
    return CycleConfig(
        max_price=50.0,
        hard_stop_r=0.8, trail_r=0.5, tp1_r=0.8, tp2_r=1.5,
        breakeven_r=0.5, giveback_pct=0.40, max_hold_bars=30,
        stoch_oversold=15.0, stoch_overbought=85.0,
        bb_low_pctb=0.10, bb_high_pctb=0.90,
        ofi_min_signal=0.08, l2_liquidity_min=40.0,
        rvol_min=1.0,
    )


def config_f_v3_default() -> V3Config:
    """V3 VWAP+BB+Squeeze -- default."""
    return V3Config()


def config_g_v3_longonly() -> V3Config:
    """V3 -- long only."""
    cfg = V3Config()
    cfg.allow_short = False
    return cfg


def config_h_v3_wide() -> V3Config:
    """V3 -- wider stops and targets for noisier stocks."""
    return V3Config(
        hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
        breakeven_r=1.0, giveback_pct=0.65, max_hold_bars=120,
        vwap_stretch_atr=2.0,
        bb_entry_pctb_low=0.03, bb_entry_pctb_high=0.97,
        l2_liquidity_min=20.0, rvol_min=0.4,
    )


def config_i_v3_vwap_only() -> V3Config:
    """V3 -- VWAP reversion only, no BB/squeeze."""
    return V3Config(
        bb_enabled=False, squeeze_enabled=False,
        vwap_stretch_atr=1.2,
        hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.0,
    )


# -- Run helpers --------------------------------------------------------------

def run_trend_strategy(sym, df_trigger, context, cfg):
    """Run V1.3 Conduct (trend) strategy."""
    return run_backtest(
        symbol=sym, df_trigger=df_trigger, trigger_tf=TRIGGER_TF,
        df_5m=context.get("5m"), df_15m=context.get("15m"),
        df_1h=context.get("1h"), df_1d=context.get("1D"),
        config=cfg,
    )


def run_cycle_strategy(sym, df_trigger, context, cfg: CycleConfig):
    """Run V2 Cycle strategy and wrap result in BacktestResult."""
    strategy = CycleStrategyV2(cfg)
    signals = strategy.generate_signals(
        df_trigger,
        df_5m=context.get("5m"), df_15m=context.get("15m"),
        df_1h=context.get("1h"), df_1d=context.get("1D"),
    )

    # Simulate -- no overlapping
    trades = []
    next_bar = 0
    for sig in signals:
        if sig.bar_index < next_bar:
            continue
        result = strategy.simulate_trade(sig, df_trigger)
        if result is not None:
            # Convert to common TradeResult for stats
            trades.append(TradeResult(
                entry_bar=result.entry_bar,
                exit_bar=result.exit_bar,
                entry_time=result.entry_time,
                exit_time=result.exit_time,
                side=Side(result.side.value),
                entry_price=result.entry_price,
                exit_price=result.exit_price,
                stop_price=result.stop_price,
                position_size=result.position_size,
                pnl=result.pnl,
                pnl_r=result.pnl_r,
                exit_reason=ExitReason(result.exit_reason.value),
                peak_r=result.peak_r,
                bars_held=result.bars_held,
            ))
            next_bar = result.exit_bar + 1

    stats = compute_statistics(trades, cfg.account_size)
    equity = build_equity_curve(trades, cfg.account_size)
    return BacktestResult(
        symbol=sym, trigger_tf=TRIGGER_TF,
        config=StrategyConfig(),  # placeholder
        trades=trades, equity_curve=equity, stats=stats,
    )


def run_v3_strategy(sym, df_trigger, context, cfg: V3Config):
    """Run V3 strategy and wrap result in BacktestResult."""
    strategy = StrategyV3(cfg)
    signals = strategy.generate_signals(
        df_trigger,
        df_5m=context.get("5m"), df_15m=context.get("15m"),
        df_1h=context.get("1h"), df_1d=context.get("1D"),
    )

    trades = []
    next_bar = 0
    for sig in signals:
        if sig.bar_index < next_bar:
            continue
        result = strategy.simulate_trade(sig, df_trigger)
        if result is not None:
            trades.append(TradeResult(
                entry_bar=result.entry_bar,
                exit_bar=result.exit_bar,
                entry_time=result.entry_time,
                exit_time=result.exit_time,
                side=Side(result.side.value),
                entry_price=result.entry_price,
                exit_price=result.exit_price,
                stop_price=result.stop_price,
                position_size=result.position_size,
                pnl=result.pnl,
                pnl_r=result.pnl_r,
                exit_reason=ExitReason(result.exit_reason.value),
                peak_r=result.peak_r,
                bars_held=result.bars_held,
            ))
            next_bar = result.exit_bar + 1

    stats = compute_statistics(trades, cfg.account_size)
    equity = build_equity_curve(trades, cfg.account_size)
    return BacktestResult(
        symbol=sym, trigger_tf=TRIGGER_TF,
        config=StrategyConfig(),
        trades=trades, equity_curve=equity, stats=stats,
    )


# -- Main comparison ---------------------------------------------------------

def main():
    print("=" * 70)
    print("  HARVESTER STRATEGY COMPARISON -- Sub-$50 Stocks + L2 Proxy")
    print("=" * 70)
    print(f"Symbols: {', '.join(SYMBOLS)}")
    print(f"Trigger: {TRIGGER_TF} | Context: {', '.join(CONTEXT_TFS)}")
    print()

    # Step 1: Fetch data
    available = fetch_data_if_needed(SYMBOLS)
    if not available:
        print("No data available. Exiting.")
        return

    print(f"\nAvailable symbols: {', '.join(available)}\n")

    # Step 2: Define strategies
    strategies = {
        "A:Trend-V1.3": ("trend", config_a_trend_optimized()),
        "B:Cycle-Def": ("cycle", config_b_cycle_default()),
        "C:Cycle-Long": ("cycle", config_c_cycle_longonly()),
        "D:Cycle-Aggro": ("cycle", config_d_cycle_aggressive()),
        "E:Cycle-Tight": ("cycle", config_e_cycle_tight()),
        "F:V3-Default": ("v3", config_f_v3_default()),
        "G:V3-LongOnly": ("v3", config_g_v3_longonly()),
        "H:V3-Wide": ("v3", config_h_v3_wide()),
        "I:V3-VWAPonly": ("v3", config_i_v3_vwap_only()),
    }

    # Step 3: Run all strategies on all symbols
    all_results = {}
    for strat_name, (strat_type, cfg) in strategies.items():
        print(f"\n{'-'*60}")
        print(f"  Running: {strat_name}")
        print(f"{'-'*60}")

        strat_trades = []
        strat_pnl_by_sym = {}

        for sym in available:
            try:
                df_trigger, context = load_symbol_data(sym)
            except FileNotFoundError:
                continue

            if strat_type == "trend":
                bt = run_trend_strategy(sym, df_trigger, context, cfg)
            elif strat_type == "v3":
                bt = run_v3_strategy(sym, df_trigger, context, cfg)
            else:
                bt = run_cycle_strategy(sym, df_trigger, context, cfg)

            strat_trades.extend(bt.trades)
            strat_pnl_by_sym[sym] = bt.stats["total_pnl"]
            nt = bt.stats["total_trades"]
            wr = bt.stats["win_rate"]
            pnl = bt.stats["total_pnl"]
            print(f"  {sym:6s}  {nt:3d} trades  WR={wr:.0%}  PnL=${pnl:+.2f}", flush=True)

        # Aggregate
        agg_stats = compute_statistics(strat_trades, 25_000.0)
        all_results[strat_name] = {
            "trades": strat_trades,
            "stats": agg_stats,
            "per_sym": strat_pnl_by_sym,
        }

        nt = agg_stats["total_trades"]
        wr = agg_stats["win_rate"]
        pf = agg_stats["profit_factor"]
        pnl = agg_stats["total_pnl"]
        sh = agg_stats["sharpe"]
        exp = agg_stats["expectancy_r"]
        print(f"  -- TOTAL: {nt} trades | WR={wr:.0%} | PF={pf:.2f} | "
              f"Exp={exp:.2f}R | PnL=${pnl:+.0f} | Sharpe={sh:.2f}")

    # Step 4: Comparison table
    print(f"\n{'='*70}")
    print(f"  STRATEGY COMPARISON -- RANKED BY SHARPE")
    print(f"{'='*70}")

    rows = []
    for name, data in sorted(all_results.items(),
                              key=lambda x: x[1]["stats"]["sharpe"], reverse=True):
        s = data["stats"]
        rows.append([
            name,
            s["total_trades"],
            f"{s['win_rate']:.0%}",
            f"{s['profit_factor']:.2f}",
            f"{s['expectancy_r']:.2f}R",
            f"${s['total_pnl']:+.0f}",
            f"${s['max_drawdown']:.0f}",
            f"{s['sharpe']:.2f}",
            f"{s['long_trades']}L/{s['short_trades']}S",
        ])

    print(tabulate(rows, headers=[
        "Strategy", "Trades", "WR", "PF", "Exp", "PnL", "MaxDD", "Sharpe", "L/S"
    ], tablefmt="simple"))

    # Step 5: Per-symbol heatmap
    print(f"\n{'='*70}")
    print(f"  PER-SYMBOL PnL HEATMAP")
    print(f"{'='*70}")

    heatmap_rows = []
    for sym in available:
        row = [sym]
        for name in sorted(all_results.keys()):
            pnl = all_results[name]["per_sym"].get(sym, 0.0)
            row.append(f"${pnl:+.0f}")
        heatmap_rows.append(row)

    print(tabulate(heatmap_rows,
                   headers=["Symbol"] + sorted(all_results.keys()),
                   tablefmt="simple"))

    # Step 6: Winner
    best = max(all_results.items(), key=lambda x: x[1]["stats"]["sharpe"])
    best_name = best[0]
    best_stats = best[1]["stats"]
    print(f"\n{'='*70}")
    verdict = "PROFITABLE" if best_stats["total_pnl"] > 0 else "UNPROFITABLE"
    print(f"  WINNER: {best_name}")
    print(f"  {verdict}: ${best_stats['total_pnl']:+.2f} | "
          f"{best_stats['total_trades']} trades | "
          f"WR {best_stats['win_rate']:.0%} | "
          f"Sharpe {best_stats['sharpe']:.2f}")
    print(f"{'='*70}")

    # Write results to file
    outpath = Path(__file__).parent / "comparison_results.txt"
    with open(outpath, "w") as f:
        f.write("STRATEGY COMPARISON -- Sub-$50 Stocks + L2 Proxy\n")
        f.write(f"Symbols: {', '.join(available)}\n")
        f.write(f"Date: {pd.Timestamp.now().strftime('%Y-%m-%d %H:%M')}\n\n")
        f.write(tabulate(rows, headers=[
            "Strategy", "Trades", "WR", "PF", "Exp", "PnL", "MaxDD", "Sharpe", "L/S"
        ], tablefmt="simple"))
        f.write("\n\nPER-SYMBOL PnL:\n")
        f.write(tabulate(heatmap_rows,
                         headers=["Symbol"] + sorted(all_results.keys()),
                         tablefmt="simple"))
        f.write(f"\n\nWINNER: {best_name} | PnL=${best_stats['total_pnl']:+.2f} | "
                f"Sharpe={best_stats['sharpe']:.2f}\n")
    print(f"\nResults saved to {outpath}")


if __name__ == "__main__":
    main()
