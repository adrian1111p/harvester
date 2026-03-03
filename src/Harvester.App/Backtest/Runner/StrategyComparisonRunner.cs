using Harvester.App.Backtest.DataFetcher;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Backtest.Runner;

public sealed record StrategyComparisonRow(
    string Strategy,
    string Variant,
    int Symbols,
    int Trades,
    double WinRate,
    double ProfitFactor,
    double Sharpe,
    double TotalPnl,
    double MaxDrawdown,
    bool MeetsMinTrades);

public static class StrategyComparisonRunner
{
    public static List<StrategyComparisonRow> RunAll(
        string[]? symbols = null,
        int minTrades = 50,
        Action<string>? log = null)
    {
        log ??= Console.WriteLine;

        var symbolUniverse = symbols?.Length > 0
            ? symbols
            : CsvBarStorage.ListSymbols();

        symbolUniverse = symbolUniverse
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        if (symbolUniverse.Length < 5)
        {
            throw new InvalidOperationException(
                $"Need at least 5 symbols for comparison; found {symbolUniverse.Length}.");
        }

        log($"Using {symbolUniverse.Length} scanner symbols: {string.Join(", ", symbolUniverse)}");
        var allData = ParameterSweep.LoadAllData(symbolUniverse, _ => { });

        var rows = new List<StrategyComparisonRow>();

        var plans = BuildPlans();
        foreach (var plan in plans)
        {
            var best = EvaluatePlan(plan, allData, minTrades);
            rows.Add(best);
            log($"{best.Strategy,-10} [{best.Variant}] -> {best.Trades} trades | WR {best.WinRate:P1} | PnL ${best.TotalPnl:F2} | PF {best.ProfitFactor:F2} | Sharpe {best.Sharpe:F2}");
        }

        PrintTable(rows, minTrades, log);
        return rows;
    }

    private static StrategyComparisonRow EvaluatePlan(
        StrategyPlan plan,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        int minTrades)
    {
        StrategyComparisonRow? best = null;

        foreach (var variant in plan.Variants)
        {
            var strategy = variant.Factory();
            var allTrades = new List<BacktestTradeResult>();

            foreach (var (sym, data) in allData)
            {
                var bt = BacktestEngine.RunBacktest(
                    symbol: sym,
                    strategy: strategy,
                    triggerBars: data.Trigger,
                    triggerTf: "1m",
                    bars5m: data.Ctx5m,
                    bars15m: data.Ctx15m,
                    bars1h: data.Ctx1h,
                    bars1d: data.Ctx1d,
                    initialCapital: plan.InitialCapital);

                allTrades.AddRange(bt.Trades);
            }

            var stats = BacktestEngine.ComputeStatistics(allTrades, plan.InitialCapital);
            var row = new StrategyComparisonRow(
                Strategy: plan.Name,
                Variant: variant.Name,
                Symbols: allData.Count,
                Trades: stats.TotalTrades,
                WinRate: stats.WinRate,
                ProfitFactor: stats.ProfitFactor,
                Sharpe: stats.Sharpe,
                TotalPnl: stats.TotalPnl,
                MaxDrawdown: stats.MaxDrawdown,
                MeetsMinTrades: stats.TotalTrades >= minTrades);

            if (row.MeetsMinTrades)
                return row;

            if (best == null || row.Trades > best.Trades)
                best = row;
        }

        return best!;
    }

    private static void PrintTable(List<StrategyComparisonRow> rows, int minTrades, Action<string> log)
    {
        log("\n=== STRATEGY COMPARISON TABLE ===");
        log($"Min trades target per strategy: {minTrades}");
        log("| Strategy | Variant | Symbols | Trades | >=50 | WinRate | PF | Sharpe | TotalPnL$ | MaxDD$ |");
        log("|---|---|---:|---:|:---:|---:|---:|---:|---:|---:|");

        foreach (var r in rows)
        {
            var meets = r.MeetsMinTrades ? "YES" : "NO";
            var pf = double.IsInfinity(r.ProfitFactor) ? "INF" : r.ProfitFactor.ToString("F2");
            log($"| {r.Strategy} | {r.Variant} | {r.Symbols} | {r.Trades} | {meets} | {r.WinRate:P1} | {pf} | {r.Sharpe:F2} | {r.TotalPnl:F2} | {r.MaxDrawdown:F2} |");
        }
    }

    private sealed record StrategyPlan(
        string Name,
        double InitialCapital,
        IReadOnlyList<StrategyVariant> Variants);

    private sealed record StrategyVariant(string Name, Func<IBacktestStrategy> Factory);

    private static List<StrategyPlan> BuildPlans()
    {
        return
        [
            new StrategyPlan(
                "V1-First",
                25_000.0,
                [new StrategyVariant("default", () => new StrategyV1())]),

            new StrategyPlan(
                "V2-Conduct",
                25_000.0,
                [new StrategyVariant("optimized", () => new StrategyV2())]),

            new StrategyPlan(
                "V3",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV3()),
                    new StrategyVariant("relaxed", () => new StrategyV3(new V3Config
                    {
                        MinPrice = 0,
                        MaxPrice = 2000,
                        L2LiquidityMin = 0,
                        SpreadZMax = 10,
                        RvolMin = 0,
                        RequireVolumeConfirm = false,
                        VwapStretchAtr = 1.0,
                        BbEntryPctbLow = 0.20,
                        BbEntryPctbHigh = 0.80,
                    }))
                ]),

            new StrategyPlan(
                "V4",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV4()),
                    new StrategyVariant("relaxed", () => new StrategyV4(new V4Config
                    {
                        RequireVolumeSpike = false,
                        VolumeSpikeMultiplier = 1.0,
                        BreakoutVolumeMultiplier = 1.0,
                        L2LiquidityMin = 0,
                        SpreadZMax = 10,
                        RvolMin = 0,
                        EnhancedMinScore = 1,
                        MinRrRatio = 1.0,
                    }))
                ]),

            new StrategyPlan(
                "V5",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV5()),
                    new StrategyVariant("relaxed", () => new StrategyV5(new V5Config
                    {
                        RvolMin = 0,
                        RequireCandleConfirm = false,
                        MaxMaDistAtr = 1.0,
                        ExhaustionDistAtr = 1.2,
                    }))
                ]),

            new StrategyPlan(
                "V6",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV6()),
                    new StrategyVariant("relaxed", () => new StrategyV6(new V6Config
                    {
                        OrMinutes = 5,
                        MinRangeAtr = 0.05,
                        MaxRangeAtr = 20.0,
                        MaxMaDistAtr = 2.0,
                        RequireVwapAlign = false,
                        RvolMin = 0,
                        EntryWindows = [(570, 960)],
                        RequireCrossFromInside = false,
                        MaxEntriesPerDirectionPerDay = 5,
                    }))
                ]),

            new StrategyPlan(
                "V7",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV7()),
                    new StrategyVariant("relaxed", () => new StrategyV7(new V7Config
                    {
                        SkipFirstNMinutes = 0,
                        PullbackAtrProximity = 0.40,
                        EmaMinSlopeAtr = 0.0,
                        RequireVolumeExpansion = false,
                        MaxMaDistAtr = 2.0,
                    }))
                ]),
        ];
    }
}
