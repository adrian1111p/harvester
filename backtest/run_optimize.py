"""Standalone optimizer that writes results to backtest/opt_results.txt"""
import asyncio, sys
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

import itertools
from backtest.data_fetcher import load_data
from backtest.engine import run_backtest, compute_statistics
from backtest.strategy import StrategyConfig

SYMBOLS = ['AAPL','TSLA','NVDA','AMD','META']
OUT_FILE = 'backtest/opt_results.txt'

def main():
    # Load data
    all_data = {}
    for sym in SYMBOLS:
        trigger = load_data(sym, '1m')
        ctx = {}
        for tf in ['5m','15m','1h','1D']:
            try: ctx[tf] = load_data(sym, tf)
            except: ctx[tf] = None
        all_data[sym] = (trigger, ctx)
    
    print(f'Loaded {len(all_data)} symbols', flush=True)

    # Build config grid
    grid = list(itertools.product(
        [0.5, 1.0, 1.5, 2.0],     # trail_r
        [0.50, 0.65, 0.80],        # giveback
        [1.5, 2.0, 3.0],           # tp1
        [1.0, 1.5, 2.0],           # hard_stop
    ))
    print(f'Testing {len(grid)} combos...', flush=True)

    results = []
    for i, (trail, give, tp1, stop) in enumerate(grid):
        cfg = StrategyConfig(
            trail_r=trail, giveback_pct=give, tp1_r=tp1,
            tp2_r=max(tp1 + 1.5, 3.0),
            hard_stop_r=stop, breakeven_r=stop * 0.8,
            rvol_min=1.0, adx_threshold=20.0,
        )
        all_trades = []
        sym_pnls = {}
        for sym, (trig, ctx) in all_data.items():
            bt = run_backtest(sym, trig, '1m',
                              ctx.get('5m'), ctx.get('15m'),
                              ctx.get('1h'), ctx.get('1D'), cfg)
            all_trades.extend(bt.trades)
            sym_pnls[sym] = bt.stats['total_pnl']

        stats = compute_statistics(all_trades, cfg.account_size)
        results.append({
            'i': i, 'trail': trail, 'give': give, 'tp1': tp1,
            'tp2': max(tp1 + 1.5, 3.0), 'stop': stop,
            'trades': stats['total_trades'],
            'wr': stats['win_rate'],
            'pf': stats['profit_factor'],
            'exp': stats['expectancy_r'],
            'pnl': stats['total_pnl'],
            'dd': stats['max_drawdown'],
            'sharpe': stats['sharpe'],
            'sym': sym_pnls,
            'exits': stats['exit_reasons'],
        })
        if (i + 1) % 10 == 0:
            print(f'  {i+1}/{len(grid)}', flush=True)

    # Sort by Sharpe
    results.sort(key=lambda r: r['sharpe'], reverse=True)

    # Write results to file
    lines = []
    lines.append('V1.3 CONDUCT STRATEGY PARAMETER OPTIMIZATION RESULTS')
    lines.append('=' * 80)
    lines.append('')
    lines.append(f'{"#":>3} {"Trail":>6} {"Give":>6} {"TP1":>5} {"TP2":>5} {"Stop":>5} {"Trades":>6} {"WR":>6} {"PF":>6} {"Exp(R)":>7} {"PnL$":>10} {"MaxDD$":>10} {"Sharpe":>7}')
    lines.append('-' * 90)

    for r in results[:30]:
        lines.append(
            f'{r["i"]:>3} {r["trail"]:>6.1f} {r["give"]:>5.0%} '
            f'{r["tp1"]:>5.1f} {r["tp2"]:>5.1f} {r["stop"]:>5.1f} '
            f'{r["trades"]:>6} {r["wr"]:>5.0%} {r["pf"]:>6.2f} '
            f'{r["exp"]:>6.2f}R {r["pnl"]:>9.0f}$ {r["dd"]:>9.0f}$ '
            f'{r["sharpe"]:>7.2f}'
        )

    lines.append('')
    lines.append('BEST CONFIG DETAILS:')
    lines.append('=' * 80)
    best = results[0]
    lines.append(f'  Trail R:       {best["trail"]}')
    lines.append(f'  Giveback:      {best["give"]:.0%}')
    lines.append(f'  TP1:           {best["tp1"]}R')
    lines.append(f'  TP2:           {best["tp2"]}R')
    lines.append(f'  Hard Stop:     {best["stop"]}R')
    lines.append(f'  Break-even at: {best["stop"] * 0.8}R')
    lines.append(f'  Total PnL:     ${best["pnl"]:.2f}')
    lines.append(f'  Sharpe:        {best["sharpe"]:.2f}')
    lines.append(f'  Win Rate:      {best["wr"]:.1%}')
    lines.append(f'  Profit Factor: {best["pf"]:.2f}')
    lines.append(f'  Expectancy:    {best["exp"]:.2f}R')
    lines.append(f'  Max Drawdown:  ${best["dd"]:.2f}')
    lines.append(f'  Trades:        {best["trades"]}')
    lines.append(f'  Exit reasons:  {best["exits"]}')
    lines.append(f'')
    lines.append(f'  Per-symbol PnL:')
    for sym, pnl in best['sym'].items():
        tag = '+' if pnl > 0 else ''
        lines.append(f'    {sym}: {tag}${pnl:.2f}')

    # Worst config for comparison
    lines.append('')
    lines.append('WORST CONFIG:')
    worst = results[-1]
    lines.append(f'  Trail={worst["trail"]} Give={worst["give"]:.0%} TP1={worst["tp1"]}R Stop={worst["stop"]}R → PnL=${worst["pnl"]:.2f} Sharpe={worst["sharpe"]:.2f}')

    output = '\n'.join(lines)
    with open(OUT_FILE, 'w') as f:
        f.write(output)

    print(f'\nResults written to {OUT_FILE}', flush=True)
    print(f'Best Sharpe: {best["sharpe"]:.2f} | PnL: ${best["pnl"]:.0f} | WR: {best["wr"]:.0%}', flush=True)

if __name__ == '__main__':
    main()
