"""
v5_sweep.py — Backtest V5 Smart Mean-Reversion vs V1.3 and V3 on Basket A.

Tests:
  1. V5 default config
  2. V5 tighter micro-trail ($0.02)
  3. V5 wider pullback RSI
  4. V5 exhaustion-fade only
  5. V5 pullback + VWAP only (no exhaustion)
  6. V1.3 Trend (baseline)
  7. V3 Balanced (baseline)
  8. V4 Exhaustion Runner (baseline)
"""
import asyncio, sys, os
sys.path.insert(0, r"d:\Site\harvester")
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

import numpy as np
import pandas as pd
from tabulate import tabulate

from backtest.data_fetcher import load_data
from backtest.engine import run_backtest
from backtest.strategy import ConductStrategyV13, StrategyConfig
from backtest.strategy_v3 import StrategyV3, V3Config
from backtest.strategy_v4 import StrategyV4, V4Config
from backtest.strategy_v5 import StrategyV5, V5Config

SYMBOLS = ["AAPL", "TSLA", "NVDA", "AMD", "META"]

# ── Configs ──
configs = {
    # V5 variants
    "V5-Default": V5Config(),
    "V5-MicroTrail2c": V5Config(micro_trail_cents=2.0, micro_trail_activate_cents=4.0),
    "V5-WideRSI": V5Config(pullback_rsi_low=45.0, pullback_rsi_high=55.0),
    "V5-ExhFadeOnly": V5Config(pullback_enabled=False, vwap_enabled=False,
                                exhaustion_fade_enabled=True),
    "V5-PullbackVWAP": V5Config(exhaustion_fade_enabled=False),
    "V5-Tight": V5Config(
        max_ma_dist_atr=0.3, micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
        hard_stop_r=1.0, breakeven_r=0.3, giveback_pct=0.40,
        tp1_r=0.8, tp2_r=1.5, max_hold_bars=30,
    ),
    "V5-Loose": V5Config(
        max_ma_dist_atr=1.0, exhaustion_dist_atr=2.5,
        pullback_rsi_low=45.0, pullback_rsi_high=55.0,
        require_candle_confirm=False, rvol_min=0.5,
        micro_trail_cents=5.0, micro_trail_activate_cents=8.0,
    ),
}

# Baselines
baseline_cfgs = {
    "V1.3-Trend": StrategyConfig(
        trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
        hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
    ),
    "V3-Balanced": V3Config(
        min_price=8.0, max_price=500.0,
        hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0,
    ),
    "V4-Exh-Runner": V4Config(
        enhanced_min_score=2,
        enable_buy_setup=False, enable_sell_setup=False,
        enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
        exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
        exhaustion_reversal_bars=3,
        hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0,
        breakeven_r=1.5, giveback_pct=0.80, max_hold_bars=180,
    ),
}


def run_strategy(name, cfg, symbol, data_1m, data_5m, data_15m, data_1h, data_1d):
    """Run a single strategy config on one symbol."""
    if isinstance(cfg, V5Config):
        strat = StrategyV5(cfg)
    elif isinstance(cfg, V3Config):
        strat = StrategyV3(cfg)
    elif isinstance(cfg, V4Config):
        strat = StrategyV4(cfg)
    else:
        strat = ConductStrategyV13(cfg)

    try:
        signals = strat.generate_signals(data_1m, data_5m, data_15m, data_1h, data_1d)
    except Exception as e:
        print(f"    [{name}/{symbol}] Signal error: {e}")
        return {"trades": 0, "pnl": 0, "wr": 0, "pf": 0, "sharpe": 0}
    if not signals:
        return {"trades": 0, "pnl": 0, "wr": 0, "pf": 0, "sharpe": 0}

    trades = []
    for sig in signals:
        result = strat.simulate_trade(sig, data_1m)
        if result:
            trades.append(result)

    if not trades:
        return {"trades": 0, "pnl": 0, "wr": 0, "pf": 0, "sharpe": 0}

    pnls = [t.pnl for t in trades]
    wins = [p for p in pnls if p > 0]
    losses = [p for p in pnls if p <= 0]
    total_pnl = sum(pnls)
    wr = len(wins) / len(trades) if trades else 0
    gross_win = sum(wins) if wins else 0
    gross_loss = abs(sum(losses)) if losses else 0.001
    pf = gross_win / gross_loss if gross_loss > 0 else 99
    sharpe = (np.mean(pnls) / np.std(pnls) * np.sqrt(252)) if np.std(pnls) > 0 else 0

    # Count by exit type
    exit_counts = {}
    for t in trades:
        reason = t.exit_reason.value
        exit_counts[reason] = exit_counts.get(reason, 0) + 1

    # Count by entry type
    entry_counts = {}
    for t in trades:
        etype = getattr(t, "entry_type", "?")
        entry_counts[etype] = entry_counts.get(etype, 0) + 1

    return {
        "trades": len(trades), "pnl": total_pnl, "wr": wr,
        "pf": pf, "sharpe": sharpe,
        "avg_win": np.mean(wins) if wins else 0,
        "avg_loss": np.mean(losses) if losses else 0,
        "exit_counts": exit_counts,
        "entry_counts": entry_counts,
    }


def main():
    print("=" * 70)
    print("  V5 SMART MEAN-REVERSION SWEEP")
    print("  Key improvements: 20MA exhaustion filter + micro-trailing")
    print("=" * 70)

    # Load data
    all_data = {}
    for sym in SYMBOLS:
        d = {}
        for tf in ["1m", "5m", "15m", "1h", "1D"]:
            try:
                d[tf] = load_data(sym, tf)
            except Exception:
                d[tf] = None
        all_data[sym] = d
        bars = len(d["1m"]) if d["1m"] is not None else 0
        print(f"  {sym}: {bars} 1m bars loaded")

    # All configs
    all_configs = {}
    for name, cfg in configs.items():
        all_configs[name] = cfg
    for name, cfg in baseline_cfgs.items():
        all_configs[name] = cfg

    # Run all combos
    results = []
    for name, cfg in all_configs.items():
        total_trades = 0
        total_pnl = 0
        all_pnls = []
        per_sym = {}

        for sym in SYMBOLS:
            d = all_data[sym]
            r = run_strategy(name, cfg, sym, d["1m"], d.get("5m"),
                           d.get("15m"), d.get("1h"), d.get("1D"))
            total_trades += r["trades"]
            total_pnl += r["pnl"]
            per_sym[sym] = r

        # Aggregate
        for sym in SYMBOLS:
            r = per_sym[sym]
            if r["trades"] > 0:
                all_pnls.extend([r["pnl"] / max(r["trades"], 1)] * r["trades"])

        wins = [p for p in all_pnls if p > 0]
        losses = [p for p in all_pnls if p <= 0]
        wr = len(wins) / len(all_pnls) if all_pnls else 0
        gw = sum(wins) if wins else 0
        gl = abs(sum(losses)) if losses else 0.001
        pf = gw / gl if gl > 0 else 99
        sharpe = (np.mean(all_pnls) / np.std(all_pnls) * np.sqrt(252)) if len(all_pnls) > 1 and np.std(all_pnls) > 0 else 0

        results.append({
            "Config": name,
            "Trades": total_trades,
            "WR%": f"{wr:.0%}",
            "PF": f"{pf:.2f}",
            "PnL": f"${total_pnl:.0f}",
            "Sharpe": f"{sharpe:.2f}",
            **{sym: f"${per_sym[sym]['pnl']:.0f}" for sym in SYMBOLS},
        })

    # Sort by PnL
    results.sort(key=lambda x: float(x["PnL"].replace("$", "").replace(",", "")),
                 reverse=True)

    print("\n" + tabulate(results, headers="keys", tablefmt="simple"))

    # ── Detailed best V5 ──
    best_v5 = None
    best_pnl = -999999
    for name, cfg in configs.items():
        total_p = 0
        for sym in SYMBOLS:
            d = all_data[sym]
            r = run_strategy(name, cfg, sym, d["1m"], d.get("5m"),
                           d.get("15m"), d.get("1h"), d.get("1D"))
            total_p += r["pnl"]
        if total_p > best_pnl:
            best_pnl = total_p
            best_v5 = name

    if best_v5:
        print(f"\n{'='*70}")
        print(f"  BEST V5: {best_v5} (PnL: ${best_pnl:.0f})")
        print(f"{'='*70}")
        cfg = configs[best_v5]
        for sym in SYMBOLS:
            d = all_data[sym]
            r = run_strategy(best_v5, cfg, sym, d["1m"], d.get("5m"),
                           d.get("15m"), d.get("1h"), d.get("1D"))
            print(f"\n  {sym}: {r['trades']} trades, PnL=${r['pnl']:.2f}, WR={r['wr']:.0%}")
            if r.get("entry_counts"):
                print(f"    Entry types: {r['entry_counts']}")
            if r.get("exit_counts"):
                print(f"    Exit reasons: {r['exit_counts']}")

    # ── Build optimal hybrid ──
    print(f"\n{'='*70}")
    print("  OPTIMAL HYBRID (best strategy per symbol)")
    print(f"{'='*70}")
    hybrid_trades = 0
    hybrid_pnl = 0
    for sym in SYMBOLS:
        d = all_data[sym]
        best_name = None
        best_sym_pnl = -999999
        for name, cfg in all_configs.items():
            r = run_strategy(name, cfg, sym, d["1m"], d.get("5m"),
                           d.get("15m"), d.get("1h"), d.get("1D"))
            if r["pnl"] > best_sym_pnl:
                best_sym_pnl = r["pnl"]
                best_name = name
        r = run_strategy(best_name, all_configs[best_name], sym,
                        d["1m"], d.get("5m"), d.get("15m"), d.get("1h"), d.get("1D"))
        hybrid_trades += r["trades"]
        hybrid_pnl += r["pnl"]
        print(f"  {sym} → {best_name}: {r['trades']} tr, PnL=${r['pnl']:.0f}, WR={r['wr']:.0%}")

    print(f"\n  HYBRID TOTAL: {hybrid_trades} trades, PnL=${hybrid_pnl:.0f}")
    print("=" * 70)


if __name__ == "__main__":
    main()
