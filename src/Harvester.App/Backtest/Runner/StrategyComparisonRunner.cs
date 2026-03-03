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
        StrategyComparisonRow? bestEligible = null;
        StrategyComparisonRow? bestAny = null;

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

            if (bestAny == null || IsBetter(row, bestAny, preferTradeFloor: false))
                bestAny = row;

            if (row.MeetsMinTrades)
            {
                if (bestEligible == null || IsBetter(row, bestEligible, preferTradeFloor: true))
                    bestEligible = row;
            }
        }

        return bestEligible ?? bestAny!;
    }

    private static bool IsBetter(StrategyComparisonRow left, StrategyComparisonRow right, bool preferTradeFloor)
    {
        if (preferTradeFloor && left.MeetsMinTrades != right.MeetsMinTrades)
            return left.MeetsMinTrades;

        int pnlCmp = left.TotalPnl.CompareTo(right.TotalPnl);
        if (pnlCmp != 0) return pnlCmp > 0;

        int ddCmp = right.MaxDrawdown.CompareTo(left.MaxDrawdown);
        if (ddCmp != 0) return ddCmp > 0;

        int sharpeCmp = left.Sharpe.CompareTo(right.Sharpe);
        if (sharpeCmp != 0) return sharpeCmp > 0;

        int pfCmp = left.ProfitFactor.CompareTo(right.ProfitFactor);
        if (pfCmp != 0) return pfCmp > 0;

        return left.Trades > right.Trades;
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
                    new StrategyVariant("default-short-only", () => new StrategyV3(new V3Config
                    {
                        AllowLong = false,
                        AllowShort = true,
                    })),
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
                    })),
                    new StrategyVariant("relaxed-short-only", () => new StrategyV3(new V3Config
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
                        AllowLong = false,
                        AllowShort = true,
                    }))
                ]),

            new StrategyPlan(
                "V4",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV4()),
                    new StrategyVariant("default-short-only", () => new StrategyV4(new V4Config
                    {
                        AllowLong = false,
                        AllowShort = true,
                    })),
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
                    })),
                    new StrategyVariant("relaxed-short-only", () => new StrategyV4(new V4Config
                    {
                        RequireVolumeSpike = false,
                        VolumeSpikeMultiplier = 1.0,
                        BreakoutVolumeMultiplier = 1.0,
                        L2LiquidityMin = 0,
                        SpreadZMax = 10,
                        RvolMin = 0,
                        EnhancedMinScore = 1,
                        MinRrRatio = 1.0,
                        AllowLong = false,
                        AllowShort = true,
                    }))
                ]),

            new StrategyPlan(
                "V5",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV5()),
                    new StrategyVariant("default-short-only", () => new StrategyV5(new V5Config
                    {
                        AllowLong = false,
                        AllowShort = true,
                    })),
                    new StrategyVariant("relaxed", () => new StrategyV5(new V5Config
                    {
                        RvolMin = 0,
                        RequireCandleConfirm = false,
                        MaxMaDistAtr = 1.0,
                        ExhaustionDistAtr = 1.2,
                    })),
                    new StrategyVariant("relaxed-short-only", () => new StrategyV5(new V5Config
                    {
                        RvolMin = 0,
                        RequireCandleConfirm = false,
                        MaxMaDistAtr = 1.0,
                        ExhaustionDistAtr = 1.2,
                        AllowLong = false,
                        AllowShort = true,
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
                    })),
                    new StrategyVariant("relaxed-short-only", () => new StrategyV6(new V6Config
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
                    new StrategyVariant("default-short-bias", () => new StrategyV7(new V7Config
                    {
                        RsiMaxLong = 55.0,
                        RsiMinShort = 30.0,
                    })),
                    new StrategyVariant("relaxed", () => new StrategyV7(new V7Config
                    {
                        SkipFirstNMinutes = 0,
                        PullbackAtrProximity = 0.40,
                        EmaMinSlopeAtr = 0.0,
                        RequireVolumeExpansion = false,
                        MaxMaDistAtr = 2.0,
                    })),
                    new StrategyVariant("relaxed-short-bias", () => new StrategyV7(new V7Config
                    {
                        SkipFirstNMinutes = 0,
                        PullbackAtrProximity = 0.40,
                        EmaMinSlopeAtr = 0.0,
                        RequireVolumeExpansion = false,
                        MaxMaDistAtr = 2.0,
                        RsiMaxLong = 55.0,
                        RsiMinShort = 30.0,
                    }))
                ]),

            new StrategyPlan(
                "V8",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV8()),
                    new StrategyVariant("default-no-usd-cap", () => new StrategyV8(new V8Config
                    {
                        UseFixedGivebackUsdCap = false,
                    })),
                    new StrategyVariant("usd-cap-20", () => new StrategyV8(new V8Config
                    {
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 20.0,
                    })),
                    new StrategyVariant("tight-risk", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.25,
                        RejectCloseBelowEma9Atr = 0.10,
                        HardStopR = 0.9,
                        TrailR = 0.30,
                        GivebackPct = 0.30,
                        Tp1R = 0.7,
                        Tp2R = 1.6,
                        MaxHoldBars = 35,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("balanced", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.35,
                        RejectCloseBelowEma9Atr = 0.06,
                        HardStopR = 1.1,
                        TrailR = 0.40,
                        GivebackPct = 0.40,
                        Tp1R = 0.9,
                        Tp2R = 2.0,
                        MaxHoldBars = 55,
                        RvolMin = 0.6,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("high-winrate", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.40,
                        RejectCloseBelowEma9Atr = 0.04,
                        HardStopR = 1.2,
                        TrailR = 0.25,
                        GivebackPct = 0.25,
                        Tp1R = 0.6,
                        Tp2R = 1.3,
                        MaxHoldBars = 30,
                        RvolMin = 0.5,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("profit-seeker", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.28,
                        RejectCloseBelowEma9Atr = 0.10,
                        RsiMinShort = 45.0,
                        RsiMaxShort = 65.0,
                        RvolMin = 1.0,
                        L2LiquidityMin = 30.0,
                        SpreadZMax = 2.0,
                        HardStopR = 0.9,
                        BreakevenR = 0.6,
                        TrailR = 0.45,
                        GivebackPct = 0.45,
                        Tp1R = 1.0,
                        Tp2R = 2.4,
                        MaxHoldBars = 65,
                        MicroTrailCents = 3.0,
                        MicroTrailActivateCents = 6.0,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("trend-selective", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.22,
                        RejectCloseBelowEma9Atr = 0.12,
                        RsiMinShort = 48.0,
                        RsiMaxShort = 62.0,
                        RvolMin = 1.1,
                        L2LiquidityMin = 35.0,
                        SpreadZMax = 1.8,
                        HardStopR = 0.8,
                        BreakevenR = 0.5,
                        TrailR = 0.50,
                        GivebackPct = 0.50,
                        Tp1R = 1.1,
                        Tp2R = 2.8,
                        MaxHoldBars = 75,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("capital-preserve", () => new StrategyV8(new V8Config
                    {
                        RiskPerTradeDollars = 20.0,
                        PullbackToEma9Atr = 0.26,
                        RejectCloseBelowEma9Atr = 0.10,
                        RsiMinShort = 46.0,
                        RsiMaxShort = 66.0,
                        RvolMin = 0.9,
                        L2LiquidityMin = 25.0,
                        SpreadZMax = 2.2,
                        HardStopR = 0.85,
                        BreakevenR = 0.45,
                        TrailR = 0.30,
                        GivebackPct = 0.30,
                        Tp1R = 0.70,
                        Tp2R = 1.50,
                        MaxHoldBars = 40,
                        MicroTrailCents = 2.0,
                        MicroTrailActivateCents = 3.5,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("low-risk-usd30", () => new StrategyV8(new V8Config
                    {
                        RiskPerTradeDollars = 10.0,
                        PullbackToEma9Atr = 0.40,
                        RejectCloseBelowEma9Atr = 0.04,
                        RsiMinShort = 42.0,
                        RsiMaxShort = 70.0,
                        MinAdx = 0.0,
                        RequireMtfBearAlignment = false,
                        RvolMin = 0.5,
                        L2LiquidityMin = 20.0,
                        SpreadZMax = 2.5,
                        HardStopR = 1.2,
                        BreakevenR = 0.5,
                        TrailR = 0.25,
                        GivebackPct = 0.25,
                        Tp1R = 0.6,
                        Tp2R = 1.3,
                        MaxHoldBars = 30,
                        MicroTrailCents = 2.0,
                        MicroTrailActivateCents = 4.0,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("defensive-usd30", () => new StrategyV8(new V8Config
                    {
                        RiskPerTradeDollars = 15.0,
                        PullbackToEma9Atr = 0.30,
                        RejectCloseBelowEma9Atr = 0.08,
                        RsiMinShort = 45.0,
                        RsiMaxShort = 65.0,
                        MinAdx = 22.0,
                        RequireMtfBearAlignment = true,
                        RvolMin = 0.9,
                        L2LiquidityMin = 25.0,
                        SpreadZMax = 2.0,
                        HardStopR = 0.9,
                        BreakevenR = 0.45,
                        TrailR = 0.30,
                        GivebackPct = 0.30,
                        Tp1R = 0.70,
                        Tp2R = 1.40,
                        MaxHoldBars = 35,
                        MicroTrailCents = 2.0,
                        MicroTrailActivateCents = 3.5,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("scalper", () => new StrategyV8(new V8Config
                    {
                        PullbackToEma9Atr = 0.45,
                        RejectCloseBelowEma9Atr = 0.03,
                        RsiMinShort = 40.0,
                        RsiMaxShort = 72.0,
                        RvolMin = 0.5,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 3.0,
                        HardStopR = 0.7,
                        BreakevenR = 0.35,
                        TrailR = 0.20,
                        GivebackPct = 0.20,
                        Tp1R = 0.50,
                        Tp2R = 1.10,
                        MaxHoldBars = 25,
                        MicroTrailCents = 1.5,
                        MicroTrailActivateCents = 2.5,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    }))
                ]),

            new StrategyPlan(
                "V9",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV9()),
                    new StrategyVariant("short-bias", () => new StrategyV9(new V9Config
                    {
                        AllowLong = false,
                        AllowShort = true,
                        OfiSignalThreshold = 0.04,
                        RvolMin = 0.7,
                        L2LiquidityMin = 18.0,
                    })),
                    new StrategyVariant("balanced", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 30.0,
                        AllowLong = true,
                        AllowShort = true,
                        RvolMin = 0.6,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 2.6,
                        MinVolAccel = -0.2,
                        PullbackToEma9Atr = 0.40,
                        MaxVwapDistAtr = 1.0,
                        RequireHtfBias = false,
                    })),
                    new StrategyVariant("high-frequency", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 20.0,
                        AllowLong = true,
                        AllowShort = true,
                        RvolMin = 0.0,
                        L2LiquidityMin = 0.0,
                        SpreadZMax = 10.0,
                        MinVolAccel = -10.0,
                        OfiSignalThreshold = 0.0,
                        PullbackToEma9Atr = 1.5,
                        MaxVwapDistAtr = 2.5,
                        UseTrendFilter = false,
                        RequirePullback = false,
                        MinEntryScore = 4,
                        SwingLookback = 2,
                        CooldownBars = 0,
                        RequireHtfBias = false,
                        RequireMtfAlign = false,
                        SkipFirstNMinutes = 0,
                        EntryWindows = [(570, 960)],
                        RsiMinLong = 0.0,
                        RsiMaxLong = 100.0,
                        RsiMinShort = 0.0,
                        RsiMaxShort = 100.0,
                        HardStopR = 0.9,
                        BreakevenR = 0.35,
                        TrailR = 0.35,
                        GivebackPct = 0.30,
                        Tp1R = 0.70,
                        Tp2R = 1.40,
                        MaxHoldBars = 35,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("pf-target", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 20.0,
                        AllowLong = true,
                        AllowShort = true,
                        RvolMin = 0.35,
                        L2LiquidityMin = 8.0,
                        SpreadZMax = 3.0,
                        MinVolAccel = -0.5,
                        OfiSignalThreshold = 0.03,
                        PullbackToEma9Atr = 0.6,
                        MaxVwapDistAtr = 1.4,
                        UseTrendFilter = true,
                        RequirePullback = true,
                        MinEntryScore = 6,
                        SwingLookback = 3,
                        CooldownBars = 1,
                        RequireHtfBias = true,
                        RequireMtfAlign = false,
                        SkipFirstNMinutes = 0,
                        EntryWindows = [(570, 960)],
                        RsiMinLong = 30.0,
                        RsiMaxLong = 80.0,
                        RsiMinShort = 20.0,
                        RsiMaxShort = 70.0,
                        HardStopR = 0.9,
                        BreakevenR = 0.35,
                        TrailR = 0.30,
                        GivebackPct = 0.25,
                        Tp1R = 0.60,
                        Tp2R = 1.20,
                        MaxHoldBars = 28,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("pf-target-short", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 20.0,
                        AllowLong = false,
                        AllowShort = true,
                        RvolMin = 0.35,
                        L2LiquidityMin = 8.0,
                        SpreadZMax = 3.0,
                        MinVolAccel = -0.5,
                        OfiSignalThreshold = 0.03,
                        PullbackToEma9Atr = 0.7,
                        MaxVwapDistAtr = 1.4,
                        UseTrendFilter = true,
                        RequirePullback = true,
                        MinEntryScore = 6,
                        SwingLookback = 3,
                        CooldownBars = 1,
                        RequireHtfBias = true,
                        RequireMtfAlign = false,
                        SkipFirstNMinutes = 0,
                        EntryWindows = [(570, 960)],
                        RsiMinShort = 20.0,
                        RsiMaxShort = 70.0,
                        HardStopR = 0.85,
                        BreakevenR = 0.30,
                        TrailR = 0.25,
                        GivebackPct = 0.20,
                        Tp1R = 0.50,
                        Tp2R = 1.10,
                        MaxHoldBars = 24,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("strict-mtf", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 50.0,
                        RvolMin = 0.9,
                        L2LiquidityMin = 25.0,
                        SpreadZMax = 2.0,
                        OfiSignalThreshold = 0.08,
                        PullbackToEma9Atr = 0.25,
                        MaxVwapDistAtr = 0.70,
                        RequireHtfBias = true,
                        RequireMtfAlign = true,
                        Tp1R = 1.1,
                        Tp2R = 2.4,
                    })),
                    new StrategyVariant("defensive-usd30", () => new StrategyV9(new V9Config
                    {
                        RiskPerTradeDollars = 15.0,
                        AllowLong = false,
                        AllowShort = true,
                        RvolMin = 0.6,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 2.8,
                        MinVolAccel = -0.25,
                        OfiSignalThreshold = 0.03,
                        PullbackToEma9Atr = 0.45,
                        MaxVwapDistAtr = 1.1,
                        HardStopR = 0.8,
                        BreakevenR = 0.40,
                        TrailR = 0.30,
                        GivebackPct = 0.25,
                        Tp1R = 0.70,
                        Tp2R = 1.50,
                        MaxHoldBars = 35,
                        UseFixedGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    }))
                ]),
        ];
    }
}
