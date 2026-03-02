"""Simplified optimizer that writes results to opt_output.txt"""
import asyncio, sys, os, traceback
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

import itertools
from backtest.data_fetcher import load_data
from backtest.engine import run_backtest, compute_statistics
from backtest.strategy import StrategyConfig

SYMBOLS = ['AAPL','TSLA','NVDA','AMD','META']
OUT = r'd:\Site\harvester\backtest\opt_output.txt'

try:
    all_data = {}
    for sym in SYMBOLS:
        trigger = load_data(sym, '1m')
        ctx = {}
        for tf in ['5m','15m','1h','1D']:
            try: ctx[tf] = load_data(sym, tf)
            except: ctx[tf] = None
        all_data[sym] = (trigger, ctx)

    grid = list(itertools.product(
        [0.5, 1.0, 1.5, 2.0],
        [0.50, 0.65, 0.80],
        [1.5, 2.0, 3.0],
        [1.0, 1.5, 2.0],
    ))

    results = []
    for i, (trail, give, tp1, stop) in enumerate(grid):
        cfg = StrategyConfig(
            trail_r=trail, giveback_pct=give, tp1_r=tp1,
            tp2_r=max(tp1+1.5, 3.0), hard_stop_r=stop,
            breakeven_r=stop*0.8, rvol_min=1.0, adx_threshold=20.0,
        )
        all_trades = []
        sym_p = {}
        for sym, (trig, ctx) in all_data.items():
            bt = run_backtest(sym, trig, '1m',
                ctx.get('5m'), ctx.get('15m'), ctx.get('1h'), ctx.get('1D'), cfg)
            all_trades.extend(bt.trades)
            sym_p[sym] = bt.stats['total_pnl']
        stats = compute_statistics(all_trades, cfg.account_size)
        results.append((trail, give, tp1, stop, stats, sym_p))

    results.sort(key=lambda r: r[4]['sharpe'], reverse=True)

    lines = ['TOP 30 CONFIGS BY SHARPE:', '']
    lines.append(f'{"Trail":>6} {"Give":>6} {"TP1":>5} {"Stop":>5} {"#Tr":>5} {"WR":>6} {"PF":>6} {"Exp":>7} {"PnL$":>10} {"DD$":>10} {"Sharpe":>7}')
    lines.append('-'*85)
    for trail, give, tp1, stop, s, sp in results[:30]:
        lines.append(f'{trail:>6.1f} {give:>5.0%} {tp1:>5.1f} {stop:>5.1f} {s["total_trades"]:>5} {s["win_rate"]:>5.0%} {s["profit_factor"]:>6.2f} {s["expectancy_r"]:>6.2f}R {s["total_pnl"]:>9.0f}$ {s["max_drawdown"]:>9.0f}$ {s["sharpe"]:>7.2f}')

    lines.append('')
    best = results[0]
    lines.append(f'BEST: Trail={best[0]} Give={best[1]:.0%} TP1={best[2]}R Stop={best[3]}R')
    lines.append(f'  PnL=${best[4]["total_pnl"]:.2f} Sharpe={best[4]["sharpe"]:.2f} WR={best[4]["win_rate"]:.1%} PF={best[4]["profit_factor"]:.2f}')
    lines.append(f'  MaxDD=${best[4]["max_drawdown"]:.2f} Trades={best[4]["total_trades"]}')
    lines.append(f'  Exits: {best[4]["exit_reasons"]}')
    for sym, p in best[5].items():
        lines.append(f'    {sym}: ${p:.2f}')

    with open(OUT, 'w') as f:
        f.write('\n'.join(lines))
    print('DONE - results in', OUT)
except Exception:
    with open(OUT, 'w') as f:
        f.write(traceback.format_exc())
    print('ERROR - see', OUT)
