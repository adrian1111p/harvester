"""Quick combined test: V3 short-only on bearish sub-$50 + Trend V1.3 on Basket A."""
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

# ── V3 Short-only on sub-$50 with multiple configs ──
print("=" * 60)
print("  V3 SHORT-ONLY on Sub-$50 Bearish Stocks")
print("=" * 60)
sub50_syms = ['SOFI', 'F', 'RIVN', 'MARA']
sub50_data = {s: load_sym(s) for s in sub50_syms}

short_configs = [
    ("Wide", V3Config(allow_long=False, min_price=8.0, max_price=50.0,
        hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0,
        giveback_pct=0.70, max_hold_bars=120, rvol_min=0.3)),
    ("Tight", V3Config(allow_long=False, min_price=8.0, max_price=50.0,
        hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.5, breakeven_r=0.8,
        giveback_pct=0.60, max_hold_bars=90, rvol_min=0.3)),
    ("Scalp", V3Config(allow_long=False, min_price=8.0, max_price=50.0,
        hard_stop_r=1.0, trail_r=0.6, tp1_r=0.8, tp2_r=1.5, breakeven_r=0.5,
        giveback_pct=0.45, max_hold_bars=30, rvol_min=0.5)),
]

best_short_cfg_name = None
best_short_trades = []
best_sharpe = -999

for cfg_name, cfg in short_configs:
    all_tr = []
    print(f"\n  Config: {cfg_name}")
    for sym in sub50_syms:
        trig, ctx = sub50_data[sym]
        trades = run_v3_bt(sym, trig, ctx, cfg)
        pnl = sum(x.pnl for x in trades)
        wr = sum(1 for x in trades if x.pnl > 0) / len(trades) if trades else 0
        print(f"    {sym}: {len(trades):3d}tr WR={wr:.0%} PnL=${pnl:+.0f}")
        all_tr.extend(trades)
    s = compute_statistics(all_tr, 25000.0)
    print(f"  => {s['total_trades']}tr WR={s['win_rate']:.0%} PF={s['profit_factor']:.2f} "
          f"PnL=${s['total_pnl']:+.0f} Sharpe={s['sharpe']:.2f}")
    if s['sharpe'] > best_sharpe:
        best_sharpe = s['sharpe']
        best_short_cfg_name = cfg_name
        best_short_trades = list(all_tr)

print(f"\n  Best short config: {best_short_cfg_name}")

# ── Trend V1.3 on Basket A ──
print("\n" + "=" * 60)
print("  TREND V1.3 on Basket A (optimized)")
print("=" * 60)
basket_a = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']
basket_a_data = {s: load_sym(s) for s in basket_a}

cfg_trend = StrategyConfig(
    trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
    hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
)
trend_trades = []
for sym in basket_a:
    trig, ctx = basket_a_data[sym]
    bt = run_backtest(sym, trig, '1m', ctx.get('5m'), ctx.get('15m'),
                      ctx.get('1h'), ctx.get('1D'), cfg_trend)
    print(f"  {sym}: {bt.stats['total_trades']:3d}tr "
          f"WR={bt.stats['win_rate']:.0%} PnL=${bt.stats['total_pnl']:+.0f}")
    trend_trades.extend(bt.trades)

st = compute_statistics(trend_trades, 25000.0)
print(f"  => {st['total_trades']}tr WR={st['win_rate']:.0%} PF={st['profit_factor']:.2f} "
      f"PnL=${st['total_pnl']:+.0f} Sharpe={st['sharpe']:.2f}")

# ── V3 Balanced on Basket A (best config #1) ──
print("\n" + "=" * 60)
print("  V3 BALANCED on Basket A (optimized: S=2.0 T=1.5 TP1=1.5 TP2=3.0)")
print("=" * 60)
cfg_v3a = V3Config(
    min_price=8.0, max_price=500.0,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0,
)
v3a_trades = []
for sym in basket_a:
    trig, ctx = basket_a_data[sym]
    trades = run_v3_bt(sym, trig, ctx, cfg_v3a)
    pnl = sum(x.pnl for x in trades)
    wr = sum(1 for x in trades if x.pnl > 0) / len(trades) if trades else 0
    print(f"  {sym}: {len(trades):3d}tr WR={wr:.0%} PnL=${pnl:+.0f}")
    v3a_trades.extend(trades)

sv3 = compute_statistics(v3a_trades, 25000.0)
print(f"  => {sv3['total_trades']}tr WR={sv3['win_rate']:.0%} PF={sv3['profit_factor']:.2f} "
      f"PnL=${sv3['total_pnl']:+.0f} Sharpe={sv3['sharpe']:.2f}")

# ── COMBINED: Best of each ──
print("\n" + "=" * 60)
print("  COMBINED PORTFOLIO")
print("=" * 60)

# Option 1: Trend-A only (already profitable)
s1 = compute_statistics(trend_trades, 25000.0)
print(f"  [1] Trend-A only:    {s1['total_trades']}tr PnL=${s1['total_pnl']:+.0f} Sharpe={s1['sharpe']:.2f}")

# Option 2: V3-A only
s2 = compute_statistics(v3a_trades, 25000.0)
print(f"  [2] V3-A only:       {s2['total_trades']}tr PnL=${s2['total_pnl']:+.0f} Sharpe={s2['sharpe']:.2f}")

# Option 3: Trend-A + V3-Short-B
combo1 = trend_trades + best_short_trades
s3 = compute_statistics(combo1, 25000.0)
print(f"  [3] Trend-A + Short-B: {s3['total_trades']}tr PnL=${s3['total_pnl']:+.0f} Sharpe={s3['sharpe']:.2f}")

# Option 4: V3-A + V3-Short-B
combo2 = v3a_trades + best_short_trades
s4 = compute_statistics(combo2, 25000.0)
print(f"  [4] V3-A + Short-B:  {s4['total_trades']}tr PnL=${s4['total_pnl']:+.0f} Sharpe={s4['sharpe']:.2f}")

# Option 5: All three
combo3 = trend_trades + v3a_trades + best_short_trades
s5 = compute_statistics(combo3, 25000.0)
print(f"  [5] All combined:    {s5['total_trades']}tr PnL=${s5['total_pnl']:+.0f} Sharpe={s5['sharpe']:.2f}")

# Best single
options = [(1, s1), (2, s2), (3, s3), (4, s4), (5, s5)]
best_opt = max(options, key=lambda x: x[1]['sharpe'])
print(f"\n  BEST OPTION: #{best_opt[0]} | "
      f"PnL=${best_opt[1]['total_pnl']:+.2f} | "
      f"Sharpe={best_opt[1]['sharpe']:.2f} | "
      f"WR={best_opt[1]['win_rate']:.0%} | "
      f"PF={best_opt[1]['profit_factor']:.2f}")
