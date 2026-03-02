"""
v4_deep_sweep.py — Deep optimization of V4 with tuned pattern logic.
Fixes: relaxed buy setup, improved 123, exhaustion variations, hybrid combos.
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
from backtest.strategy_v4 import V4Config, StrategyV4, PatternType

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

# ── Load data ──
basket_a = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']
print("Loading data...", flush=True)
data = {s: load_sym(s) for s in basket_a}

# ══════════════════════════════════════════════════════════════════════════
# PART 1: Exhaustion-focused sweeps (best pattern by far)
# ══════════════════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  PART 1: EXHAUSTION PATTERN OPTIMIZATION")
print("=" * 80)

exh_configs = [
    ("Exh-S2-LB15-Move3",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
              exhaustion_reversal_bars=3,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S1-LB15-Move3",
     V4Config(enhanced_min_score=1, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
              exhaustion_reversal_bars=3,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S2-LB10-Move2.5",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=10, exhaustion_min_move_atr=2.5,
              exhaustion_reversal_bars=2,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S1-LB10-Move2",
     V4Config(enhanced_min_score=1, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=10, exhaustion_min_move_atr=2.0,
              exhaustion_reversal_bars=2,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S2-LB20-Move4",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=20, exhaustion_min_move_atr=4.0,
              exhaustion_reversal_bars=3,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S1-LB12-Move2-Rev2",
     V4Config(enhanced_min_score=1, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=12, exhaustion_min_move_atr=2.0,
              exhaustion_reversal_bars=2,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S2-LB12-Move2.5-Rev2",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=12, exhaustion_min_move_atr=2.5,
              exhaustion_reversal_bars=2,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Exh-S2-LB15-Move3-Runner",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
              exhaustion_reversal_bars=3,
              hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0,
              breakeven_r=1.5, giveback_pct=0.80, max_hold_bars=180)),
]

print(f"\n{'#':<3} {'Config':<28} {'Tr':>4} {'WR':>5} {'PF':>6} {'PnL':>9} {'Sharpe':>7}")
print("-" * 70)
exh_results = []
for idx, (name, cfg) in enumerate(exh_configs, 1):
    all_trades = []
    per_sym = {}
    for sym in basket_a:
        trig, ctx = data[sym]
        trades, _ = run_v4(sym, trig, ctx, cfg)
        all_trades.extend(trades)
        per_sym[sym] = sum(t.pnl for t in trades)
    stats = compute_statistics(all_trades, 25000.0)
    sym_str = " ".join(f"{s}:${v:+.0f}" for s, v in per_sym.items() if v != 0)
    print(f"{idx:<3} {name:<28} {stats['total_trades']:>4} "
          f"{stats['win_rate']:>4.0%} {stats['profit_factor']:>6.2f} "
          f"${stats['total_pnl']:>+8.0f} {stats['sharpe']:>7.2f}  {sym_str}",
          flush=True)
    exh_results.append((name, stats, all_trades))

# ══════════════════════════════════════════════════════════════════════════
# PART 2: Sell Setup tuning (2nd best pattern)
# ══════════════════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  PART 2: SELL SETUP (Buy-Low-Sell-High Cycle) OPTIMIZATION")
print("=" * 80)

setup_configs = [
    ("Setup-S2-Ret20-80",
     V4Config(enhanced_min_score=2, enable_breakout=False, enable_breakdown=False,
              enable_exhaustion=False, enable_123_pattern=False,
              retracement_min=0.20, retracement_max=0.80, pullback_bars_min=2,
              setup_lookback=25,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Setup-S1-Ret20-80",
     V4Config(enhanced_min_score=1, enable_breakout=False, enable_breakdown=False,
              enable_exhaustion=False, enable_123_pattern=False,
              retracement_min=0.20, retracement_max=0.80, pullback_bars_min=2,
              setup_lookback=25,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Setup-S2-Ret30-70-LB20",
     V4Config(enhanced_min_score=2, enable_breakout=False, enable_breakdown=False,
              enable_exhaustion=False, enable_123_pattern=False,
              retracement_min=0.30, retracement_max=0.70, pullback_bars_min=2,
              setup_lookback=20,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("Setup-S2-NoVolReq",
     V4Config(enhanced_min_score=2, enable_breakout=False, enable_breakdown=False,
              enable_exhaustion=False, enable_123_pattern=False,
              retracement_min=0.20, retracement_max=0.80, pullback_bars_min=2,
              require_volume_spike=False, setup_lookback=25,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),
]

print(f"\n{'#':<3} {'Config':<28} {'Tr':>4} {'WR':>5} {'PF':>6} {'PnL':>9} {'Sharpe':>7}")
print("-" * 70)
setup_results = []
for idx, (name, cfg) in enumerate(setup_configs, 1):
    all_trades = []
    for sym in basket_a:
        trig, ctx = data[sym]
        trades, _ = run_v4(sym, trig, ctx, cfg)
        all_trades.extend(trades)
    stats = compute_statistics(all_trades, 25000.0)
    print(f"{idx:<3} {name:<28} {stats['total_trades']:>4} "
          f"{stats['win_rate']:>4.0%} {stats['profit_factor']:>6.2f} "
          f"${stats['total_pnl']:>+8.0f} {stats['sharpe']:>7.2f}", flush=True)
    setup_results.append((name, stats, all_trades))

# ══════════════════════════════════════════════════════════════════════════
# PART 3: HYBRID — Best V4 patterns + Trend V1.3 + V3 per symbol
# ══════════════════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  PART 3: GRAND HYBRID (V1.3 + V3 + V4-Exhaustion per symbol)")
print("=" * 80)

# V1.3 optimized
cfg_trend = StrategyConfig(
    trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0,
    hard_stop_r=1.5, breakeven_r=1.2, rvol_min=1.3, adx_threshold=20.0,
)
# V3 balanced (best from prior sweep)
cfg_v3 = V3Config(min_price=8.0, max_price=500.0,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0)

# V4 exhaustion (best config: S2, LB15, Move3)
cfg_v4_exh = V4Config(
    enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
    enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
    exhaustion_lookback=15, exhaustion_min_move_atr=3.0,
    exhaustion_reversal_bars=3,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)

# V4 Cycle BLSH (for cycle trades)
cfg_v4_cycle = V4Config(
    enhanced_min_score=3, enable_breakout=False, enable_breakdown=False,
    hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
    breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)

print(f"\n{'Symbol':<6} | {'Strategy':<18} | {'Tr':>4} | {'WR':>5} | {'PnL':>8} | {'Sharpe':>7}")
print("-" * 65)

all_by_strat = {}  # {strategy_name: {symbol: trades}}
strategies = {
    'Trend-V1.3': cfg_trend,
    'V3-Balanced': cfg_v3,
    'V4-Exhaustion': cfg_v4_exh,
    'V4-CycleBLSH': cfg_v4_cycle,
}

for sym in basket_a:
    trig, ctx = data[sym]
    for sname, scfg in strategies.items():
        if sname == 'Trend-V1.3':
            bt = run_backtest(sym, trig, '1m', ctx.get('5m'), ctx.get('15m'),
                              ctx.get('1h'), ctx.get('1D'), scfg)
            trades = bt.trades
            stats = bt.stats
        elif sname.startswith('V3'):
            trades = run_v3(sym, trig, ctx, scfg)
            stats = compute_statistics(trades, 25000.0)
        else:
            trades, _ = run_v4(sym, trig, ctx, scfg)
            stats = compute_statistics(trades, 25000.0)

        all_by_strat.setdefault(sname, {})[sym] = (trades, stats)
        print(f"{sym:<6} | {sname:<18} | {stats['total_trades']:>4} | "
              f"{stats['win_rate']:>4.0%} | ${stats['total_pnl']:>+7.0f} | "
              f"{stats['sharpe']:>7.2f}")
    print("-" * 65)

# Compute totals
print(f"\n{'Strategy':<18} | {'Tr':>4} | {'WR':>5} | {'PF':>5} | {'PnL':>9} | {'Sharpe':>7}")
print("-" * 60)
strat_totals = {}
for sname in strategies:
    all_trades = []
    for sym in basket_a:
        all_trades.extend(all_by_strat[sname][sym][0])
    stats = compute_statistics(all_trades, 25000.0)
    strat_totals[sname] = (stats, all_trades)
    print(f"{sname:<18} | {stats['total_trades']:>4} | {stats['win_rate']:>4.0%} | "
          f"{stats['profit_factor']:>5.2f} | ${stats['total_pnl']:>+8.0f} | "
          f"{stats['sharpe']:>7.2f}")

# ── Per-symbol best selection ──
print("\n" + "=" * 80)
print("  BEST STRATEGY PER SYMBOL (Hybrid Selection)")
print("=" * 80)
hybrid_trades = []
hybrid_picks = {}
for sym in basket_a:
    best_pnl = -999999
    best_name = None
    for sname in strategies:
        pnl = all_by_strat[sname][sym][1]['total_pnl']
        if pnl > best_pnl:
            best_pnl = pnl
            best_name = sname
    hybrid_picks[sym] = best_name
    trades, stats = all_by_strat[best_name][sym]
    hybrid_trades.extend(trades)
    print(f"  {sym}: {best_name:<18} -> {stats['total_trades']}tr "
          f"WR={stats['win_rate']:.0%} PnL=${stats['total_pnl']:+.0f}")

hybrid_stats = compute_statistics(hybrid_trades, 25000.0)
print(f"\n  HYBRID: {hybrid_stats['total_trades']}tr WR={hybrid_stats['win_rate']:.0%} "
      f"PF={hybrid_stats['profit_factor']:.2f} PnL=${hybrid_stats['total_pnl']:+.0f} "
      f"Sharpe={hybrid_stats['sharpe']:.2f} MaxDD=${hybrid_stats['max_drawdown']:.0f}")

# ── Additive combos: run non-overlapping strategies on same symbol ──
print("\n" + "=" * 80)
print("  ADDITIVE COMBOS (V4-Exh adds to V1.3/V3 since different signals)")
print("=" * 80)

combos = [
    ("V1.3 + V4-Exh", ['Trend-V1.3', 'V4-Exhaustion']),
    ("V3 + V4-Exh", ['V3-Balanced', 'V4-Exhaustion']),
    ("V1.3 + V4-Cycle", ['Trend-V1.3', 'V4-CycleBLSH']),
    ("V3 + V4-Cycle", ['V3-Balanced', 'V4-CycleBLSH']),
    ("V1.3 + V3 + V4-Exh", ['Trend-V1.3', 'V3-Balanced', 'V4-Exhaustion']),
    ("HYBRID + V4-Exh", None),  # Special: hybrid picks + exhaustion overlay
]

for combo_name, strat_list in combos:
    combo_trades = []
    if strat_list:
        for sname in strat_list:
            for sym in basket_a:
                combo_trades.extend(all_by_strat[sname][sym][0])
    else:
        # Hybrid + exhaustion overlay
        for sym in basket_a:
            best = hybrid_picks[sym]
            combo_trades.extend(all_by_strat[best][sym][0])
            # Only add exhaustion if not already the pick
            if best != 'V4-Exhaustion':
                combo_trades.extend(all_by_strat['V4-Exhaustion'][sym][0])

    cs = compute_statistics(combo_trades, 25000.0)
    print(f"  {combo_name:<25}: {cs['total_trades']:>4}tr WR={cs['win_rate']:.0%} "
          f"PF={cs['profit_factor']:.2f} PnL=${cs['total_pnl']:+.0f} "
          f"Sharpe={cs['sharpe']:.2f}")

# ══════════════════════════════════════════════════════════════════════════
# FINAL RANKING
# ══════════════════════════════════════════════════════════════════════════
print("\n" + "=" * 80)
print("  FINAL RANKING (all approaches)")
print("=" * 80)

all_options = []
# Individual strategies
for sname, (stats, trades) in strat_totals.items():
    all_options.append((sname, stats))
# Hybrid
all_options.append(("HYBRID", hybrid_stats))
# Combos
for combo_name, strat_list in combos:
    combo_trades = []
    if strat_list:
        for sname in strat_list:
            for sym in basket_a:
                combo_trades.extend(all_by_strat[sname][sym][0])
    else:
        for sym in basket_a:
            best = hybrid_picks[sym]
            combo_trades.extend(all_by_strat[best][sym][0])
            if best != 'V4-Exhaustion':
                combo_trades.extend(all_by_strat['V4-Exhaustion'][sym][0])
    cs = compute_statistics(combo_trades, 25000.0)
    all_options.append((combo_name, cs))

ranked = sorted(all_options, key=lambda x: x[1]['total_pnl'], reverse=True)
for i, (name, stats) in enumerate(ranked, 1):
    pnl_marker = "+" if stats['total_pnl'] > 0 else ""
    print(f"  {i:>2}. {name:<25}: {stats['total_trades']:>4}tr WR={stats['win_rate']:.0%} "
          f"PF={stats['profit_factor']:.2f} PnL=${stats['total_pnl']:>+.0f} "
          f"Sharpe={stats['sharpe']:.2f}")

winner = ranked[0]
print(f"\n  >>> WINNER: {winner[0]}")
print(f"      Trades: {winner[1]['total_trades']} | WR: {winner[1]['win_rate']:.0%} | "
      f"PF: {winner[1]['profit_factor']:.2f}")
print(f"      PnL: ${winner[1]['total_pnl']:+.0f} | Sharpe: {winner[1]['sharpe']:.2f} | "
      f"MaxDD: ${winner[1]['max_drawdown']:.0f}")
