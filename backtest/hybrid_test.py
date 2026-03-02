"""Per-symbol best strategy selection: hybrid portfolio."""
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

def run_v3_bt(sym, trig, ctx, cfg):
    strategy = StrategyV3(cfg)
    sigs = strategy.generate_signals(trig, ctx.get('5m'), ctx.get('15m'),
                                      ctx.get('1h'), ctx.get('1D'))
    trades = []
    nb = 0
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

def load_sym(sym):
    t = load_data(sym, '1m')
    c = {}
    for tf in ['5m', '15m', '1h', '1D']:
        try:
            c[tf] = load_data(sym, tf)
        except FileNotFoundError:
            c[tf] = None
    return t, c

basket_a = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']

cfg_trend = StrategyConfig(
    trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
    hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
)

v3_configs = {
    "V3-Balanced": V3Config(min_price=8.0, max_price=500.0,
        hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0),
    "V3-Aggressive": V3Config(min_price=8.0, max_price=500.0,
        hard_stop_r=2.0, trail_r=1.5, tp1_r=2.0, tp2_r=4.0, breakeven_r=1.2),
    "V3-LongLoose": V3Config(allow_long=True, allow_short=False,
        min_price=8.0, max_price=500.0,
        hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=4.0, breakeven_r=1.5,
        giveback_pct=0.75, max_hold_bars=120),
}

print("=" * 72)
print("  PER-SYMBOL STRATEGY COMPARISON (Basket A)")
print("=" * 72)

# Run every strategy on every symbol
results = {}  # {sym: {strategy_name: (trades, stats)}}
for sym in basket_a:
    trig, ctx = load_sym(sym)
    results[sym] = {}

    # Trend V1.3
    bt = run_backtest(sym, trig, '1m', ctx.get('5m'), ctx.get('15m'),
                      ctx.get('1h'), ctx.get('1D'), cfg_trend)
    results[sym]['Trend-V1.3'] = (bt.trades, bt.stats)

    # V3 variants
    for vname, vcfg in v3_configs.items():
        trades = run_v3_bt(sym, trig, ctx, vcfg)
        stats = compute_statistics(trades, 25000.0) if trades else {
            'total_trades': 0, 'win_rate': 0, 'profit_factor': 0,
            'total_pnl': 0, 'sharpe': 0}
        results[sym][vname] = (trades, stats)

# Print detailed comparison table
print(f"\n{'Symbol':<6} | {'Strategy':<15} | {'Trades':>6} | {'WR':>5} | {'PF':>5} | {'PnL':>8} | {'Sharpe':>6}")
print("-" * 72)
best_per_sym = {}
for sym in basket_a:
    best_pnl = -999999
    best_name = None
    for sname, (trades, stats) in results[sym].items():
        pnl = stats['total_pnl']
        wr = stats['win_rate']
        pf = stats['profit_factor']
        sh = stats['sharpe']
        n = stats['total_trades']
        marker = ""
        if pnl > best_pnl:
            best_pnl = pnl
            best_name = sname
        print(f"{sym:<6} | {sname:<15} | {n:>6} | {wr:>4.0%} | {pf:>5.2f} | ${pnl:>+7.0f} | {sh:>6.2f}")
    best_per_sym[sym] = best_name
    print(f"{'':6} | {'>>> BEST: '+best_name:<15}")
    print("-" * 72)

# Build hybrid portfolio
print("\n" + "=" * 72)
print("  HYBRID PORTFOLIO (Best strategy per symbol)")
print("=" * 72)
hybrid_trades = []
for sym in basket_a:
    best = best_per_sym[sym]
    trades, stats = results[sym][best]
    print(f"  {sym}: {best:<15} -> {stats['total_trades']}tr "
          f"WR={stats['win_rate']:.0%} PnL=${stats['total_pnl']:+.0f}")
    hybrid_trades.extend(trades)

hs = compute_statistics(hybrid_trades, 25000.0)
print(f"\n  HYBRID TOTAL: {hs['total_trades']}tr WR={hs['win_rate']:.0%} "
      f"PF={hs['profit_factor']:.2f} PnL=${hs['total_pnl']:+.0f} Sharpe={hs['sharpe']:.2f}")
print(f"  Max Drawdown: ${hs['max_drawdown']:.0f} | "
      f"Avg Win: ${hs['avg_win']:.0f} | Avg Loss: ${hs['avg_loss']:.0f}")

# Compare with standalone strategies
print("\n" + "=" * 72)
print("  FINAL COMPARISON")
print("=" * 72)
for sname in ['Trend-V1.3'] + list(v3_configs.keys()):
    all_trades = []
    for sym in basket_a:
        all_trades.extend(results[sym][sname][0])
    s = compute_statistics(all_trades, 25000.0) if all_trades else {
        'total_trades': 0, 'total_pnl': 0, 'sharpe': 0, 'win_rate': 0, 'profit_factor': 0}
    print(f"  {sname:<15}: {s['total_trades']:>3}tr WR={s['win_rate']:.0%} "
          f"PF={s['profit_factor']:.2f} PnL=${s['total_pnl']:+.0f} Sharpe={s['sharpe']:.2f}")

print(f"  {'HYBRID':<15}: {hs['total_trades']:>3}tr WR={hs['win_rate']:.0%} "
      f"PF={hs['profit_factor']:.2f} PnL=${hs['total_pnl']:+.0f} Sharpe={hs['sharpe']:.2f}")

best_all = max(
    [('Trend-V1.3', compute_statistics([t for s in basket_a for t in results[s]['Trend-V1.3'][0]], 25000.0))]
    + [(k, compute_statistics([t for s in basket_a for t in results[s][k][0]], 25000.0))
       for k in v3_configs.keys() if any(results[s][k][0] for s in basket_a)]
    + [('HYBRID', hs)],
    key=lambda x: x[1]['total_pnl']
)
print(f"\n  >>> WINNER: {best_all[0]} with PnL=${best_all[1]['total_pnl']:+.0f} "
      f"Sharpe={best_all[1]['sharpe']:.2f}")
