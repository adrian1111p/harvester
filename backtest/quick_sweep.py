"""Quick 15-config optimizer with progress output."""
import asyncio, sys, os
sys.path.insert(0, r'd:\Site\harvester')
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

try:
    from backtest.data_fetcher import load_data
    from backtest.engine import run_backtest, compute_statistics
    from backtest.strategy import StrategyConfig

    SYMS = ['AAPL','TSLA','NVDA','AMD','META']
    data = {}
    for sym in SYMS:
        t = load_data(sym, '1m')
        c = {}
        for tf in ['5m','15m','1h','1D']:
            try: c[tf] = load_data(sym, tf)
            except: c[tf] = None
        data[sym] = (t, c)
    print('DATA LOADED', flush=True)

    configs = [
        dict(trail_r=0.5, giveback_pct=0.50, tp1_r=1.5, tp2_r=3.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=1.0, giveback_pct=0.50, tp1_r=1.5, tp2_r=3.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=1.5, giveback_pct=0.50, tp1_r=1.5, tp2_r=3.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=2.0, giveback_pct=0.50, tp1_r=1.5, tp2_r=3.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=1.0, giveback_pct=0.65, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=1.0, giveback_pct=0.80, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.0, breakeven_r=0.8),
        dict(trail_r=1.0, giveback_pct=0.65, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.5, breakeven_r=1.2),
        dict(trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.5, breakeven_r=1.2),
        dict(trail_r=1.5, giveback_pct=0.70, tp1_r=2.5, tp2_r=5.0, hard_stop_r=1.5, breakeven_r=1.2),
        dict(trail_r=2.0, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0, hard_stop_r=2.0, breakeven_r=1.5),
        dict(trail_r=2.0, giveback_pct=0.80, tp1_r=3.0, tp2_r=5.0, hard_stop_r=2.0, breakeven_r=1.5),
        dict(trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.5, breakeven_r=1.2, rvol_min=0.8),
        dict(trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.5, breakeven_r=1.2, adx_threshold=15.0),
        dict(trail_r=1.5, giveback_pct=0.70, tp1_r=2.0, tp2_r=4.0, hard_stop_r=1.5, breakeven_r=1.2, rvol_min=0.8, adx_threshold=15.0),
        dict(trail_r=2.0, giveback_pct=0.70, tp1_r=2.5, tp2_r=5.0, hard_stop_r=2.0, breakeven_r=1.5, rvol_min=0.8, adx_threshold=15.0),
    ]

    results = []
    for i, cd in enumerate(configs):
        cfg = StrategyConfig(**cd)
        trades = []
        sp = {}
        for sym, (trig, ctx) in data.items():
            bt = run_backtest(sym, trig, '1m', ctx.get('5m'), ctx.get('15m'), ctx.get('1h'), ctx.get('1D'), cfg)
            trades.extend(bt.trades)
            sp[sym] = bt.stats['total_pnl']
        s = compute_statistics(trades, cfg.account_size)
        results.append((i, cfg, s, sp))
        tr = cfg.trail_r; gv = cfg.giveback_pct; t1 = cfg.tp1_r; st = cfg.hard_stop_r
        print(f'{i:2d} T={tr:.1f} G={gv:.0%} TP1={t1:.1f} S={st:.1f} | {s["total_trades"]:3d}tr WR={s["win_rate"]:.0%} PF={s["profit_factor"]:.2f} E={s["expectancy_r"]:.2f}R PnL=${s["total_pnl"]:.0f} DD=${s["max_drawdown"]:.0f} Sh={s["sharpe"]:.2f}', flush=True)

    results.sort(key=lambda r: r[2]['sharpe'], reverse=True)
    print('\n=== RANKED BY SHARPE ===', flush=True)
    for rank, (i, cfg, s, sp) in enumerate(results[:5]):
        print(f'#{rank+1} [cfg{i}] T={cfg.trail_r} G={cfg.giveback_pct:.0%} TP1={cfg.tp1_r} S={cfg.hard_stop_r} | {s["total_trades"]}tr WR={s["win_rate"]:.0%} PF={s["profit_factor"]:.2f} PnL=${s["total_pnl"]:.0f} Sharpe={s["sharpe"]:.2f}', flush=True)
        for sym, p in sp.items():
            print(f'     {sym}: ${p:.2f}', flush=True)

    # Also write to file
    with open(r'd:\Site\harvester\backtest\sweep_results.txt', 'w') as f:
        f.write('RANKED BY SHARPE:\n')
        for rank, (i, cfg, s, sp) in enumerate(results):
            f.write(f'#{rank+1} T={cfg.trail_r} G={cfg.giveback_pct:.0%} TP1={cfg.tp1_r} S={cfg.hard_stop_r} | {s["total_trades"]}tr WR={s["win_rate"]:.0%} PF={s["profit_factor"]:.2f} E={s["expectancy_r"]:.2f}R PnL=${s["total_pnl"]:.0f} DD=${s["max_drawdown"]:.0f} Sharpe={s["sharpe"]:.2f}\n')
            for sym, p in sp.items():
                f.write(f'  {sym}: ${p:.2f}\n')
            f.write('\n')

    print('DONE', flush=True)
except Exception as e:
    import traceback
    traceback.print_exc()
    print('FAILED', flush=True)
