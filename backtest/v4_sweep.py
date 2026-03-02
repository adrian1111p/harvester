"""
v4_sweep.py — Sweep V4 Image Pattern Strategy across many configs on Basket A.
Tests multiple pattern combinations, score thresholds, and exit params.
"""
import asyncio, sys, os
sys.path.insert(0, r'd:\Site\harvester')
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

from backtest.data_fetcher import load_data
from backtest.engine import compute_statistics
from backtest.strategy import TradeResult, Side, ExitReason
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
    trades, nb = [], 0
    pattern_counts = {}
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
            p = r.pattern.value
            pattern_counts[p] = pattern_counts.get(p, 0) + 1
            nb = r.exit_bar + 1
    return trades, pattern_counts

# ── Load data ──
basket_a = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']
print("Loading data...", flush=True)
data = {s: load_sym(s) for s in basket_a}
print(f"Loaded {len(basket_a)} symbols\n", flush=True)

# ── Define configs ──
configs = [
    # ── Full pattern suite, varying score threshold & stops ──
    ("A: Full-Score2-Wide",
     V4Config(enhanced_min_score=2, hard_stop_r=2.0, trail_r=1.5,
              tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0, giveback_pct=0.70,
              max_hold_bars=120)),

    ("B: Full-Score3-Wide",
     V4Config(enhanced_min_score=3, hard_stop_r=2.0, trail_r=1.5,
              tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0, giveback_pct=0.70,
              max_hold_bars=120)),

    ("C: Full-Score2-Tight",
     V4Config(enhanced_min_score=2, hard_stop_r=1.5, trail_r=1.0,
              tp1_r=1.0, tp2_r=2.5, breakeven_r=0.8, giveback_pct=0.60,
              max_hold_bars=90)),

    ("D: Full-Score3-Tight",
     V4Config(enhanced_min_score=3, hard_stop_r=1.5, trail_r=1.0,
              tp1_r=1.0, tp2_r=2.5, breakeven_r=0.8, giveback_pct=0.60,
              max_hold_bars=90)),

    ("E: Full-Score2-Runner",
     V4Config(enhanced_min_score=2, hard_stop_r=2.0, trail_r=1.5,
              tp1_r=2.0, tp2_r=4.0, breakeven_r=1.2, giveback_pct=0.75,
              max_hold_bars=180)),

    # ── Pattern subsets ──
    ("F: SetupOnly-S2",
     V4Config(enhanced_min_score=2, enable_breakout=False, enable_breakdown=False,
              enable_exhaustion=False, enable_123_pattern=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("G: 123Only-S2",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_breakout=False, enable_breakdown=False, enable_exhaustion=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("H: BreakOnly-S2",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_exhaustion=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("I: ExhOnly-S2",
     V4Config(enhanced_min_score=2, enable_buy_setup=False, enable_sell_setup=False,
              enable_123_pattern=False, enable_breakout=False, enable_breakdown=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    # ── Cycle focus: buy low sell high (setups + 123 + exhaustion, no breakout) ──
    ("J: CycleBLSH-S2",
     V4Config(enhanced_min_score=2, enable_breakout=False, enable_breakdown=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    ("K: CycleBLSH-S3",
     V4Config(enhanced_min_score=3, enable_breakout=False, enable_breakdown=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    # ── Long-only cycle ──
    ("L: LongCycle-S2",
     V4Config(enhanced_min_score=2, allow_short=False,
              enable_breakout=False, enable_breakdown=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    # ── Relaxed filters for more signals ──
    ("M: Full-Score1-Wide",
     V4Config(enhanced_min_score=1, hard_stop_r=2.0, trail_r=1.5,
              tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0, giveback_pct=0.70,
              max_hold_bars=120, l2_liquidity_min=15.0, rvol_min=0.3)),

    # ── Aggressive runner ──
    ("N: Full-Score2-Aggro",
     V4Config(enhanced_min_score=2, hard_stop_r=2.5, trail_r=2.0,
              tp1_r=2.0, tp2_r=5.0, breakeven_r=1.5, giveback_pct=0.80,
              max_hold_bars=180, l2_liquidity_min=15.0)),

    # ── Tightest quality filter ──
    ("O: Full-Score4-Wide",
     V4Config(enhanced_min_score=4, hard_stop_r=2.0, trail_r=1.5,
              tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0, giveback_pct=0.70,
              max_hold_bars=120)),

    # ── No OFI requirement ──
    ("P: NoOFI-Score2",
     V4Config(enhanced_min_score=2, ofi_confirm=False,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    # ── Wider retracement range ──
    ("Q: WideRetrace-S2",
     V4Config(enhanced_min_score=2, retracement_min=0.20, retracement_max=0.80,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),

    # ── Shorter lookback (faster signals) ──
    ("R: ShortLB-S2",
     V4Config(enhanced_min_score=2, setup_lookback=20, p123_lookback=20,
              breakout_lookback=15, exhaustion_lookback=10,
              hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0,
              breakeven_r=1.0, giveback_pct=0.70, max_hold_bars=120)),
]

# ── Run sweep ──
print(f"{'#':<3} {'Config':<22} {'Tr':>4} {'WR':>5} {'PF':>5} {'PnL':>9} {'Sharpe':>7} {'Patterns'}")
print("-" * 90)

results = []
for idx, (name, cfg) in enumerate(configs, 1):
    all_trades = []
    all_patterns = {}
    for sym in basket_a:
        trig, ctx = data[sym]
        trades, pcounts = run_v4(sym, trig, ctx, cfg)
        all_trades.extend(trades)
        for p, c in pcounts.items():
            all_patterns[p] = all_patterns.get(p, 0) + c

    stats = compute_statistics(all_trades, 25000.0)
    pat_str = " ".join(f"{k[:3]}:{v}" for k, v in sorted(all_patterns.items()))
    print(f"{idx:<3} {name:<22} {stats['total_trades']:>4} "
          f"{stats['win_rate']:>4.0%} {stats['profit_factor']:>5.2f} "
          f"${stats['total_pnl']:>+8.0f} {stats['sharpe']:>7.2f}  {pat_str}",
          flush=True)
    results.append((name, stats, all_patterns, all_trades))

# ── Top 5 by PnL ──
print("\n" + "=" * 75)
print("  TOP 5 by PnL")
print("=" * 75)
by_pnl = sorted(results, key=lambda x: x[1]['total_pnl'], reverse=True)
for name, stats, pats, trades in by_pnl[:5]:
    print(f"  {name}: {stats['total_trades']}tr WR={stats['win_rate']:.0%} "
          f"PF={stats['profit_factor']:.2f} PnL=${stats['total_pnl']:+.0f} "
          f"Sharpe={stats['sharpe']:.2f}")
    # Per-symbol breakdown
    sym_trades = {}
    for t in trades:
        s = basket_a[0]  # Default
        for sym in basket_a:
            trig, _ = data[sym]
            if t.entry_bar < len(trig) and t.entry_time in trig.index:
                s = sym
                break
        sym_trades.setdefault(s, []).append(t)

print("\n" + "=" * 75)
print("  TOP 5 by Sharpe")
print("=" * 75)
by_sharpe = sorted(results, key=lambda x: x[1]['sharpe'], reverse=True)
for name, stats, pats, _ in by_sharpe[:5]:
    print(f"  {name}: {stats['total_trades']}tr WR={stats['win_rate']:.0%} "
          f"PF={stats['profit_factor']:.2f} PnL=${stats['total_pnl']:+.0f} "
          f"Sharpe={stats['sharpe']:.2f}")

# ── Best overall ──
best = by_pnl[0]
print(f"\n>>> BEST CONFIG: {best[0]}")
print(f"    Trades: {best[1]['total_trades']} | WR: {best[1]['win_rate']:.0%} | "
      f"PF: {best[1]['profit_factor']:.2f} | PnL: ${best[1]['total_pnl']:+.0f} | "
      f"Sharpe: {best[1]['sharpe']:.2f}")
print(f"    Max DD: ${best[1]['max_drawdown']:.0f} | "
      f"Avg Win: ${best[1]['avg_win']:.0f} | Avg Loss: ${best[1]['avg_loss']:.0f}")
