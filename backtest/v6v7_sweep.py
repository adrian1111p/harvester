"""
v6v7_sweep.py — Backtest V6 ORB + V7 9EMA Scalp vs all baselines on Basket A.

Conduct Strategy V2.0 sweep.

Tests:
  V6 configs: Default, Wide-OR, Tight-OR, VWAP-Only, No-VWAP
  V7 configs: Default, Tight, Loose, Aggressive, Conservative
  Baselines: V1.3-Trend, V2.0-Conduct, V3-Balanced, V4-Exh-Runner, V5-Tight, V5-PullbackVWAP
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
from backtest.strategy import ConductStrategyV13, StrategyConfig
from backtest.strategy_v3 import StrategyV3, V3Config
from backtest.strategy_v4 import StrategyV4, V4Config
from backtest.strategy_v5 import StrategyV5, V5Config
from backtest.strategy_v6 import StrategyV6, V6Config
from backtest.strategy_v7 import StrategyV7, V7Config

SYMBOLS = ["AAPL", "TSLA", "NVDA", "AMD", "META"]

# ═══════════════════════════════════════════════════════════════════════════
# V6 ORB configs
# ═══════════════════════════════════════════════════════════════════════════
v6_configs = {
    "V6-Default": V6Config(),
    "V6-Tight": V6Config(
        or_minutes=10,
        micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
        hard_stop_r=1.0, breakeven_r=0.3, giveback_pct=0.40,
        tp1_r=1.0, tp2_r=2.0, max_hold_bars=40,
    ),
    "V6-Wide": V6Config(
        or_minutes=30,
        min_range_atr=0.5, max_range_atr=15.0,
        hard_stop_r=2.0, tp1_r=2.0, tp2_r=4.0,
        max_hold_bars=90,
    ),
    "V6-NoVWAP": V6Config(require_vwap_align=False),
    "V6-VWAPOnly": V6Config(
        require_vwap_align=True,
        min_range_atr=0.2, rvol_min=0.5,
    ),
    "V6-NarrowOR": V6Config(
        or_minutes=5,
        min_range_atr=0.15, max_range_atr=15.0,
        micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
        tp1_r=0.8, tp2_r=1.5, max_hold_bars=30,
    ),
}

# ═══════════════════════════════════════════════════════════════════════════
# V7 9EMA Scalp configs
# ═══════════════════════════════════════════════════════════════════════════
v7_configs = {
    "V7-Default": V7Config(),
    "V7-Tight": V7Config(
        pullback_atr_proximity=0.15,
        micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
        hard_stop_r=1.0, breakeven_r=0.3, giveback_pct=0.40,
        tp1_r=0.8, tp2_r=1.5, max_hold_bars=30,
    ),
    "V7-Loose": V7Config(
        pullback_atr_proximity=0.35,
        rvol_min=0.5,
        require_volume_contraction=False,
        require_volume_expansion=False,
        require_candle_confirm=False,
        max_hold_bars=60,
    ),
    "V7-Aggressive": V7Config(
        skip_first_n_minutes=5,
        pullback_atr_proximity=0.3,
        ema_min_slope_atr=0.01,
        rsi_max_long=80.0, rsi_min_short=20.0,
        hard_stop_r=2.0, tp2_r=3.0,
        max_hold_bars=60,
    ),
    "V7-Conservative": V7Config(
        skip_first_n_minutes=15,
        pullback_atr_proximity=0.1,
        ema_min_slope_atr=0.04,
        rvol_min=1.2,
        hard_stop_r=1.0, tp1_r=0.5, tp2_r=1.0,
        max_hold_bars=20,
        max_ma_dist_atr=0.3,
    ),
    "V7-EMATrailOnly": V7Config(
        use_ema_trail=True,
        micro_trail_cents=0.0,
        ema_trail_buffer_atr=0.05,
        max_hold_bars=50,
    ),
}

# ═══════════════════════════════════════════════════════════════════════════
# Baselines
# ═══════════════════════════════════════════════════════════════════════════
baseline_cfgs = {
    "V1.3-Trend": StrategyConfig(
        trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
        hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
    ),
    "V2.0-Conduct": StrategyConfig(
        trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
        hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
        max_ma_dist_atr=0.5,
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
    "V5-Tight": V5Config(
        max_ma_dist_atr=0.3, micro_trail_cents=2.0, micro_trail_activate_cents=3.0,
        hard_stop_r=1.0, breakeven_r=0.3, giveback_pct=0.40,
        tp1_r=0.8, tp2_r=1.5, max_hold_bars=30,
    ),
    "V5-PullbackVWAP": V5Config(exhaustion_fade_enabled=False),
}


def run_strategy(name, cfg, symbol, data_1m, data_5m, data_15m, data_1h, data_1d):
    """Run a single strategy config on one symbol, return metrics dict."""
    if isinstance(cfg, V7Config):
        strat = StrategyV7(cfg)
    elif isinstance(cfg, V6Config):
        strat = StrategyV6(cfg)
    elif isinstance(cfg, V5Config):
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
        try:
            result = strat.simulate_trade(sig, data_1m)
        except Exception:
            continue
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

    exit_counts = {}
    for t in trades:
        reason = t.exit_reason.value
        exit_counts[reason] = exit_counts.get(reason, 0) + 1

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
    print("=" * 80)
    print("  V6 ORB + V7 9EMA SCALP SWEEP  — Conduct Strategy V2.0")
    print("  V6: Opening Range Breakout  |  V7: Ride the 9 EMA")
    print("=" * 80)

    # ── Load data ──
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

    # ── Combine all configs ──
    all_configs = {}
    for name, cfg in v6_configs.items():
        all_configs[name] = cfg
    for name, cfg in v7_configs.items():
        all_configs[name] = cfg
    for name, cfg in baseline_cfgs.items():
        all_configs[name] = cfg

    # ── Run all combos ──
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

    # Sort by PnL descending
    results.sort(key=lambda x: float(x["PnL"].replace("$", "").replace(",", "")),
                 reverse=True)

    print(f"\n{'='*80}")
    print("  ALL CONFIGS RANKED BY PnL")
    print(f"{'='*80}")
    print(tabulate(results, headers="keys", tablefmt="simple"))

    # ── V6 detail ──
    print(f"\n{'='*80}")
    print("  V6 ORB DETAIL")
    print(f"{'='*80}")
    v6_results = [r for r in results if r["Config"].startswith("V6")]
    if v6_results:
        print(tabulate(v6_results, headers="keys", tablefmt="simple"))
        best_v6 = v6_results[0]["Config"]
        cfg = v6_configs[best_v6]
        for sym in SYMBOLS:
            d = all_data[sym]
            r = run_strategy(best_v6, cfg, sym, d["1m"], d.get("5m"),
                             d.get("15m"), d.get("1h"), d.get("1D"))
            print(f"  {sym}: {r['trades']} trades, PnL=${r['pnl']:.2f}, WR={r['wr']:.0%}")
            if r.get("entry_counts"):
                print(f"    Entry types: {r['entry_counts']}")
            if r.get("exit_counts"):
                print(f"    Exit reasons: {r['exit_counts']}")

    # ── V7 detail ──
    print(f"\n{'='*80}")
    print("  V7 9EMA SCALP DETAIL")
    print(f"{'='*80}")
    v7_results = [r for r in results if r["Config"].startswith("V7")]
    if v7_results:
        print(tabulate(v7_results, headers="keys", tablefmt="simple"))
        best_v7 = v7_results[0]["Config"]
        cfg = v7_configs[best_v7]
        for sym in SYMBOLS:
            d = all_data[sym]
            r = run_strategy(best_v7, cfg, sym, d["1m"], d.get("5m"),
                             d.get("15m"), d.get("1h"), d.get("1D"))
            print(f"  {sym}: {r['trades']} trades, PnL=${r['pnl']:.2f}, WR={r['wr']:.0%}")
            if r.get("entry_counts"):
                print(f"    Entry types: {r['entry_counts']}")
            if r.get("exit_counts"):
                print(f"    Exit reasons: {r['exit_counts']}")

    # ═══════════════════════════════════════════════════════════════════════
    # OPTIMAL HYBRID V2 — best strategy per symbol (including V6+V7)
    # ═══════════════════════════════════════════════════════════════════════
    print(f"\n{'='*80}")
    print("  OPTIMAL HYBRID V2 (best strategy per symbol)")
    print(f"{'='*80}")
    hybrid_trades = 0
    hybrid_pnl = 0
    hybrid_assignments = {}

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
        hybrid_assignments[sym] = best_name
        print(f"  {sym} → {best_name}: {r['trades']} tr, PnL=${r['pnl']:.0f}, WR={r['wr']:.0%}, PF={r['pf']:.2f}")

    print(f"\n  HYBRID V2 TOTAL: {hybrid_trades} trades, PnL=${hybrid_pnl:.0f}")
    print(f"  Assignments: {hybrid_assignments}")
    print("=" * 80)


if __name__ == "__main__":
    main()
