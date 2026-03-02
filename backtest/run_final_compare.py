"""
run_final_compare.py — Comprehensive strategy test across BOTH baskets.

Tests 3 strategy families:
  1. V1.3 Trend (optimized) — works on trending stocks
  2. V3 VWAP+BB+Squeeze — mean reversion for cyclical stocks
  3. V3 Short-Only — ride downtrends on bearish sub-$50 stocks

Tests on:
  Basket A: AAPL, TSLA, NVDA, AMD, META  (higher-priced, trending)
  Basket B: SOFI, F, RIVN, MARA           (sub-$50, good volume, 8-50 range)
"""
from __future__ import annotations

import asyncio
import sys
import os

if sys.version_info >= (3, 14):
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        asyncio.set_event_loop(asyncio.new_event_loop())

import numpy as np
import pandas as pd
from tabulate import tabulate
from pathlib import Path

from backtest.data_fetcher import DATA_DIR, connect_ib, fetch_all_timeframes, load_data, save_data
from backtest.engine import compute_statistics, build_equity_curve, BacktestResult, run_backtest
from backtest.strategy import StrategyConfig, ConductStrategyV13, TradeResult, Side, ExitReason
from backtest.strategy_v3 import V3Config, StrategyV3, V3TradeResult

TRIGGER_TF = "1m"
CONTEXT_TFS = ["5m", "15m", "1h", "1D"]
ALL_TFS = [TRIGGER_TF] + CONTEXT_TFS
PORT = 7497
CLIENT_ID = 83

BASKET_A = ["AAPL", "TSLA", "NVDA", "AMD", "META"]
BASKET_B = ["SOFI", "F", "RIVN", "MARA"]


def data_cached(sym):
    d = DATA_DIR / sym
    return d.exists() and all((d / f"{tf}.csv").exists() for tf in ALL_TFS)


def ensure_data(symbols):
    need = [s for s in symbols if not data_cached(s)]
    if not need:
        return symbols
    print(f"  Fetching: {', '.join(need)}", flush=True)
    try:
        ib = connect_ib(port=PORT, client_id=CLIENT_ID)
    except Exception as e:
        print(f"  TWS unavailable: {e}")
        return [s for s in symbols if data_cached(s)]
    try:
        for sym in need:
            try:
                data = fetch_all_timeframes(ib, sym, ALL_TFS, pacing_delay=3.0)
                save_data(sym, data)
            except Exception as e:
                print(f"  {sym} FAILED: {e}")
    finally:
        ib.disconnect()
    return [s for s in symbols if data_cached(s)]


def load_sym(sym):
    trig = load_data(sym, TRIGGER_TF)
    ctx = {}
    for tf in CONTEXT_TFS:
        try:
            ctx[tf] = load_data(sym, tf)
        except FileNotFoundError:
            ctx[tf] = None
    return trig, ctx


def run_trend(sym, trig, ctx, cfg):
    return run_backtest(sym, trig, TRIGGER_TF,
                        ctx.get("5m"), ctx.get("15m"), ctx.get("1h"), ctx.get("1D"), cfg)


def run_v3(sym, trig, ctx, cfg: V3Config):
    strategy = StrategyV3(cfg)
    sigs = strategy.generate_signals(trig, ctx.get("5m"), ctx.get("15m"),
                                      ctx.get("1h"), ctx.get("1D"))
    trades = []
    next_bar = 0
    for sig in sigs:
        if sig.bar_index < next_bar:
            continue
        r = strategy.simulate_trade(sig, trig)
        if r:
            trades.append(TradeResult(
                entry_bar=r.entry_bar, exit_bar=r.exit_bar,
                entry_time=r.entry_time, exit_time=r.exit_time,
                side=Side(r.side.value), entry_price=r.entry_price,
                exit_price=r.exit_price, stop_price=r.stop_price,
                position_size=r.position_size, pnl=r.pnl, pnl_r=r.pnl_r,
                exit_reason=ExitReason(r.exit_reason.value),
                peak_r=r.peak_r, bars_held=r.bars_held,
            ))
            next_bar = r.exit_bar + 1
    stats = compute_statistics(trades, cfg.account_size)
    eq = build_equity_curve(trades, cfg.account_size)
    return BacktestResult(sym, TRIGGER_TF, StrategyConfig(), trades, eq, stats)


# ── Strategy configs ────────────────────────────────────────────────────────

def trend_optimized():
    """V1.3 Trend (paper-validated optimized)."""
    return StrategyConfig(
        trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
        hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
    )

def v3_balanced():
    """V3 balanced: VWAP+BB+Squeeze, both directions."""
    return V3Config(
        hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.5,
        breakeven_r=0.8, giveback_pct=0.60, max_hold_bars=90,
        vwap_stretch_atr=1.5, bb_entry_pctb_low=0.05, bb_entry_pctb_high=0.95,
        l2_liquidity_min=25.0, rvol_min=0.5, min_price=8.0, max_price=500.0,
    )

def v3_short_only():
    """V3 short-only for bearish stocks."""
    cfg = v3_balanced()
    cfg.allow_long = False
    cfg.max_price = 50.0
    return cfg

def v3_long_only_loose():
    """V3 long-only with loose filters for cycle buying."""
    return V3Config(
        allow_short=False,
        hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
        breakeven_r=1.0, giveback_pct=0.65, max_hold_bars=120,
        vwap_stretch_atr=2.0,
        bb_entry_pctb_low=0.03, bb_entry_pctb_high=0.97,
        l2_liquidity_min=20.0, rvol_min=0.4,
        rsi_oversold=40.0, rsi_overbought=60.0,
        min_price=8.0, max_price=500.0,
    )

def v3_scalp():
    """V3 quick scalp: fast trades at extreme levels."""
    return V3Config(
        hard_stop_r=1.0, trail_r=0.6, tp1_r=0.7, tp2_r=1.5,
        breakeven_r=0.5, giveback_pct=0.45, max_hold_bars=30,
        vwap_stretch_atr=1.2,
        bb_entry_pctb_low=0.02, bb_entry_pctb_high=0.98,
        l2_liquidity_min=30.0, rvol_min=0.7,
        min_price=8.0, max_price=500.0,
    )


def main():
    print("=" * 70)
    print("  HARVESTER FINAL STRATEGY COMPARISON")
    print("  Basket A (trending): AAPL TSLA NVDA AMD META")
    print("  Basket B (sub-$50):  SOFI F RIVN MARA")
    print("=" * 70, flush=True)

    # Ensure data
    avail_a = ensure_data(BASKET_A)
    avail_b = ensure_data(BASKET_B)
    print(f"\nAvailable: A={avail_a}  B={avail_b}\n", flush=True)

    # Load all data upfront
    all_data = {}
    for sym in avail_a + avail_b:
        if sym not in all_data:
            try:
                all_data[sym] = load_sym(sym)
            except Exception as e:
                print(f"  Skip {sym}: {e}")

    # Define test matrix
    tests = [
        # (name, strategy_type, config_fn, symbols)
        ("Trend-V1.3-A",   "trend", trend_optimized(),    avail_a),
        ("V3-Balanced-A",  "v3",    v3_balanced(),         avail_a),
        ("V3-LongLoose-A", "v3",    v3_long_only_loose(),  avail_a),
        ("V3-Scalp-A",     "v3",    v3_scalp(),            avail_a),
        ("Trend-V1.3-B",   "trend", trend_optimized(),    avail_b),
        ("V3-Balanced-B",  "v3",    v3_balanced(),         avail_b),
        ("V3-ShortOnly-B", "v3",    v3_short_only(),       avail_b),
        ("V3-Scalp-B",     "v3",    v3_scalp(),            avail_b),
        # Cross-basket
        ("Trend-V1.3-ALL", "trend", trend_optimized(),    avail_a + avail_b),
        ("V3-Balanced-ALL","v3",    v3_balanced(),         avail_a + avail_b),
    ]

    results = {}
    for name, stype, cfg, syms in tests:
        print(f"\n--- {name} ({len(syms)} symbols) ---", flush=True)
        all_trades = []
        per_sym = {}
        for sym in syms:
            if sym not in all_data:
                continue
            trig, ctx = all_data[sym]
            if stype == "trend":
                bt = run_trend(sym, trig, ctx, cfg)
            else:
                bt = run_v3(sym, trig, ctx, cfg)
            all_trades.extend(bt.trades)
            per_sym[sym] = bt.stats["total_pnl"]
            nt = bt.stats["total_trades"]
            wr = bt.stats["win_rate"]
            pnl = bt.stats["total_pnl"]
            print(f"  {sym:5s} {nt:3d}tr WR={wr:.0%} PnL=${pnl:+.0f}", flush=True)

        agg = compute_statistics(all_trades, 25_000.0)
        results[name] = {"stats": agg, "per_sym": per_sym, "trades": all_trades}
        print(f"  => {agg['total_trades']}tr WR={agg['win_rate']:.0%} "
              f"PF={agg['profit_factor']:.2f} Exp={agg['expectancy_r']:.2f}R "
              f"PnL=${agg['total_pnl']:+.0f} Sharpe={agg['sharpe']:.2f}", flush=True)

    # ── Ranking ──
    print(f"\n{'='*70}")
    print("  FINAL RANKING BY SHARPE")
    print(f"{'='*70}")

    rows = []
    for name, data in sorted(results.items(),
                              key=lambda x: x[1]["stats"]["sharpe"], reverse=True):
        s = data["stats"]
        rows.append([
            name, s["total_trades"],
            f"{s['win_rate']:.0%}", f"{s['profit_factor']:.2f}",
            f"{s['expectancy_r']:.2f}R", f"${s['total_pnl']:+.0f}",
            f"${s['max_drawdown']:.0f}", f"{s['sharpe']:.2f}",
            f"{s['long_trades']}L/{s['short_trades']}S",
        ])

    print(tabulate(rows, headers=[
        "Strategy", "Trades", "WR", "PF", "Exp", "PnL", "MaxDD", "Sharpe", "L/S"
    ], tablefmt="simple"))

    # Per-symbol for top 3
    ranked = sorted(results.items(), key=lambda x: x[1]["stats"]["sharpe"], reverse=True)
    print(f"\n{'='*70}")
    print("  TOP 3 - PER SYMBOL BREAKDOWN")
    print(f"{'='*70}")
    for rank, (name, data) in enumerate(ranked[:3], 1):
        s = data["stats"]
        print(f"\n  #{rank} {name}: PnL=${s['total_pnl']:+.2f} | "
              f"Sharpe={s['sharpe']:.2f} | WR={s['win_rate']:.0%}")
        for sym, pnl in sorted(data["per_sym"].items(), key=lambda x: x[1], reverse=True):
            marker = "+" if pnl > 0 else "-"
            print(f"    {marker} {sym:5s}  ${pnl:+.2f}")

    # Verdict
    best_name, best_data = ranked[0]
    best_s = best_data["stats"]
    print(f"\n{'='*70}")
    verdict = "PROFITABLE" if best_s["total_pnl"] > 0 else "UNPROFITABLE"
    print(f"  BEST STRATEGY: {best_name}")
    print(f"  {verdict}: ${best_s['total_pnl']:+.2f} | {best_s['total_trades']} trades | "
          f"WR {best_s['win_rate']:.0%} | Sharpe {best_s['sharpe']:.2f}")
    print(f"{'='*70}")

    # Save
    out = Path(__file__).parent / "final_comparison.txt"
    with open(out, "w", encoding="utf-8") as f:
        f.write("FINAL STRATEGY COMPARISON\n")
        f.write(f"Date: {pd.Timestamp.now()}\n\n")
        f.write(tabulate(rows, headers=[
            "Strategy", "Trades", "WR", "PF", "Exp", "PnL", "MaxDD", "Sharpe", "L/S"
        ], tablefmt="simple"))
        f.write(f"\n\nBEST: {best_name} PnL=${best_s['total_pnl']:+.2f} "
                f"Sharpe={best_s['sharpe']:.2f}\n")
        for rank, (name, data) in enumerate(ranked[:3], 1):
            s = data["stats"]
            f.write(f"\n#{rank} {name}: PnL=${s['total_pnl']:+.2f} Sharpe={s['sharpe']:.2f}\n")
            for sym, pnl in sorted(data["per_sym"].items(), key=lambda x: x[1], reverse=True):
                f.write(f"  {sym}: ${pnl:+.2f}\n")
    print(f"\nResults saved to {out}")


if __name__ == "__main__":
    main()
