"""Quick V3 parameter sweep on Basket A to find profitable config."""
import asyncio, sys, os
sys.path.insert(0, r'd:\Site\harvester')
os.chdir(r'd:\Site\harvester')
if sys.version_info >= (3, 14):
    try: asyncio.get_running_loop()
    except RuntimeError: asyncio.set_event_loop(asyncio.new_event_loop())

try:
    from backtest.data_fetcher import load_data
    from backtest.engine import compute_statistics, build_equity_curve, BacktestResult, run_backtest
    from backtest.strategy import StrategyConfig, TradeResult, Side, ExitReason
    from backtest.strategy_v3 import V3Config, StrategyV3
    from backtest.indicators import enrich_with_indicators

    SYMS = ['AAPL', 'TSLA', 'NVDA', 'AMD', 'META']
    CTFS = ['5m', '15m', '1h', '1D']

    # Load all data upfront
    data = {}
    for sym in SYMS:
        t = load_data(sym, '1m')
        c = {}
        for tf in CTFS:
            try: c[tf] = load_data(sym, tf)
            except: c[tf] = None
        data[sym] = (t, c)
    print('DATA LOADED', flush=True)

    def run_v3_bt(sym, trig, ctx, cfg):
        strategy = StrategyV3(cfg)
        sigs = strategy.generate_signals(trig, ctx.get('5m'), ctx.get('15m'),
                                          ctx.get('1h'), ctx.get('1D'))
        trades = []
        nb = 0
        for sig in sigs:
            if sig.bar_index < nb: continue
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

    configs = [
        # Varying stop/target combos
        dict(hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.5, breakeven_r=0.8),  # baseline
        dict(hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0),  # wider
        dict(hard_stop_r=1.0, trail_r=0.8, tp1_r=0.8, tp2_r=2.0, breakeven_r=0.6),  # tighter
        dict(hard_stop_r=1.5, trail_r=1.2, tp1_r=1.5, tp2_r=3.0, breakeven_r=1.0, giveback_pct=0.70),
        dict(hard_stop_r=2.0, trail_r=1.5, tp1_r=2.0, tp2_r=4.0, breakeven_r=1.2, giveback_pct=0.70),
        # VWAP stretch variations
        dict(vwap_stretch_atr=1.0, hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.0),
        dict(vwap_stretch_atr=2.0, hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0),
        # BB entry variations
        dict(bb_entry_pctb_low=0.02, bb_entry_pctb_high=0.98, hard_stop_r=1.5, trail_r=1.2, tp1_r=1.0, tp2_r=2.5),
        dict(bb_entry_pctb_low=0.10, bb_entry_pctb_high=0.90, hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.0),
        # Short only (riding downtrend)
        dict(allow_long=False, hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.5),
        # Long only
        dict(allow_short=False, hard_stop_r=2.0, trail_r=1.5, tp1_r=1.5, tp2_r=3.0),
        # Aggressive entry + wide target
        dict(vwap_stretch_atr=1.0, bb_entry_pctb_low=0.10, bb_entry_pctb_high=0.90,
             hard_stop_r=2.0, trail_r=1.5, tp1_r=2.0, tp2_r=4.0, breakeven_r=1.2,
             giveback_pct=0.70, max_hold_bars=120, rvol_min=0.3),
        # L2-strict: high liquidity requirement
        dict(l2_liquidity_min=40.0, hard_stop_r=1.5, trail_r=1.0, tp1_r=1.0, tp2_r=2.5),
        # Squeeze-only
        dict(vwap_enabled=False, bb_enabled=False, hard_stop_r=1.5, trail_r=1.0, tp1_r=1.5, tp2_r=3.0),
        # Gentle stops + long hold
        dict(hard_stop_r=2.5, trail_r=2.0, tp1_r=2.0, tp2_r=5.0, breakeven_r=1.5,
             giveback_pct=0.75, max_hold_bars=180),
    ]

    results = []
    for i, cd in enumerate(configs):
        cfg = V3Config(min_price=8.0, max_price=500.0, **cd)
        trades = []
        sp = {}
        for sym, (trig, ctx) in data.items():
            bt_trades = run_v3_bt(sym, trig, ctx, cfg)
            trades.extend(bt_trades)
            sp[sym] = sum(t.pnl for t in bt_trades)
        s = compute_statistics(trades, 25000.0)
        results.append((i, cfg, s, sp))
        print(f'{i:2d} S={cfg.hard_stop_r:.1f} T={cfg.trail_r:.1f} '
              f'TP1={cfg.tp1_r:.1f} TP2={cfg.tp2_r:.1f} | '
              f'{s["total_trades"]:3d}tr WR={s["win_rate"]:.0%} '
              f'PF={s["profit_factor"]:.2f} E={s["expectancy_r"]:.2f}R '
              f'PnL=${s["total_pnl"]:.0f} Sh={s["sharpe"]:.2f}', flush=True)

    results.sort(key=lambda r: r[2]['sharpe'], reverse=True)
    print('\n=== TOP 5 BY SHARPE ===', flush=True)
    for rank, (i, cfg, s, sp) in enumerate(results[:5]):
        print(f'#{rank+1} [cfg{i}] S={cfg.hard_stop_r} T={cfg.trail_r} '
              f'TP1={cfg.tp1_r} TP2={cfg.tp2_r} BE={cfg.breakeven_r} '
              f'VWAP={cfg.vwap_stretch_atr} | '
              f'{s["total_trades"]}tr WR={s["win_rate"]:.0%} '
              f'PF={s["profit_factor"]:.2f} PnL=${s["total_pnl"]:.0f} '
              f'Sharpe={s["sharpe"]:.2f}', flush=True)
        for sym, p in sp.items():
            print(f'     {sym}: ${p:.2f}', flush=True)

    with open(r'd:\Site\harvester\backtest\v3_sweep_results.txt', 'w') as f:
        f.write('V3 SWEEP ON BASKET A\n\n')
        for rank, (i, cfg, s, sp) in enumerate(results):
            f.write(f'#{rank+1} [cfg{i}] S={cfg.hard_stop_r} T={cfg.trail_r} '
                    f'TP1={cfg.tp1_r} TP2={cfg.tp2_r} | '
                    f'{s["total_trades"]}tr WR={s["win_rate"]:.0%} '
                    f'PF={s["profit_factor"]:.2f} E={s["expectancy_r"]:.2f}R '
                    f'PnL=${s["total_pnl"]:.0f} Sh={s["sharpe"]:.2f}\n')
            for sym, p in sp.items():
                f.write(f'  {sym}: ${p:.2f}\n')
            f.write('\n')

    print('DONE', flush=True)
except Exception as e:
    import traceback
    traceback.print_exc()
    print('FAILED', flush=True)
