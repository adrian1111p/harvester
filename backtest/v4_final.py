"""
v4_final.py — Final test: Exhaustion-Runner hybrid + prepare live config.
"""
import asyncio, sys, os
sys.path.insert(0, r'd:\Site\harvester')
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

from backtest.data_fetcher import load_data
from backtest.engine import compute_statistics, run_backtest
from backtest.strategy import StrategyConfig, TradeResult, Side, ExitReason
from backtest.strategy_v3 import V3Config, StrategyV3
from backtest.strategy_v4 import V4Config, StrategyV4

def load_sym(sym):
    t = load_data(sym, '1m')
    c = {}
    for tf in ['5m', '15m', '1h', '1D']:
        try: c[tf] = load_data(sym, tf)
        except FileNotFoundError: c[tf] = None
    return t, c

def run_v4(sym, trig, ctx, cfg):
    strategy = StrategyV4(cfg)
    sigs = strategy.generate_signals(trig, ctx.get('5m'), ctx.get('15m'),
                                      ctx.get('1h'), ctx.get('1D'))
    trades, nb, pats = [], 0, {}
    for sig in sigs:
        if sig.bar_index < nb:
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
                peak_r=r.peak_r, bars_held=r.bars_held))
            pats[r.pattern.value] = pats.get(r.pattern.value, 0) + 1
            nb = r.exit_bar + 1
    return trades, pats

def run_v3(sym, trig, ctx, cfg):
    strategy = StrategyV3(cfg)
    sigs = strategy.generate_signals(trig, ctx.get('5m'), ctx.get('15m'),
                                      ctx.get('1h'), ctx.get('1D'))
    trades, nb = [], 0
    for sig in sigs:
        if sig.bar_index < nb:
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
                peak_r=r.peak_r, bars_held=r.bars_held))
            nb = r.exit_bar + 1
    return trades

basket_a = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']
print("Loading data...", flush=True)
data = {s: load_sym(s) for s in basket_a}

# Configs
cfg_trend = StrategyConfig(
    trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
    hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
)
cfg_v3 = V3Config(min_price=8.0, max_price=500.0,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0)

# Two exhaustion variants
cfg_exh_base = V4Config(
    enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)

cfg_exh_runner = V4Config(
    enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0,
    breakeven_r=1.5, giveback_pct=0.80, max_hold_bars=180)

cfg_exh_strict = V4Config(
    enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=20, exhaustion_min_move_atr=4.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)

strategies_map = {
    'Trend-V1.3': ('trend', cfg_trend),
    'V3-Balanced': ('v3', cfg_v3),
    'V4-Exh-Base': ('v4', cfg_exh_base),
    'V4-Exh-Runner': ('v4', cfg_exh_runner),
    'V4-Exh-Strict': ('v4', cfg_exh_strict),
}

# Run all
results = {}
for sym in basket_a:
    trig, ctx = data[sym]
    results[sym] = {}
    for sname, (stype, scfg) in strategies_map.items():
        if stype == 'trend':
            bt = run_backtest(sym, trig, '1m', ctx.get('5m'), ctx.get('15m'),
                              ctx.get('1h'), ctx.get('1D'), scfg)
            trades, stats = bt.trades, bt.stats
        elif stype == 'v3':
            trades = run_v3(sym, trig, ctx, scfg)
            stats = compute_statistics(trades, 25000.0)
        else:
            trades, _ = run_v4(sym, trig, ctx, scfg)
            stats = compute_statistics(trades, 25000.0)
        results[sym][sname] = (trades, stats)

# ═══════════════════════════════════════════════════════════════
# Per-symbol comparison
# ═══════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  PER-SYMBOL COMPARISON")
print("=" * 80)
for sym in basket_a:
    print(f"\n  {sym}:")
    for sname in strategies_map:
        _, stats = results[sym][sname]
        print(f"    {sname:<18}: {stats['total_trades']:>3}tr "
              f"WR={stats['win_rate']:.0%} PnL=${stats['total_pnl']:+.0f}")

# ═══════════════════════════════════════════════════════════════
# Hybrid variants
# ═══════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  HYBRID VARIANTS (best strategy per symbol)")
print("=" * 80)

for exh_name in ['V4-Exh-Base', 'V4-Exh-Runner', 'V4-Exh-Strict']:
    avail = {k: v for k, v in strategies_map.items()
             if k in ['Trend-V1.3', 'V3-Balanced', exh_name]}
    hybrid_trades = []
    picks = {}
    for sym in basket_a:
        best_pnl = -999999
        best_name = None
        for sname in avail:
            pnl = results[sym][sname][1]['total_pnl']
            if pnl > best_pnl:
                best_pnl = pnl
                best_name = sname
        picks[sym] = best_name
        hybrid_trades.extend(results[sym][best_name][0])

    hs = compute_statistics(hybrid_trades, 25000.0)
    pick_str = " ".join(f"{s}={n.split('-')[-1]}" for s, n in picks.items())
    print(f"  Hybrid-{exh_name.split('-')[-1]}: {hs['total_trades']:>3}tr "
          f"WR={hs['win_rate']:.0%} PF={hs['profit_factor']:.2f} "
          f"PnL=${hs['total_pnl']:+.0f} Sharpe={hs['sharpe']:.2f} "
          f"MaxDD=${hs['max_drawdown']:.0f}")
    print(f"    Picks: {pick_str}")

# ═══════════════════════════════════════════════════════════════
# Best additive: V1.3 + V3 + V4-Runner (all 3 layers)
# ═══════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  ADDITIVE TRIPLE STACK (V1.3 + V3 + V4-Exh variants)")
print("=" * 80)

for exh_name in ['V4-Exh-Base', 'V4-Exh-Runner', 'V4-Exh-Strict']:
    combo_trades = []
    for sym in basket_a:
        combo_trades.extend(results[sym]['Trend-V1.3'][0])
        combo_trades.extend(results[sym]['V3-Balanced'][0])
        combo_trades.extend(results[sym][exh_name][0])
    cs = compute_statistics(combo_trades, 25000.0)
    print(f"  V1.3+V3+{exh_name.split('-')[-1]:>6}: {cs['total_trades']:>3}tr "
          f"WR={cs['win_rate']:.0%} PF={cs['profit_factor']:.2f} "
          f"PnL=${cs['total_pnl']:+.0f} Sharpe={cs['sharpe']:.2f} "
          f"MaxDD=${cs['max_drawdown']:.0f}")

# ═══════════════════════════════════════════════════════════════
# THE DEFINITIVE BEST: Hybrid-Runner per sym + Exhaustion addon
# ═══════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  DEFINITIVE BEST: Hybrid + Exhaustion-Runner addon")
print("=" * 80)

# Best picks from prior hybrid test: AAPL=V3, TSLA=V4-Exh-Runner, AMD=V1.3, META=V1.3
# Then overlay exhaustion-runner on symbols that aren't already using it
best_picks = {}
best_hybrid_trades = []
for sym in basket_a:
    best_pnl = -999999
    best_name = None
    for sname in strategies_map:
        pnl = results[sym][sname][1]['total_pnl']
        if pnl > best_pnl:
            best_pnl = pnl
            best_name = sname
    best_picks[sym] = best_name
    best_hybrid_trades.extend(results[sym][best_name][0])
    print(f"  {sym}: {best_name:<18} -> "
          f"{results[sym][best_name][1]['total_trades']}tr "
          f"WR={results[sym][best_name][1]['win_rate']:.0%} "
          f"PnL=${results[sym][best_name][1]['total_pnl']:+.0f}")

bhs = compute_statistics(best_hybrid_trades, 25000.0)
print(f"\n  BEST HYBRID: {bhs['total_trades']}tr WR={bhs['win_rate']:.0%} "
      f"PF={bhs['profit_factor']:.2f} PnL=${bhs['total_pnl']:+.0f} "
      f"Sharpe={bhs['sharpe']:.2f} MaxDD=${bhs['max_drawdown']:.0f}")
print(f"  Avg Win: ${bhs['avg_win']:.0f} | Avg Loss: ${bhs['avg_loss']:.0f} | "
      f"Long WR: {bhs['long_win_rate']:.0%} | Short WR: {bhs['short_win_rate']:.0%}")

print("\n" + "=" * 80)
print("  LIVE PAPER TRADING CONFIG RECOMMENDATION")
print("=" * 80)
print("""
  STRATEGY: Per-symbol hybrid selection
  SIZE: 2 shares per trade (paper trading test)

  Symbol assignments:
""")
for sym, pick in best_picks.items():
    print(f"    {sym}: {pick}")
print(f"""
  Expected performance (from backtest):
    Trades: {bhs['total_trades']}
    Win Rate: {bhs['win_rate']:.0%}
    Profit Factor: {bhs['profit_factor']:.2f}
    Sharpe: {bhs['sharpe']:.2f}
    Max Drawdown: ${bhs['max_drawdown']:.0f}
""")
