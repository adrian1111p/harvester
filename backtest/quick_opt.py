"""Small focused optimizer - 15 configs, writes results immediately."""
import asyncio, sys, os, time
sys.path.insert(0, r'd:\Site\harvester')
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

from backtest.data_fetcher import load_data
from backtest.engine import run_backtest, compute_statistics
from backtest.strategy import StrategyConfig

SYMBOLS = ['AAPL','TSLA','NVDA','AMD','META']
OUT = r'd:\Site\harvester\backtest\results.txt'

# Load data once
data = {}
for sym in SYMBOLS:
    trigger = load_data(sym, '1m')
    ctx = {}
    for tf in ['5m','15m','1h','1D']:
        try: ctx[tf] = load_data(sym, tf)
        except: ctx[tf] = None
    data[sym] = (trigger, ctx)

# Focused configs to test (based on what we learned)
configs = [
    # Baseline
    {"trail_r": 0.5, "giveback_pct": 0.50, "tp1_r": 1.5, "tp2_r": 3.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    # Wider trail
    {"trail_r": 1.0, "giveback_pct": 0.50, "tp1_r": 1.5, "tp2_r": 3.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    {"trail_r": 1.5, "giveback_pct": 0.50, "tp1_r": 1.5, "tp2_r": 3.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    {"trail_r": 2.0, "giveback_pct": 0.50, "tp1_r": 1.5, "tp2_r": 3.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    # Wider giveback
    {"trail_r": 1.0, "giveback_pct": 0.65, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    {"trail_r": 1.0, "giveback_pct": 0.80, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.0, "breakeven_r": 0.8},
    # Wider stop
    {"trail_r": 1.0, "giveback_pct": 0.65, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.5, "breakeven_r": 1.2},
    {"trail_r": 1.5, "giveback_pct": 0.70, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.5, "breakeven_r": 1.2},
    {"trail_r": 1.5, "giveback_pct": 0.70, "tp1_r": 2.5, "tp2_r": 5.0, "hard_stop_r": 1.5, "breakeven_r": 1.2},
    # Wider stop + wider trail
    {"trail_r": 2.0, "giveback_pct": 0.70, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 2.0, "breakeven_r": 1.5},
    {"trail_r": 2.0, "giveback_pct": 0.80, "tp1_r": 3.0, "tp2_r": 5.0, "hard_stop_r": 2.0, "breakeven_r": 1.5},
    # Very wide
    {"trail_r": 2.5, "giveback_pct": 0.80, "tp1_r": 3.0, "tp2_r": 5.0, "hard_stop_r": 2.0, "breakeven_r": 1.5},
    # Lower RVOL
    {"trail_r": 1.5, "giveback_pct": 0.70, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.5, "breakeven_r": 1.2, "rvol_min": 0.8},
    # Lower ADX
    {"trail_r": 1.5, "giveback_pct": 0.70, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.5, "breakeven_r": 1.2, "adx_threshold": 15.0},
    # Combo
    {"trail_r": 1.5, "giveback_pct": 0.70, "tp1_r": 2.0, "tp2_r": 4.0, "hard_stop_r": 1.5, "breakeven_r": 1.2, "rvol_min": 0.8, "adx_threshold": 15.0},
]

lines = []
header = f'{"#":>2} {"Trail":>5} {"Give":>5} {"TP1":>4} {"TP2":>4} {"Stop":>4} {"#Tr":>4} {"WR":>5} {"PF":>5} {"ExpR":>6} {"PnL$":>8} {"DD$":>8} {"Shp":>6}'
lines.append(header)
lines.append('-' * len(header))

for i, cfg_dict in enumerate(configs):
    rvol = cfg_dict.pop('rvol_min', 1.0)
    adxt = cfg_dict.pop('adx_threshold', 20.0)
    cfg = StrategyConfig(**cfg_dict, rvol_min=rvol, adx_threshold=adxt)

    all_trades = []
    sym_p = {}
    for sym, (trig, ctx) in data.items():
        bt = run_backtest(sym, trig, '1m',
            ctx.get('5m'), ctx.get('15m'), ctx.get('1h'), ctx.get('1D'), cfg)
        all_trades.extend(bt.trades)
        sym_p[sym] = bt.stats['total_pnl']
    s = compute_statistics(all_trades, cfg.account_size)

    line = (f'{i:>2} {cfg.trail_r:>5.1f} {cfg.giveback_pct:>4.0%} '
            f'{cfg.tp1_r:>4.1f} {cfg.tp2_r:>4.1f} {cfg.hard_stop_r:>4.1f} '
            f'{s["total_trades"]:>4} {s["win_rate"]:>4.0%} {s["profit_factor"]:>5.2f} '
            f'{s["expectancy_r"]:>5.2f}R {s["total_pnl"]:>7.0f}$ {s["max_drawdown"]:>7.0f}$ '
            f'{s["sharpe"]:>6.2f}')
    lines.append(line)

    # Write after each iteration
    with open(OUT, 'w') as f:
        f.write('\n'.join(lines))
    print(f'  Config {i}: Sharpe={s["sharpe"]:.2f} PnL=${s["total_pnl"]:.0f}', flush=True)

# Add best config details at end
print('DONE', flush=True)
