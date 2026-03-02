using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Engine;

/// <summary>
/// Core backtest engine:  statistics, equity curve, and full run loop.
/// Ported from Python engine.py V1.1.
/// </summary>
public static class BacktestEngine
{
    // ── Statistics ────────────────────────────────────────────────────────

    /// <summary>Compute performance statistics from a list of completed trades.</summary>
    public static BacktestStatistics ComputeStatistics(
        IReadOnlyList<BacktestTradeResult> trades,
        double initialCapital)
    {
        if (trades.Count == 0)
        {
            return new BacktestStatistics(
                TotalTrades: 0, Winners: 0, Losers: 0,
                WinRate: 0, AvgWin: 0, AvgLoss: 0,
                ProfitFactor: 0, ExpectancyR: 0,
                TotalPnl: 0, MaxDrawdown: 0, MaxDrawdownPct: 0,
                Sharpe: 0, AvgBarsHeld: 0,
                LongTrades: 0, ShortTrades: 0,
                LongWinRate: 0, ShortWinRate: 0,
                ExitReasons: new Dictionary<ExitReason, int>());
        }

        var winners = trades.Where(t => t.Pnl > 0).ToList();
        var losers = trades.Where(t => t.Pnl <= 0).ToList();

        double grossProfit = winners.Sum(t => t.Pnl);
        double grossLoss = Math.Abs(losers.Sum(t => t.Pnl));
        double totalPnl = trades.Sum(t => t.Pnl);

        // Equity curve for drawdown calculation
        var equity = new double[trades.Count + 1];
        equity[0] = initialCapital;
        for (int i = 0; i < trades.Count; i++)
            equity[i + 1] = equity[i] + trades[i].Pnl;

        double peakEquity = equity[0];
        double maxDd = 0;
        for (int i = 1; i < equity.Length; i++)
        {
            if (equity[i] > peakEquity) peakEquity = equity[i];
            double dd = peakEquity - equity[i];
            if (dd > maxDd) maxDd = dd;
        }

        double peaksMax = equity.Max();
        double maxDdPct = peaksMax > 0 ? maxDd / peaksMax : 0;

        // Sharpe (annualised, sqrt(252))
        double[] pnls = trades.Select(t => t.Pnl).ToArray();
        double sharpe = 0;
        if (pnls.Length > 1)
        {
            double mean = pnls.Average();
            double std = Math.Sqrt(pnls.Select(p => (p - mean) * (p - mean)).Average());
            if (std > 0)
                sharpe = (mean / std) * Math.Sqrt(252);
        }

        // Long / Short breakdown
        var longs = trades.Where(t => t.Side == TradeSide.Long).ToList();
        var shorts = trades.Where(t => t.Side == TradeSide.Short).ToList();
        int longWinners = longs.Count(t => t.Pnl > 0);
        int shortWinners = shorts.Count(t => t.Pnl > 0);

        // Exit reason distribution
        var exitReasons = new Dictionary<ExitReason, int>();
        foreach (var t in trades)
        {
            if (!exitReasons.TryGetValue(t.ExitReason, out int count))
                count = 0;
            exitReasons[t.ExitReason] = count + 1;
        }

        return new BacktestStatistics(
            TotalTrades: trades.Count,
            Winners: winners.Count,
            Losers: losers.Count,
            WinRate: (double)winners.Count / trades.Count,
            AvgWin: winners.Count > 0 ? grossProfit / winners.Count : 0,
            AvgLoss: losers.Count > 0 ? -grossLoss / losers.Count : 0,
            ProfitFactor: grossLoss > 0 ? grossProfit / grossLoss : double.PositiveInfinity,
            ExpectancyR: trades.Average(t => t.PnlR),
            TotalPnl: totalPnl,
            MaxDrawdown: maxDd,
            MaxDrawdownPct: maxDdPct,
            Sharpe: sharpe,
            AvgBarsHeld: trades.Average(t => t.BarsHeld),
            LongTrades: longs.Count,
            ShortTrades: shorts.Count,
            LongWinRate: longs.Count > 0 ? (double)longWinners / longs.Count : 0,
            ShortWinRate: shorts.Count > 0 ? (double)shortWinners / shorts.Count : 0,
            ExitReasons: exitReasons);
    }

    // ── Equity Curve ─────────────────────────────────────────────────────

    /// <summary>Build a time-indexed equity curve from trades.</summary>
    public static IReadOnlyList<(DateTime Time, double Equity)> BuildEquityCurve(
        IReadOnlyList<BacktestTradeResult> trades,
        double initialCapital)
    {
        if (trades.Count == 0)
            return [(DateTime.UtcNow, initialCapital)];

        var curve = new List<(DateTime, double)>(trades.Count + 1)
        {
            (trades[0].EntryTime, initialCapital)
        };

        double cumulative = initialCapital;
        foreach (var t in trades)
        {
            cumulative += t.Pnl;
            curve.Add((t.ExitTime, cumulative));
        }

        return curve;
    }

    // ── Run Backtest ─────────────────────────────────────────────────────

    /// <summary>
    /// Execute a full backtest for one symbol using the given strategy.
    /// </summary>
    /// <param name="symbol">Ticker symbol.</param>
    /// <param name="strategy">Strategy instance implementing IBacktestStrategy.</param>
    /// <param name="triggerBars">Enriched trigger-timeframe bars.</param>
    /// <param name="triggerTf">Timeframe label (e.g. "1m").</param>
    /// <param name="bars5m">Optional enriched 5-min bars.</param>
    /// <param name="bars15m">Optional enriched 15-min bars.</param>
    /// <param name="bars1h">Optional enriched 1-hour bars.</param>
    /// <param name="bars1d">Optional enriched daily bars.</param>
    /// <param name="initialCapital">Starting capital for statistics.</param>
    public static BacktestResult RunBacktest(
        string symbol,
        IBacktestStrategy strategy,
        EnrichedBar[] triggerBars,
        string triggerTf = "1m",
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null,
        double initialCapital = 25_000.0)
    {
        // 1. Generate signals
        var signals = strategy.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);

        // 2. Simulate trades (no overlapping — skip signals while in a trade)
        var trades = new List<BacktestTradeResult>();
        int nextAllowedBar = 0;

        foreach (var sig in signals)
        {
            if (sig.BarIndex < nextAllowedBar)
                continue;

            var result = strategy.SimulateTrade(sig, triggerBars);
            if (result != null)
            {
                trades.Add(result);
                nextAllowedBar = result.ExitBar + 1;
            }
        }

        // 3. Compute stats & equity curve
        var stats = ComputeStatistics(trades, initialCapital);
        var equityCurve = BuildEquityCurve(trades, initialCapital);

        return new BacktestResult(
            Symbol: symbol,
            TriggerTf: triggerTf,
            Trades: trades,
            EquityCurve: equityCurve,
            Stats: stats);
    }
}
