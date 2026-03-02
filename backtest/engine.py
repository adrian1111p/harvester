"""
engine.py — Backtest Engine V1.1: bar-by-bar simulation with equity tracking.
Runs ConductStrategyV13 / V3 / V4 / V5 signals through the trade simulator,
computes equity curves, drawdown, and performance statistics.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional

import numpy as np
import pandas as pd
from tabulate import tabulate

from backtest.strategy import (
    ConductStrategyV13,
    ExitReason,
    Side,
    Signal,
    StrategyConfig,
    TradeResult,
)


@dataclass
class BacktestResult:
    """Complete result of a backtest run."""
    symbol: str
    trigger_tf: str
    config: StrategyConfig
    trades: list[TradeResult]
    equity_curve: pd.Series
    stats: dict

    def summary_table(self) -> str:
        """Produce a formatted summary table."""
        rows = [
            ["Symbol", self.symbol],
            ["Trigger TF", self.trigger_tf],
            ["Total Trades", self.stats["total_trades"]],
            ["Winners", self.stats["winners"]],
            ["Losers", self.stats["losers"]],
            ["Win Rate", f"{self.stats['win_rate']:.1%}"],
            ["Avg Win ($)", f"${self.stats['avg_win']:.2f}"],
            ["Avg Loss ($)", f"${self.stats['avg_loss']:.2f}"],
            ["Profit Factor", f"{self.stats['profit_factor']:.2f}"],
            ["Expectancy (R)", f"{self.stats['expectancy_r']:.2f}R"],
            ["Total PnL ($)", f"${self.stats['total_pnl']:.2f}"],
            ["Max Drawdown ($)", f"${self.stats['max_drawdown']:.2f}"],
            ["Max Drawdown (%)", f"{self.stats['max_drawdown_pct']:.1%}"],
            ["Sharpe Ratio", f"{self.stats['sharpe']:.2f}"],
            ["Avg Bars Held", f"{self.stats['avg_bars_held']:.0f}"],
            ["Long Trades", self.stats["long_trades"]],
            ["Short Trades", self.stats["short_trades"]],
            ["Long Win Rate", f"{self.stats['long_win_rate']:.1%}"],
            ["Short Win Rate", f"{self.stats['short_win_rate']:.1%}"],
        ]

        # Exit reason breakdown
        for reason, count in sorted(self.stats["exit_reasons"].items()):
            rows.append([f"  Exit: {reason}", count])

        return tabulate(rows, headers=["Metric", "Value"], tablefmt="simple")

    def trades_table(self, n: int = 20) -> str:
        """Show last N trades as a table."""
        if not self.trades:
            return "No trades."
        rows = []
        for t in self.trades[-n:]:
            rows.append([
                t.entry_time.strftime("%Y-%m-%d %H:%M"),
                t.side.value,
                f"${t.entry_price:.2f}",
                f"${t.exit_price:.2f}",
                f"${t.pnl:.2f}",
                f"{t.pnl_r:.2f}R",
                t.exit_reason.value,
                t.bars_held,
            ])
        return tabulate(
            rows,
            headers=["Entry Time", "Side", "Entry$", "Exit$", "PnL$", "PnL(R)", "Exit Reason", "Bars"],
            tablefmt="simple",
        )


def compute_statistics(
    trades: list[TradeResult],
    initial_capital: float,
) -> dict:
    """Compute performance statistics from a list of trades."""
    if not trades:
        return {
            "total_trades": 0, "winners": 0, "losers": 0,
            "win_rate": 0.0, "avg_win": 0.0, "avg_loss": 0.0,
            "profit_factor": 0.0, "expectancy_r": 0.0,
            "total_pnl": 0.0, "max_drawdown": 0.0, "max_drawdown_pct": 0.0,
            "sharpe": 0.0, "avg_bars_held": 0,
            "long_trades": 0, "short_trades": 0,
            "long_win_rate": 0.0, "short_win_rate": 0.0,
            "exit_reasons": {},
        }

    pnls = [t.pnl for t in trades]
    pnl_rs = [t.pnl_r for t in trades]
    winners = [t for t in trades if t.pnl > 0]
    losers = [t for t in trades if t.pnl <= 0]

    total_pnl = sum(pnls)
    gross_profit = sum(t.pnl for t in winners) if winners else 0.0
    gross_loss = abs(sum(t.pnl for t in losers)) if losers else 0.0

    # Equity curve for drawdown
    equity = [initial_capital]
    for p in pnls:
        equity.append(equity[-1] + p)
    equity_arr = np.array(equity)
    peaks = np.maximum.accumulate(equity_arr)
    drawdowns = peaks - equity_arr
    max_dd = float(drawdowns.max())
    max_dd_pct = max_dd / peaks.max() if peaks.max() > 0 else 0.0

    # Sharpe (annualized, assuming ~6.5h trading day, 252 days)
    pnl_arr = np.array(pnls)
    if len(pnl_arr) > 1 and pnl_arr.std() > 0:
        sharpe = (pnl_arr.mean() / pnl_arr.std()) * np.sqrt(252)
    else:
        sharpe = 0.0

    # Long/Short breakdown
    long_trades = [t for t in trades if t.side == Side.LONG]
    short_trades = [t for t in trades if t.side == Side.SHORT]
    long_winners = [t for t in long_trades if t.pnl > 0]
    short_winners = [t for t in short_trades if t.pnl > 0]

    # Exit reason distribution
    exit_reasons: dict[str, int] = {}
    for t in trades:
        key = t.exit_reason.value
        exit_reasons[key] = exit_reasons.get(key, 0) + 1

    return {
        "total_trades": len(trades),
        "winners": len(winners),
        "losers": len(losers),
        "win_rate": len(winners) / len(trades),
        "avg_win": gross_profit / len(winners) if winners else 0.0,
        "avg_loss": -gross_loss / len(losers) if losers else 0.0,
        "profit_factor": gross_profit / gross_loss if gross_loss > 0 else float("inf"),
        "expectancy_r": float(np.mean(pnl_rs)),
        "total_pnl": total_pnl,
        "max_drawdown": max_dd,
        "max_drawdown_pct": max_dd_pct,
        "sharpe": sharpe,
        "avg_bars_held": float(np.mean([t.bars_held for t in trades])),
        "long_trades": len(long_trades),
        "short_trades": len(short_trades),
        "long_win_rate": len(long_winners) / len(long_trades) if long_trades else 0.0,
        "short_win_rate": len(short_winners) / len(short_trades) if short_trades else 0.0,
        "exit_reasons": exit_reasons,
    }


def build_equity_curve(trades: list[TradeResult], initial_capital: float) -> pd.Series:
    """Build time-indexed equity curve."""
    if not trades:
        return pd.Series([initial_capital], name="Equity")
    timestamps = [trades[0].entry_time]
    equity_vals = [initial_capital]
    cumulative = initial_capital
    for t in trades:
        cumulative += t.pnl
        timestamps.append(t.exit_time)
        equity_vals.append(cumulative)
    return pd.Series(equity_vals, index=timestamps, name="Equity")


def run_backtest(
    symbol: str,
    df_trigger: pd.DataFrame,
    trigger_tf: str = "1m",
    df_5m: pd.DataFrame | None = None,
    df_15m: pd.DataFrame | None = None,
    df_1h: pd.DataFrame | None = None,
    df_1d: pd.DataFrame | None = None,
    config: StrategyConfig | None = None,
) -> BacktestResult:
    """Run the full backtest for one symbol."""
    cfg = config or StrategyConfig()
    strategy = ConductStrategyV13(cfg)

    # Generate signals
    signals = strategy.generate_signals(df_trigger, df_5m, df_15m, df_1h, df_1d)

    # Simulate trades (no overlapping — skip signals while in a trade)
    trades: list[TradeResult] = []
    next_allowed_bar = 0

    for sig in signals:
        if sig.bar_index < next_allowed_bar:
            continue
        result = strategy.simulate_trade(sig, df_trigger)
        if result is not None:
            trades.append(result)
            next_allowed_bar = result.exit_bar + 1

    # Compute stats
    stats = compute_statistics(trades, cfg.account_size)
    equity = build_equity_curve(trades, cfg.account_size)

    return BacktestResult(
        symbol=symbol,
        trigger_tf=trigger_tf,
        config=cfg,
        trades=trades,
        equity_curve=equity,
        stats=stats,
    )
