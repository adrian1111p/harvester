using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record ReplayScannerRankedSymbolRow(
    string Symbol,
    double WeightedScore,
    bool Eligible,
    double AverageRank
);

public sealed record ReplayScannerSymbolSelectionSnapshotRow(
    DateTime TimestampUtc,
    string SourcePath,
    IReadOnlyList<ReplayScannerRankedSymbolRow> RankedSymbols,
    IReadOnlyList<string> SelectedSymbols
);

public interface IReplayScannerSelectionSource
{
    ReplayScannerSymbolSelectionSnapshotRow GetScannerSelectionSnapshot();
}

public interface IReplaySimulationFeedbackSink
{
    void OnReplaySliceResult(StrategyDataSlice dataSlice, ReplaySliceSimulationResult result, string activeSymbol);
}

public sealed record ReplayDayTradingContext(
    DateTime TimestampUtc,
    string Symbol,
    double MarkPrice,
    double BidPrice,
    double AskPrice,
    double PositionQuantity,
    double AveragePrice
);

public sealed record ReplayDayTradingDecision(
    IReadOnlyList<ReplayOrderIntent> Orders,
    bool StopFurtherProcessing
);

public interface IReplayGlobalSafetyOverlayStrategy
{
    ReplayDayTradingDecision Evaluate(ReplayDayTradingContext context);
}

public interface IReplayEntryStrategy
{
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context, ReplayScannerSymbolSelectionSnapshotRow selection);
}

public interface IReplayTradeManagementStrategy
{
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context);
}

public sealed record Tmg001BracketConfig(
    bool Enabled,
    double StopLossPct,
    double TakeProfitPct,
    string TimeInForce
)
{
    public static Tmg001BracketConfig Default { get; } = new(
        Enabled: true,
        StopLossPct: 0.003,
        TakeProfitPct: 0.006,
        TimeInForce: "DAY");
}

public sealed record Tmg002BreakEvenConfig(
    bool Enabled,
    double TriggerProfitPct,
    double StopOffsetPct,
    string TimeInForce
)
{
    public static Tmg002BreakEvenConfig Default { get; } = new(
    Enabled: false,
        TriggerProfitPct: 0.003,
        StopOffsetPct: 0.0,
        TimeInForce: "DAY");
}

public sealed record Tmg003TrailingProgressionConfig(
    bool Enabled,
    double TriggerProfitPct,
    double TrailOffsetPct,
    string TimeInForce
)
{
    public static Tmg003TrailingProgressionConfig Default { get; } = new(
        Enabled: false,
        TriggerProfitPct: 0.006,
        TrailOffsetPct: 0.002,
        TimeInForce: "DAY");
}

public sealed record Tmg004PartialTakeProfitRunnerTrailConfig(
    bool Enabled,
    double TriggerProfitPct,
    double TakeProfitPct,
    double TakeProfitFraction,
    double RunnerTrailOffsetPct,
    string TimeInForce
)
{
    public static Tmg004PartialTakeProfitRunnerTrailConfig Default { get; } = new(
        Enabled: false,
        TriggerProfitPct: 0.008,
        TakeProfitPct: 0.008,
        TakeProfitFraction: 0.5,
        RunnerTrailOffsetPct: 0.002,
        TimeInForce: "DAY");
}

public sealed record Tmg005TimeStopConfig(
    bool Enabled,
    int MaxHoldingBars,
    int MaxHoldingMinutes,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg005TimeStopConfig Default { get; } = new(
        Enabled: false,
        MaxHoldingBars: 30,
        MaxHoldingMinutes: 120,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg006VolatilityAdaptiveExitConfig(
    bool Enabled,
    double LowVolThresholdPct,
    double HighVolThresholdPct,
    double LowStopLossPct,
    double LowTakeProfitPct,
    double MidStopLossPct,
    double MidTakeProfitPct,
    double HighStopLossPct,
    double HighTakeProfitPct,
    string TimeInForce
)
{
    public static Tmg006VolatilityAdaptiveExitConfig Default { get; } = new(
        Enabled: false,
        LowVolThresholdPct: 0.002,
        HighVolThresholdPct: 0.006,
        LowStopLossPct: 0.002,
        LowTakeProfitPct: 0.004,
        MidStopLossPct: 0.003,
        MidTakeProfitPct: 0.006,
        HighStopLossPct: 0.004,
        HighTakeProfitPct: 0.010,
        TimeInForce: "DAY");
}

public sealed record Tmg007DrawdownDeriskConfig(
    bool Enabled,
    double DeriskDrawdownPct,
    double FlattenDrawdownPct,
    double DeriskFraction,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg007DrawdownDeriskConfig Default { get; } = new(
        Enabled: false,
        DeriskDrawdownPct: 0.003,
        FlattenDrawdownPct: 0.006,
        DeriskFraction: 0.5,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg008SessionVwapReversionConfig(
    bool Enabled,
    int MinSamples,
    double AdverseDeviationPct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg008SessionVwapReversionConfig Default { get; } = new(
        Enabled: false,
        MinSamples: 5,
        AdverseDeviationPct: 0.002,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg009LiquiditySpreadExitConfig(
    bool Enabled,
    double SpreadTriggerPct,
    bool RequireUnrealizedLoss,
    double MinAdverseMovePct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg009LiquiditySpreadExitConfig Default { get; } = new(
        Enabled: false,
        SpreadTriggerPct: 0.003,
        RequireUnrealizedLoss: true,
        MinAdverseMovePct: 0.001,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKETABLE_LIMIT");
}

public sealed record Tmg010EventRiskCooldownConfig(
    bool Enabled,
    double ShockMovePct,
    double SpreadTriggerPct,
    int CooldownBars,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg010EventRiskCooldownConfig Default { get; } = new(
        Enabled: false,
        ShockMovePct: 0.015,
        SpreadTriggerPct: 0.010,
        CooldownBars: 5,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg011StallExitConfig(
    bool Enabled,
    int MinHoldingBars,
    double MaxAbsoluteMovePct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg011StallExitConfig Default { get; } = new(
        Enabled: false,
        MinHoldingBars: 10,
        MaxAbsoluteMovePct: 0.0015,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg012PnlCapExitConfig(
    bool Enabled,
    double StopLossUsd,
    double TakeProfitUsd,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg012PnlCapExitConfig Default { get; } = new(
        Enabled: false,
        StopLossUsd: 20.0,
        TakeProfitUsd: 40.0,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg013SpreadPersistenceExitConfig(
    bool Enabled,
    double SpreadTriggerPct,
    int MinConsecutiveBars,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg013SpreadPersistenceExitConfig Default { get; } = new(
        Enabled: false,
        SpreadTriggerPct: 0.004,
        MinConsecutiveBars: 3,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKETABLE_LIMIT");
}

public sealed record Tmg014GapRiskExitConfig(
    bool Enabled,
    double GapMovePct,
    bool RequireAdverseDirection,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg014GapRiskExitConfig Default { get; } = new(
        Enabled: false,
        GapMovePct: 0.01,
        RequireAdverseDirection: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg015AdverseDriftExitConfig(
    bool Enabled,
    int MinConsecutiveAdverseBars,
    double MinCumulativeAdverseMovePct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg015AdverseDriftExitConfig Default { get; } = new(
        Enabled: false,
        MinConsecutiveAdverseBars: 3,
        MinCumulativeAdverseMovePct: 0.002,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg016PeakPullbackExitConfig(
    bool Enabled,
    double ActivationProfitPct,
    double PullbackFromPeakPct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg016PeakPullbackExitConfig Default { get; } = new(
        Enabled: false,
        ActivationProfitPct: 0.004,
        PullbackFromPeakPct: 0.002,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg017MicrostructureStressExitConfig(
    bool Enabled,
    double SpreadTriggerPct,
    double MidDislocationPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg017MicrostructureStressExitConfig Default { get; } = new(
        Enabled: false,
        SpreadTriggerPct: 0.003,
        MidDislocationPct: 0.001,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKETABLE_LIMIT");
}

public sealed record Tmg018StaleFavorableMoveExitConfig(
    bool Enabled,
    int MaxBarsWithoutFavorableExtension,
    double MinOpenProfitPct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg018StaleFavorableMoveExitConfig Default { get; } = new(
        Enabled: false,
        MaxBarsWithoutFavorableExtension: 10,
        MinOpenProfitPct: 0.001,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg019RollingAdverseWindowExitConfig(
    bool Enabled,
    int WindowBars,
    double AdverseMoveSumPct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg019RollingAdverseWindowExitConfig Default { get; } = new(
        Enabled: false,
        WindowBars: 5,
        AdverseMoveSumPct: 0.003,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg020UnderperformanceTimeoutExitConfig(
    bool Enabled,
    int MaxBarsToReachMinProfit,
    double MinProfitPct,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg020UnderperformanceTimeoutExitConfig Default { get; } = new(
        Enabled: false,
        MaxBarsToReachMinProfit: 20,
        MinProfitPct: 0.001,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg021QuotePressureExitConfig(
    bool Enabled,
    double MinPressurePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg021QuotePressureExitConfig Default { get; } = new(
        Enabled: false,
        MinPressurePct: 0.001,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKETABLE_LIMIT");
}

public sealed record Tmg022VolatilityShockWindowExitConfig(
    bool Enabled,
    int WindowBars,
    double ShockMoveSumPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg022VolatilityShockWindowExitConfig Default { get; } = new(
        Enabled: false,
        WindowBars: 3,
        ShockMoveSumPct: 0.01,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg023ProfitReversionFailsafeExitConfig(
    bool Enabled,
    double ActivationProfitPct,
    double ReversionProfitFloorPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg023ProfitReversionFailsafeExitConfig Default { get; } = new(
        Enabled: false,
        ActivationProfitPct: 0.004,
        ReversionProfitFloorPct: 0.0005,
        RequireAdverseUnrealized: false,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg024RangeCompressionExitConfig(
    bool Enabled,
    int WindowBars,
    double MaxRangePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg024RangeCompressionExitConfig Default { get; } = new(
        Enabled: false,
        WindowBars: 5,
        MaxRangePct: 0.001,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg025RollingVolatilityFloorExitConfig(
    bool Enabled,
    int WindowBars,
    double MaxRealizedVolPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg025RollingVolatilityFloorExitConfig Default { get; } = new(
        Enabled: false,
        WindowBars: 5,
        MaxRealizedVolPct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg026ChopAdverseExitConfig(
    bool Enabled,
    int WindowBars,
    int MinSignAlternations,
    double MinAdverseMoveSumPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg026ChopAdverseExitConfig Default { get; } = new(
        Enabled: false,
        WindowBars: 6,
        MinSignAlternations: 4,
        MinAdverseMoveSumPct: 0.002,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg027TrendExhaustionExitConfig(
    bool Enabled,
    int FavorableBarsLookback,
    double MinFavorableMovePct,
    int ReversalConfirmBars,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg027TrendExhaustionExitConfig Default { get; } = new(
        Enabled: false,
        FavorableBarsLookback: 4,
        MinFavorableMovePct: 0.002,
        ReversalConfirmBars: 2,
        RequireAdverseUnrealized: false,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public interface IReplayEndOfDayStrategy
{
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context);
}

public sealed record Eod001ForceFlatConfig(
    bool Enabled,
    int SessionCloseHourUtc,
    int SessionCloseMinuteUtc,
    int FlattenLeadMinutes,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Eod001ForceFlatConfig Default { get; } = new(
        Enabled: true,
        SessionCloseHourUtc: 21,
        SessionCloseMinuteUtc: 0,
        FlattenLeadMinutes: 5,
        FlattenRoute: "SMART",
        FlattenTif: "DAY+",
        FlattenOrderType: "MARKET");
}

public sealed class ReplayDayTradingPipeline
{
    private readonly IReadOnlyList<IReplayGlobalSafetyOverlayStrategy> _globalSafetyOverlays;
    private readonly IReadOnlyList<IReplayEntryStrategy> _entryStrategies;
    private readonly IReadOnlyList<IReplayTradeManagementStrategy> _tradeManagementStrategies;
    private readonly IReadOnlyList<IReplayEndOfDayStrategy> _endOfDayStrategies;

    public ReplayDayTradingPipeline(
        IReadOnlyList<IReplayGlobalSafetyOverlayStrategy> globalSafetyOverlays,
        IReadOnlyList<IReplayEntryStrategy> entryStrategies,
        IReadOnlyList<IReplayTradeManagementStrategy> tradeManagementStrategies,
        IReadOnlyList<IReplayEndOfDayStrategy> endOfDayStrategies)
    {
        _globalSafetyOverlays = globalSafetyOverlays;
        _entryStrategies = entryStrategies;
        _tradeManagementStrategies = tradeManagementStrategies;
        _endOfDayStrategies = endOfDayStrategies;
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(
        ReplayDayTradingContext context,
        ReplayScannerSymbolSelectionSnapshotRow selection)
    {
        var orders = new List<ReplayOrderIntent>();

        foreach (var overlay in _globalSafetyOverlays)
        {
            var decision = overlay.Evaluate(context);
            if (decision.Orders.Count > 0)
            {
                orders.AddRange(decision.Orders);
            }

            if (decision.StopFurtherProcessing)
            {
                return orders;
            }
        }

        foreach (var entry in _entryStrategies)
        {
            var entryOrders = entry.Evaluate(context, selection);
            if (entryOrders.Count > 0)
            {
                orders.AddRange(entryOrders);
            }
        }

        foreach (var management in _tradeManagementStrategies)
        {
            var managementOrders = management.Evaluate(context);
            if (managementOrders.Count > 0)
            {
                orders.AddRange(managementOrders);
            }
        }

        foreach (var endOfDay in _endOfDayStrategies)
        {
            var eodOrders = endOfDay.Evaluate(context);
            if (eodOrders.Count > 0)
            {
                orders.AddRange(eodOrders);
            }
        }

        return orders;
    }
}

public sealed class ReplayScannerSymbolSelectionModule
{
    private readonly ReplayScannerSymbolSelectionSnapshotRow _snapshot;

    public ReplayScannerSymbolSelectionModule(string candidatesInputPath, int topN, double minScore)
    {
        if (string.IsNullOrWhiteSpace(candidatesInputPath))
        {
            throw new ArgumentException("Replay scanner candidates input path is required.", nameof(candidatesInputPath));
        }

        var fullPath = Path.GetFullPath(candidatesInputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay scanner candidates input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ScannerCandidateInputRow[]>(File.ReadAllText(fullPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var ranked = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => new ReplayScannerRankedSymbolRow(
                x.Symbol.Trim().ToUpperInvariant(),
                x.WeightedScore,
                x.Eligible is not false,
                x.AverageRank))
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .ToArray();

        var selected = ranked
            .Where(x => x.Eligible)
            .Where(x => x.WeightedScore >= minScore)
            .Take(Math.Max(1, topN))
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _snapshot = new ReplayScannerSymbolSelectionSnapshotRow(
            DateTime.UtcNow,
            fullPath,
            ranked,
            selected);
    }

    public ReplayScannerSymbolSelectionSnapshotRow GetSnapshot()
    {
        return _snapshot;
    }

    private sealed class ScannerCandidateInputRow
    {
        public string Symbol { get; set; } = string.Empty;
        public double WeightedScore { get; set; }
        public bool? Eligible { get; set; }
        public double AverageRank { get; set; }
    }
}

public sealed record Ovl001FlattenConfig(
    int ImmediateWindowSec,
    double ImmediateAdverseMovePct,
    double ImmediateAdverseMoveUsd,
    double GivebackPctOfNotional,
    double GivebackUsdCap,
    bool TrailingActivatesOnlyAfterProfit,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Ovl001FlattenConfig Default { get; } = new(
        ImmediateWindowSec: 5,
        ImmediateAdverseMovePct: 0.002,
        ImmediateAdverseMoveUsd: 10.0,
        GivebackPctOfNotional: 0.01,
        GivebackUsdCap: 30.0,
        TrailingActivatesOnlyAfterProfit: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY+",
        FlattenOrderType: "MARKET");
}

public sealed class Ovl001FlattenReversalAndGivebackCapStrategy : IReplayGlobalSafetyOverlayStrategy
{
    public const string StrategyId = "OVL_001_FLATTEN_REVERSAL_AND_GIVEBACK_CAP";

    private readonly Ovl001FlattenConfig _config;
    private readonly Dictionary<string, Ovl001PositionState> _stateBySymbol;

    public Ovl001FlattenReversalAndGivebackCapStrategy(Ovl001FlattenConfig? config = null)
    {
        _config = config ?? Ovl001FlattenConfig.Default;
        _stateBySymbol = new Dictionary<string, Ovl001PositionState>(StringComparer.OrdinalIgnoreCase);
    }

    public void OnPositionEvent(
        string symbol,
        DateTime timestampUtc,
        double positionQuantity,
        double averagePrice,
        IReadOnlyList<ReplayFillRow> fills)
    {
        var normalizedSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return;
        }

        if (Math.Abs(positionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(normalizedSymbol);
            return;
        }

        var side = positionQuantity > 0 ? "LONG" : "SHORT";
        var shares = Math.Abs(positionQuantity);

        if (!_stateBySymbol.TryGetValue(normalizedSymbol, out var current)
            || !string.Equals(current.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            var openingFill = fills
                .Where(x => string.Equals(x.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.TimestampUtc)
                .FirstOrDefault();

            var entryTimeUtc = openingFill?.TimestampUtc ?? timestampUtc;
            var entryPrice = averagePrice > 0
                ? averagePrice
                : openingFill?.FillPrice ?? 0.0;

            _stateBySymbol[normalizedSymbol] = new Ovl001PositionState(
                EntryTimeUtc: entryTimeUtc,
                EntryPrice: entryPrice,
                Shares: shares,
                Side: side,
                PeakPrice: null,
                TroughPrice: null,
                PeakProfitUsd: 0.0,
                TrailingActive: false);
            return;
        }

        _stateBySymbol[normalizedSymbol] = current with
        {
            Shares = shares,
            EntryPrice = averagePrice > 0 ? averagePrice : current.EntryPrice
        };
    }

    public ReplayDayTradingDecision Evaluate(ReplayDayTradingContext context)
    {
        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            return new ReplayDayTradingDecision([], false);
        }

        if (!_stateBySymbol.TryGetValue(symbol, out var state))
        {
            state = new Ovl001PositionState(
                EntryTimeUtc: context.TimestampUtc,
                EntryPrice: context.AveragePrice,
                Shares: Math.Abs(context.PositionQuantity),
                Side: context.PositionQuantity > 0 ? "LONG" : "SHORT",
                PeakPrice: null,
                TroughPrice: null,
                PeakProfitUsd: 0.0,
                TrailingActive: false);
        }

        var shares = Math.Max(1e-9, Math.Abs(context.PositionQuantity));
        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        state = state with
        {
            Shares = shares,
            Side = side,
            EntryPrice = context.AveragePrice > 0 ? context.AveragePrice : state.EntryPrice
        };

        var entryPrice = Math.Max(1e-9, state.EntryPrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entryPrice) * shares
            : (entryPrice - context.MarkPrice) * shares;
        var adverse = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice < entryPrice
            : context.MarkPrice > entryPrice;

        var ageSec = Math.Max(0, (context.TimestampUtc - state.EntryTimeUtc).TotalSeconds);
        var adversePct = Math.Abs(context.MarkPrice - entryPrice) / entryPrice;
        var immediatePctTriggered = _config.ImmediateAdverseMovePct > 0 && adversePct >= _config.ImmediateAdverseMovePct;
        var immediateUsdTriggered = _config.ImmediateAdverseMoveUsd > 0 && unrealizedPnl <= -_config.ImmediateAdverseMoveUsd;
        if (ageSec <= _config.ImmediateWindowSec && adverse && (immediatePctTriggered || immediateUsdTriggered))
        {
            _stateBySymbol[symbol] = state;
            return BuildFlattenDecision(context, "immediate_reversal_flatten");
        }

        if (string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase))
        {
            var peakPrice = state.PeakPrice.HasValue
                ? Math.Max(state.PeakPrice.Value, context.MarkPrice)
                : context.MarkPrice;
            var peakProfit = Math.Max(state.PeakProfitUsd, (peakPrice - entryPrice) * shares);
            state = state with
            {
                PeakPrice = peakPrice,
                PeakProfitUsd = peakProfit
            };
        }
        else
        {
            var troughPrice = state.TroughPrice.HasValue
                ? Math.Min(state.TroughPrice.Value, context.MarkPrice)
                : context.MarkPrice;
            var peakProfit = Math.Max(state.PeakProfitUsd, (entryPrice - troughPrice) * shares);
            state = state with
            {
                TroughPrice = troughPrice,
                PeakProfitUsd = peakProfit
            };
        }

        var trailingActive = _config.TrailingActivatesOnlyAfterProfit
            ? state.PeakProfitUsd > 0
            : true;
        state = state with { TrailingActive = trailingActive };

        var positionNotional = entryPrice * shares;
        var givebackLimitUsd = Math.Min(_config.GivebackPctOfNotional * positionNotional, _config.GivebackUsdCap);
        var givebackUsd = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, ((state.PeakPrice ?? context.MarkPrice) - context.MarkPrice) * shares)
            : Math.Max(0.0, (context.MarkPrice - (state.TroughPrice ?? context.MarkPrice)) * shares);

        _stateBySymbol[symbol] = state;

        if (state.TrailingActive && givebackUsd >= givebackLimitUsd)
        {
            return BuildFlattenDecision(context, "giveback_cap_flatten");
        }

        return new ReplayDayTradingDecision([], false);
    }

    private ReplayDayTradingDecision BuildFlattenDecision(ReplayDayTradingContext context, string reason)
    {
        var symbol = context.Symbol.Trim().ToUpperInvariant();
        var qty = Math.Abs(context.PositionQuantity);
        if (qty <= 1e-9)
        {
            return new ReplayDayTradingDecision([], true);
        }

        var flattenSide = context.PositionQuantity > 0 ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        var cancelIntent = new ReplayOrderIntent(
            TimestampUtc: context.TimestampUtc,
            Symbol: symbol,
            Side: "",
            Quantity: 0,
            OrderType: "CANCEL",
            LimitPrice: null,
            StopPrice: null,
            TrailAmount: null,
            TrailPercent: null,
            TimeInForce: _config.FlattenTif,
            ExpireAtUtc: null,
            Source: $"{StrategyId}:{reason}:cancel",
            OrderId: string.Empty,
            ParentOrderId: string.Empty,
            OcoGroup: string.Empty,
            ComboGroupId: string.Empty,
            Route: _config.FlattenRoute);

        var flattenIntent = new ReplayOrderIntent(
            TimestampUtc: context.TimestampUtc,
            Symbol: symbol,
            Side: flattenSide,
            Quantity: qty,
            OrderType: flattenOrderType,
            LimitPrice: flattenLimitPrice,
            StopPrice: null,
            TrailAmount: null,
            TrailPercent: null,
            TimeInForce: _config.FlattenTif,
            ExpireAtUtc: null,
            Source: $"{StrategyId}:{reason}:flatten",
            OrderId: string.Empty,
            ParentOrderId: string.Empty,
            OcoGroup: string.Empty,
            ComboGroupId: string.Empty,
            Route: _config.FlattenRoute);

        return new ReplayDayTradingDecision([cancelIntent, flattenIntent], true);
    }

    private sealed record Ovl001PositionState(
        DateTime EntryTimeUtc,
        double EntryPrice,
        double Shares,
        string Side,
        double? PeakPrice,
        double? TroughPrice,
        double PeakProfitUsd,
        bool TrailingActive
    );
}

public sealed class ReplayScannerSingleShotEntryStrategy : IReplayEntryStrategy
{
    private readonly HashSet<string> _submittedSymbols;
    private readonly double _orderQuantity;
    private readonly string _orderSide;
    private readonly string _orderType;
    private readonly string _timeInForce;
    private readonly double _limitOffsetBps;

    public ReplayScannerSingleShotEntryStrategy(
        double orderQuantity,
        string orderSide,
        string orderType,
        string timeInForce,
        double limitOffsetBps)
    {
        _submittedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _orderQuantity = Math.Max(0, orderQuantity);
        _orderSide = NormalizeOrderSide(orderSide);
        _orderType = NormalizeOrderType(orderType);
        _timeInForce = NormalizeTimeInForce(timeInForce);
        _limitOffsetBps = Math.Max(0, limitOffsetBps);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context, ReplayScannerSymbolSelectionSnapshotRow selection)
    {
        if (_orderQuantity <= 0)
        {
            return [];
        }

        var symbol = context.Symbol.Trim().ToUpperInvariant();
        if (!selection.SelectedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        if (_submittedSymbols.Contains(symbol))
        {
            return [];
        }

        var side = ResolveOrderSide(context.PositionQuantity);
        if (string.IsNullOrWhiteSpace(side))
        {
            _submittedSymbols.Add(symbol);
            return [];
        }

        double? limitPrice = null;
        if (string.Equals(_orderType, "LMT", StringComparison.OrdinalIgnoreCase))
        {
            if (context.MarkPrice <= 0)
            {
                return [];
            }

            var offset = context.MarkPrice * (_limitOffsetBps / 10000.0);
            limitPrice = string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0.0001, context.MarkPrice - offset)
                : context.MarkPrice + offset;
        }

        _submittedSymbols.Add(symbol);

        return
        [
            new ReplayOrderIntent(
                context.TimestampUtc,
                symbol,
                side,
                _orderQuantity,
                _orderType,
                limitPrice,
                null,
                null,
                null,
                _timeInForce,
                null,
                "entry:scanner-candidate")
        ];
    }

    private string ResolveOrderSide(double positionQuantity)
    {
        if (string.Equals(_orderSide, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return positionQuantity > 1e-9 ? "SELL" : "BUY";
        }

        return _orderSide;
    }

    private static string NormalizeOrderSide(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "BUY" : value.Trim().ToUpperInvariant();
        return normalized is "BUY" or "SELL" or "AUTO"
            ? normalized
            : "BUY";
    }

    private static string NormalizeOrderType(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "MKT" : value.Trim().ToUpperInvariant();
        return normalized is "MKT" or "LMT"
            ? normalized
            : "MKT";
    }

    private static string NormalizeTimeInForce(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "DAY" : value.Trim().ToUpperInvariant();
        return normalized is "DAY" or "DAY+" or "GTC" or "IOC" or "FOK"
            ? normalized
            : "DAY";
    }
}

public sealed class Tmg001BracketExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_001_BRACKET_EXIT";

    private readonly Tmg001BracketConfig _config;
    private readonly Dictionary<string, BracketState> _stateBySymbol;

    public Tmg001BracketExitStrategy(Tmg001BracketConfig? config = null)
    {
        _config = config ?? Tmg001BracketConfig.Default;
        _stateBySymbol = new Dictionary<string, BracketState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var shares = Math.Abs(context.PositionQuantity);
        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var entry = context.AveragePrice;
        if (entry <= 0)
        {
            return [];
        }

        var needsRefresh = true;
        if (_stateBySymbol.TryGetValue(symbol, out var existing))
        {
            needsRefresh = !string.Equals(existing.Side, side, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(existing.Shares - shares) > 1e-9;
        }

        if (!needsRefresh)
        {
            return [];
        }

        var ocoGroup = $"{StrategyId}:{symbol}:{context.TimestampUtc:yyyyMMddHHmmssfff}";
        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var limitPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TakeProfitPct)
            : entry * (1.0 - _config.TakeProfitPct);
        var stopPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 - _config.StopLossPct)
            : entry * (1.0 + _config.StopLossPct);

        var orders = new List<ReplayOrderIntent>();
        if (existing is not null)
        {
            orders.Add(new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:refresh-cancel"));
        }

        orders.Add(new ReplayOrderIntent(
            TimestampUtc: context.TimestampUtc,
            Symbol: symbol,
            Side: exitSide,
            Quantity: shares,
            OrderType: "LMT",
            LimitPrice: limitPrice,
            StopPrice: null,
            TrailAmount: null,
            TrailPercent: null,
            TimeInForce: _config.TimeInForce,
            ExpireAtUtc: null,
            Source: $"trade-management:{StrategyId}:take-profit",
            OcoGroup: ocoGroup));

        orders.Add(new ReplayOrderIntent(
            TimestampUtc: context.TimestampUtc,
            Symbol: symbol,
            Side: exitSide,
            Quantity: shares,
            OrderType: "STP",
            LimitPrice: null,
            StopPrice: stopPrice,
            TrailAmount: null,
            TrailPercent: null,
            TimeInForce: _config.TimeInForce,
            ExpireAtUtc: null,
            Source: $"trade-management:{StrategyId}:stop-loss",
            OcoGroup: ocoGroup));

        _stateBySymbol[symbol] = new BracketState(side, shares, ocoGroup);
        return orders;
    }

    private sealed record BracketState(
        string Side,
        double Shares,
        string OcoGroup
    );
}

public sealed class Tmg002BreakEvenEscalationStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_002_BREAK_EVEN_ESCALATION";

    private readonly Tmg002BreakEvenConfig _config;
    private readonly HashSet<string> _activatedSymbols;

    public Tmg002BreakEvenEscalationStrategy(Tmg002BreakEvenConfig? config = null)
    {
        _config = config ?? Tmg002BreakEvenConfig.Default;
        _activatedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _activatedSymbols.Remove(symbol);
            return [];
        }

        if (_activatedSymbols.Contains(symbol))
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var triggerPx = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TriggerProfitPct)
            : entry * (1.0 - _config.TriggerProfitPct);
        var triggered = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice >= triggerPx
            : context.MarkPrice <= triggerPx;
        if (!triggered)
        {
            return [];
        }

        _activatedSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var stopSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var baseStop = entry;
        var stopOffset = Math.Max(0.0, _config.StopOffsetPct) * entry;
        var stopPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? baseStop + stopOffset
            : Math.Max(0.0001, baseStop - stopOffset);

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:escalate-cancel"),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: stopSide,
                Quantity: qty,
                OrderType: "STP",
                LimitPrice: null,
                StopPrice: stopPrice,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:break-even-stop")
        ];
    }
}

public sealed class Tmg003TrailingProgressionStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_003_TRAILING_PROGRESSION";

    private readonly Tmg003TrailingProgressionConfig _config;
    private readonly HashSet<string> _activatedSymbols;

    public Tmg003TrailingProgressionStrategy(Tmg003TrailingProgressionConfig? config = null)
    {
        _config = config ?? Tmg003TrailingProgressionConfig.Default;
        _activatedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _activatedSymbols.Remove(symbol);
            return [];
        }

        if (_activatedSymbols.Contains(symbol))
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var triggerPx = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TriggerProfitPct)
            : entry * (1.0 - _config.TriggerProfitPct);
        var triggered = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice >= triggerPx
            : context.MarkPrice <= triggerPx;
        if (!triggered)
        {
            return [];
        }

        _activatedSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var stopSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var trailAmount = Math.Max(0.0001, context.MarkPrice * Math.Max(0.0, _config.TrailOffsetPct));

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:activate-cancel"),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: stopSide,
                Quantity: qty,
                OrderType: "TRAIL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: trailAmount,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:activate-trailing")
        ];
    }
}

public sealed class Tmg004PartialTakeProfitRunnerTrailStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_004_PARTIAL_TP_RUNNER_TRAIL";

    private readonly Tmg004PartialTakeProfitRunnerTrailConfig _config;
    private readonly HashSet<string> _activatedSymbols;

    public Tmg004PartialTakeProfitRunnerTrailStrategy(Tmg004PartialTakeProfitRunnerTrailConfig? config = null)
    {
        _config = config ?? Tmg004PartialTakeProfitRunnerTrailConfig.Default;
        _activatedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _activatedSymbols.Remove(symbol);
            return [];
        }

        if (_activatedSymbols.Contains(symbol))
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var triggerPx = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TriggerProfitPct)
            : entry * (1.0 - _config.TriggerProfitPct);
        var triggered = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice >= triggerPx
            : context.MarkPrice <= triggerPx;
        if (!triggered)
        {
            return [];
        }

        _activatedSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var fraction = Math.Clamp(_config.TakeProfitFraction, 0.0, 1.0);
        var takeProfitQty = qty * fraction;
        var runnerQty = Math.Max(0.0, qty - takeProfitQty);
        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var takeProfitPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TakeProfitPct)
            : entry * (1.0 - _config.TakeProfitPct);
        var trailAmount = Math.Max(0.0001, context.MarkPrice * Math.Max(0.0, _config.RunnerTrailOffsetPct));

        var orders = new List<ReplayOrderIntent>
        {
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:activate-cancel")
        };

        if (takeProfitQty > 1e-9)
        {
            orders.Add(new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: takeProfitQty,
                OrderType: "LMT",
                LimitPrice: takeProfitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:partial-take-profit"));
        }

        if (runnerQty > 1e-9)
        {
            orders.Add(new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: runnerQty,
                OrderType: "TRAIL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: trailAmount,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:runner-trailing"));
        }

        return orders;
    }
}

public sealed class Tmg005TimeStopStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_005_TIME_STOP";

    private readonly Tmg005TimeStopConfig _config;
    private readonly Dictionary<string, TimeStopState> _stateBySymbol;

    public Tmg005TimeStopStrategy(Tmg005TimeStopConfig? config = null)
    {
        _config = config ?? Tmg005TimeStopConfig.Default;
        _stateBySymbol = new Dictionary<string, TimeStopState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        if (!_stateBySymbol.TryGetValue(symbol, out var state)
            || !string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new TimeStopState(
                Side: side,
                EntryTimestampUtc: context.TimestampUtc,
                BarsHeld: 0,
                Triggered: false);
        }

        state = state with { BarsHeld = state.BarsHeld + 1 };

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var elapsedMinutes = Math.Max(0, (context.TimestampUtc - state.EntryTimestampUtc).TotalMinutes);
        var barsTriggered = _config.MaxHoldingBars > 0 && state.BarsHeld >= _config.MaxHoldingBars;
        var minutesTriggered = _config.MaxHoldingMinutes > 0 && elapsedMinutes >= _config.MaxHoldingMinutes;
        if (!barsTriggered && !minutesTriggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = context.PositionQuantity > 0 ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:time-stop-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:time-stop-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record TimeStopState(
        string Side,
        DateTime EntryTimestampUtc,
        int BarsHeld,
        bool Triggered
    );
}

public sealed class Tmg006VolatilityAdaptiveExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_006_VOLATILITY_ADAPTIVE_EXIT";

    private readonly Tmg006VolatilityAdaptiveExitConfig _config;
    private readonly Dictionary<string, AdaptiveState> _stateBySymbol;

    public Tmg006VolatilityAdaptiveExitStrategy(Tmg006VolatilityAdaptiveExitConfig? config = null)
    {
        _config = config ?? Tmg006VolatilityAdaptiveExitConfig.Default;
        _stateBySymbol = new Dictionary<string, AdaptiveState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var shares = Math.Abs(context.PositionQuantity);
        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new AdaptiveState(
                Side: side,
                Shares: shares,
                LastMarkPrice: context.MarkPrice,
                EmaAbsReturnPct: 0.0,
                ActiveRegime: "MID");

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = state with
            {
                Side = side,
                Shares = shares,
                LastMarkPrice = context.MarkPrice,
                EmaAbsReturnPct = 0.0,
                ActiveRegime = "MID"
            };
        }

        var absReturnPct = state.LastMarkPrice > 1e-9
            ? Math.Abs(context.MarkPrice - state.LastMarkPrice) / state.LastMarkPrice
            : 0.0;
        var ema = state.EmaAbsReturnPct <= 0
            ? absReturnPct
            : (0.3 * absReturnPct) + (0.7 * state.EmaAbsReturnPct);
        var regime = ResolveRegime(ema);
        var (stopLossPct, takeProfitPct) = ResolveProfile(regime);

        var refreshNeeded = !string.Equals(state.ActiveRegime, regime, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase)
            || Math.Abs(state.Shares - shares) > 1e-9;

        state = state with
        {
            Side = side,
            Shares = shares,
            LastMarkPrice = context.MarkPrice,
            EmaAbsReturnPct = ema,
            ActiveRegime = regime
        };
        _stateBySymbol[symbol] = state;

        if (!refreshNeeded)
        {
            return [];
        }

        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var limitPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + takeProfitPct)
            : entry * (1.0 - takeProfitPct);
        var stopPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 - stopLossPct)
            : entry * (1.0 + stopLossPct);
        var ocoGroup = $"{StrategyId}:{symbol}:{context.TimestampUtc:yyyyMMddHHmmssfff}";

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:refresh-cancel"),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: shares,
                OrderType: "LMT",
                LimitPrice: limitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{regime}:take-profit",
                OcoGroup: ocoGroup),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: shares,
                OrderType: "STP",
                LimitPrice: null,
                StopPrice: stopPrice,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{regime}:stop-loss",
                OcoGroup: ocoGroup)
        ];
    }

    private string ResolveRegime(double emaAbsReturnPct)
    {
        if (emaAbsReturnPct <= _config.LowVolThresholdPct)
        {
            return "LOW";
        }

        if (emaAbsReturnPct >= _config.HighVolThresholdPct)
        {
            return "HIGH";
        }

        return "MID";
    }

    private (double StopLossPct, double TakeProfitPct) ResolveProfile(string regime)
    {
        return regime switch
        {
            "LOW" => (_config.LowStopLossPct, _config.LowTakeProfitPct),
            "HIGH" => (_config.HighStopLossPct, _config.HighTakeProfitPct),
            _ => (_config.MidStopLossPct, _config.MidTakeProfitPct)
        };
    }

    private sealed record AdaptiveState(
        string Side,
        double Shares,
        double LastMarkPrice,
        double EmaAbsReturnPct,
        string ActiveRegime
    );
}

public sealed class Tmg007DrawdownDeriskStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_007_DRAWDOWN_DERISK";

    private readonly Tmg007DrawdownDeriskConfig _config;
    private readonly Dictionary<string, DeriskState> _stateBySymbol;

    public Tmg007DrawdownDeriskStrategy(Tmg007DrawdownDeriskConfig? config = null)
    {
        _config = config ?? Tmg007DrawdownDeriskConfig.Default;
        _stateBySymbol = new Dictionary<string, DeriskState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var shares = Math.Abs(context.PositionQuantity);
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new DeriskState(
                Side: side,
                PeakFavorablePrice: context.MarkPrice,
                TroughFavorablePrice: context.MarkPrice,
                DeriskDone: false,
                FlattenDone: false);

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new DeriskState(
                Side: side,
                PeakFavorablePrice: context.MarkPrice,
                TroughFavorablePrice: context.MarkPrice,
                DeriskDone: false,
                FlattenDone: false);
        }

        if (string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase))
        {
            state = state with { PeakFavorablePrice = Math.Max(state.PeakFavorablePrice, context.MarkPrice) };
        }
        else
        {
            state = state with { TroughFavorablePrice = Math.Min(state.TroughFavorablePrice, context.MarkPrice) };
        }

        var drawdownPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, (state.PeakFavorablePrice - context.MarkPrice) / Math.Max(1e-9, state.PeakFavorablePrice))
            : Math.Max(0.0, (context.MarkPrice - state.TroughFavorablePrice) / Math.Max(1e-9, state.TroughFavorablePrice));

        if (!state.FlattenDone && drawdownPct >= _config.FlattenDrawdownPct)
        {
            state = state with { FlattenDone = true, DeriskDone = true };
            _stateBySymbol[symbol] = state;
            return BuildCancelAndFlatten(context, shares, side, "drawdown-flatten");
        }

        if (!state.DeriskDone && drawdownPct >= _config.DeriskDrawdownPct)
        {
            state = state with { DeriskDone = true };
            _stateBySymbol[symbol] = state;

            var fraction = Math.Clamp(_config.DeriskFraction, 0.0, 1.0);
            var deriskQty = shares * fraction;
            if (deriskQty <= 1e-9)
            {
                return [];
            }

            var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
            return
            [
                new ReplayOrderIntent(
                    TimestampUtc: context.TimestampUtc,
                    Symbol: symbol,
                    Side: string.Empty,
                    Quantity: 0,
                    OrderType: "CANCEL",
                    LimitPrice: null,
                    StopPrice: null,
                    TrailAmount: null,
                    TrailPercent: null,
                    TimeInForce: _config.FlattenTif,
                    ExpireAtUtc: null,
                    Source: $"trade-management:{StrategyId}:derisk-cancel",
                    Route: _config.FlattenRoute),
                new ReplayOrderIntent(
                    TimestampUtc: context.TimestampUtc,
                    Symbol: symbol,
                    Side: exitSide,
                    Quantity: deriskQty,
                    OrderType: "MKT",
                    LimitPrice: null,
                    StopPrice: null,
                    TrailAmount: null,
                    TrailPercent: null,
                    TimeInForce: _config.FlattenTif,
                    ExpireAtUtc: null,
                    Source: $"trade-management:{StrategyId}:derisk-partial",
                    Route: _config.FlattenRoute)
            ];
        }

        _stateBySymbol[symbol] = state;
        return [];
    }

    private IReadOnlyList<ReplayOrderIntent> BuildCancelAndFlatten(ReplayDayTradingContext context, double qty, string side, string reason)
    {
        var symbol = context.Symbol.Trim().ToUpperInvariant();
        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (exitSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{reason}:cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{reason}:flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record DeriskState(
        string Side,
        double PeakFavorablePrice,
        double TroughFavorablePrice,
        bool DeriskDone,
        bool FlattenDone
    );
}

public sealed class Tmg008SessionVwapReversionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_008_SESSION_VWAP_REVERSION_EXIT";

    private readonly Tmg008SessionVwapReversionConfig _config;
    private readonly Dictionary<string, VwapState> _stateBySymbol;

    public Tmg008SessionVwapReversionExitStrategy(Tmg008SessionVwapReversionConfig? config = null)
    {
        _config = config ?? Tmg008SessionVwapReversionConfig.Default;
        _stateBySymbol = new Dictionary<string, VwapState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new VwapState(Side: side, SampleCount: 0, CumPrice: 0.0, Triggered: false);

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new VwapState(Side: side, SampleCount: 0, CumPrice: 0.0, Triggered: false);
        }

        state = state with
        {
            SampleCount = state.SampleCount + 1,
            CumPrice = state.CumPrice + context.MarkPrice
        };

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var minSamples = Math.Max(1, _config.MinSamples);
        if (state.SampleCount < minSamples)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var sessionVwap = state.CumPrice / Math.Max(1, state.SampleCount);
        var deviationPct = (context.MarkPrice - sessionVwap) / Math.Max(1e-9, sessionVwap);
        var adverseDeviation = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? -deviationPct
            : deviationPct;
        if (adverseDeviation < Math.Max(0.0, _config.AdverseDeviationPct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:vwap-reversion-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:vwap-reversion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record VwapState(
        string Side,
        int SampleCount,
        double CumPrice,
        bool Triggered
    );
}

public sealed class Tmg009LiquiditySpreadExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_009_LIQUIDITY_SPREAD_EXIT_GUARD";

    private readonly Tmg009LiquiditySpreadExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg009LiquiditySpreadExitStrategy(Tmg009LiquiditySpreadExitConfig? config = null)
    {
        _config = config ?? Tmg009LiquiditySpreadExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var bid = context.BidPrice;
        var ask = context.AskPrice;
        if (bid <= 0 || ask <= 0 || ask < bid)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var mid = (bid + ask) / 2.0;
        if (mid <= 1e-9)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var spreadPct = (ask - bid) / mid;
        if (spreadPct < Math.Max(0.0, _config.SpreadTriggerPct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var entry = context.AveragePrice;
        var adverseMovePct = entry > 1e-9
            ? (string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0.0, (entry - context.MarkPrice) / entry)
                : Math.Max(0.0, (context.MarkPrice - entry) / entry))
            : 0.0;

        if (_config.RequireUnrealizedLoss && adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? ask
                : bid)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:spread-guard-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:spread-guard-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}

public sealed class Tmg010EventRiskCooldownGuardStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_010_EVENT_RISK_COOLDOWN_GUARD";

    private readonly Tmg010EventRiskCooldownConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg010EventRiskCooldownGuardStrategy(Tmg010EventRiskCooldownConfig? config = null)
    {
        _config = config ?? Tmg010EventRiskCooldownConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(LastMarkPrice: context.MarkPrice, CooldownBarsRemaining: 0);

        var shockMovePct = state.LastMarkPrice > 1e-9
            ? Math.Abs(context.MarkPrice - state.LastMarkPrice) / state.LastMarkPrice
            : 0.0;
        var spreadPct = 0.0;
        if (context.BidPrice > 0 && context.AskPrice > 0 && context.AskPrice >= context.BidPrice)
        {
            var mid = (context.BidPrice + context.AskPrice) / 2.0;
            if (mid > 1e-9)
            {
                spreadPct = (context.AskPrice - context.BidPrice) / mid;
            }
        }

        var riskEvent = shockMovePct >= Math.Max(0.0, _config.ShockMovePct)
            || spreadPct >= Math.Max(0.0, _config.SpreadTriggerPct);

        if (state.CooldownBarsRemaining > 0)
        {
            state = state with
            {
                LastMarkPrice = context.MarkPrice,
                CooldownBarsRemaining = state.CooldownBarsRemaining - 1
            };
            _stateBySymbol[symbol] = state;
            return [];
        }

        if (!riskEvent)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            CooldownBarsRemaining = Math.Max(0, _config.CooldownBars)
        };

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:risk-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:risk-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        double LastMarkPrice,
        int CooldownBarsRemaining
    );
}

public sealed class Tmg011StallExitGuardStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_011_STALL_EXIT_GUARD";

    private readonly Tmg011StallExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg011StallExitGuardStrategy(Tmg011StallExitConfig? config = null)
    {
        _config = config ?? Tmg011StallExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, HoldingBars: 0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, HoldingBars: 0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var holdingBars = state.HoldingBars + 1;
        var absoluteMovePct = Math.Abs(context.MarkPrice - entry) / entry;
        var shouldExit = holdingBars >= Math.Max(0, _config.MinHoldingBars)
            && absoluteMovePct <= Math.Max(0.0, _config.MaxAbsoluteMovePct);

        if (!shouldExit)
        {
            _stateBySymbol[symbol] = state with { HoldingBars = holdingBars };
            return [];
        }

        _stateBySymbol[symbol] = state with { HoldingBars = holdingBars, Triggered = true };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stall-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stall-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        int HoldingBars,
        bool Triggered
    );
}

public sealed class Tmg012PnlCapExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_012_PNL_CAP_EXIT";

    private readonly Tmg012PnlCapExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg012PnlCapExitStrategy(Tmg012PnlCapExitConfig? config = null)
    {
        _config = config ?? Tmg012PnlCapExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var qty = Math.Abs(context.PositionQuantity);
        var unrealizedPnlUsd = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;

        var stopLossTriggered = _config.StopLossUsd > 0 && unrealizedPnlUsd <= -_config.StopLossUsd;
        var takeProfitTriggered = _config.TakeProfitUsd > 0 && unrealizedPnlUsd >= _config.TakeProfitUsd;
        if (!stopLossTriggered && !takeProfitTriggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;
        var reason = takeProfitTriggered ? "take-profit" : "stop-loss";

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{reason}-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{reason}-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}

public sealed class Tmg013SpreadPersistenceExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_013_SPREAD_PERSISTENCE_EXIT";

    private readonly Tmg013SpreadPersistenceExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg013SpreadPersistenceExitStrategy(Tmg013SpreadPersistenceExitConfig? config = null)
    {
        _config = config ?? Tmg013SpreadPersistenceExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, ConsecutiveWideSpreadBars: 0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, ConsecutiveWideSpreadBars: 0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var bid = context.BidPrice;
        var ask = context.AskPrice;
        if (bid <= 0 || ask <= 0 || ask < bid)
        {
            _stateBySymbol[symbol] = state with { ConsecutiveWideSpreadBars = 0 };
            return [];
        }

        var mid = (bid + ask) / 2.0;
        if (mid <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { ConsecutiveWideSpreadBars = 0 };
            return [];
        }

        var spreadPct = (ask - bid) / mid;
        var isWideSpread = spreadPct >= Math.Max(0.0, _config.SpreadTriggerPct);
        var consecutiveWideSpreadBars = isWideSpread
            ? state.ConsecutiveWideSpreadBars + 1
            : 0;

        var minBars = Math.Max(1, _config.MinConsecutiveBars);
        if (consecutiveWideSpreadBars < minBars)
        {
            _stateBySymbol[symbol] = state with { ConsecutiveWideSpreadBars = consecutiveWideSpreadBars };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            ConsecutiveWideSpreadBars = consecutiveWideSpreadBars,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? ask
                : bid)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:persistent-spread-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:persistent-spread-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        int ConsecutiveWideSpreadBars,
        bool Triggered
    );
}

public sealed class Tmg014GapRiskExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_014_GAP_RISK_EXIT";

    private readonly Tmg014GapRiskExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg014GapRiskExitStrategy(Tmg014GapRiskExitConfig? config = null)
    {
        _config = config ?? Tmg014GapRiskExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var gapMovePct = Math.Abs(context.MarkPrice - entry) / entry;
        if (gapMovePct < Math.Max(0.0, _config.GapMovePct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var adverseDirection = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice < entry
            : context.MarkPrice > entry;
        if (_config.RequireAdverseDirection && !adverseDirection)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:gap-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:gap-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}

public sealed class Tmg015AdverseDriftExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_015_ADVERSE_DRIFT_EXIT";

    private readonly Tmg015AdverseDriftExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg015AdverseDriftExitStrategy(Tmg015AdverseDriftExitConfig? config = null)
    {
        _config = config ?? Tmg015AdverseDriftExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, ConsecutiveAdverseBars: 0, CumulativeAdverseMovePct: 0.0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, ConsecutiveAdverseBars: 0, CumulativeAdverseMovePct: 0.0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var delta = context.MarkPrice - previousMark;
        var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (delta < 0 ? Math.Abs(delta) / previousMark : 0.0)
            : (delta > 0 ? Math.Abs(delta) / previousMark : 0.0);
        var adverseBar = adverseMovePct > 0.0;

        var consecutiveAdverseBars = adverseBar ? state.ConsecutiveAdverseBars + 1 : 0;
        var cumulativeAdverseMovePct = adverseBar
            ? state.CumulativeAdverseMovePct + adverseMovePct
            : 0.0;

        var minBars = Math.Max(1, _config.MinConsecutiveAdverseBars);
        var minCumMove = Math.Max(0.0, _config.MinCumulativeAdverseMovePct);
        var shouldFlatten = consecutiveAdverseBars >= minBars
            && cumulativeAdverseMovePct >= minCumMove;

        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                ConsecutiveAdverseBars = consecutiveAdverseBars,
                CumulativeAdverseMovePct = cumulativeAdverseMovePct
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            ConsecutiveAdverseBars = consecutiveAdverseBars,
            CumulativeAdverseMovePct = cumulativeAdverseMovePct,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:drift-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:drift-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        int ConsecutiveAdverseBars,
        double CumulativeAdverseMovePct,
        bool Triggered
    );
}

public sealed class Tmg016PeakPullbackExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_016_PEAK_PULLBACK_EXIT";

    private readonly Tmg016PeakPullbackExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg016PeakPullbackExitStrategy(Tmg016PeakPullbackExitConfig? config = null)
    {
        _config = config ?? Tmg016PeakPullbackExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, PeakPrice: context.MarkPrice, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, PeakPrice: context.MarkPrice, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var peakPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(state.PeakPrice, context.MarkPrice)
            : Math.Min(state.PeakPrice <= 1e-9 ? context.MarkPrice : state.PeakPrice, context.MarkPrice);

        var profitPctFromEntry = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (peakPrice - entry) / entry
            : (entry - peakPrice) / entry;
        var activationReached = profitPctFromEntry >= Math.Max(0.0, _config.ActivationProfitPct);
        if (!activationReached)
        {
            _stateBySymbol[symbol] = state with { PeakPrice = peakPrice };
            return [];
        }

        var pullbackPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, (peakPrice - context.MarkPrice) / peakPrice)
            : Math.Max(0.0, (context.MarkPrice - peakPrice) / peakPrice);
        if (pullbackPct < Math.Max(0.0, _config.PullbackFromPeakPct))
        {
            _stateBySymbol[symbol] = state with { PeakPrice = peakPrice };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            PeakPrice = peakPrice,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:pullback-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:pullback-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double PeakPrice,
        bool Triggered
    );
}

public sealed class Tmg017MicrostructureStressExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_017_MICROSTRUCTURE_STRESS_EXIT";

    private readonly Tmg017MicrostructureStressExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg017MicrostructureStressExitStrategy(Tmg017MicrostructureStressExitConfig? config = null)
    {
        _config = config ?? Tmg017MicrostructureStressExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        if (context.BidPrice <= 0 || context.AskPrice <= 0 || context.AskPrice < context.BidPrice)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var mid = (context.BidPrice + context.AskPrice) / 2.0;
        if (mid <= 1e-9)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var spreadPct = (context.AskPrice - context.BidPrice) / mid;
        var dislocationPct = Math.Abs(context.MarkPrice - mid) / mid;
        var stressTriggered = spreadPct >= Math.Max(0.0, _config.SpreadTriggerPct)
            && dislocationPct >= Math.Max(0.0, _config.MidDislocationPct);
        if (!stressTriggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * Math.Abs(context.PositionQuantity)
            : (entry - context.MarkPrice) * Math.Abs(context.PositionQuantity);
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        _stateBySymbol[symbol] = state with { Triggered = true };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY" ? context.AskPrice : context.BidPrice)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stress-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stress-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}

public sealed class Tmg018StaleFavorableMoveExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_018_STALE_FAVORABLE_MOVE_EXIT";

    private readonly Tmg018StaleFavorableMoveExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg018StaleFavorableMoveExitStrategy(Tmg018StaleFavorableMoveExitConfig? config = null)
    {
        _config = config ?? Tmg018StaleFavorableMoveExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, BestFavorablePrice: context.MarkPrice, BarsWithoutExtension: 0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, BestFavorablePrice: context.MarkPrice, BarsWithoutExtension: 0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var improved = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice > state.BestFavorablePrice
            : context.MarkPrice < state.BestFavorablePrice;
        var bestFavorablePrice = improved
            ? context.MarkPrice
            : state.BestFavorablePrice;
        var barsWithoutExtension = improved
            ? 0
            : state.BarsWithoutExtension + 1;

        var openProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) / entry
            : (entry - context.MarkPrice) / entry;
        var minProfit = Math.Max(0.0, _config.MinOpenProfitPct);
        var shouldFlatten = barsWithoutExtension >= Math.Max(0, _config.MaxBarsWithoutFavorableExtension)
            && openProfitPct >= minProfit;
        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with
            {
                BestFavorablePrice = bestFavorablePrice,
                BarsWithoutExtension = barsWithoutExtension
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            BestFavorablePrice = bestFavorablePrice,
            BarsWithoutExtension = barsWithoutExtension,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stale-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:stale-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double BestFavorablePrice,
        int BarsWithoutExtension,
        bool Triggered
    );
}

public sealed class Tmg019RollingAdverseWindowExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_019_ROLLING_ADVERSE_WINDOW_EXIT";

    private readonly Tmg019RollingAdverseWindowExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg019RollingAdverseWindowExitStrategy(Tmg019RollingAdverseWindowExitConfig? config = null)
    {
        _config = config ?? Tmg019RollingAdverseWindowExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAdverseMoves: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAdverseMoves: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var delta = context.MarkPrice - previousMark;
        var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (delta < 0 ? Math.Abs(delta) / previousMark : 0.0)
            : (delta > 0 ? Math.Abs(delta) / previousMark : 0.0);

        var windowBars = Math.Max(1, _config.WindowBars);
        var updatedMoves = state.RecentAdverseMoves.Count > 0
            ? state.RecentAdverseMoves.ToList()
            : [];
        updatedMoves.Add(adverseMovePct);
        if (updatedMoves.Count > windowBars)
        {
            updatedMoves.RemoveAt(0);
        }

        var adverseMoveSum = updatedMoves.Sum();
        var shouldFlatten = updatedMoves.Count >= windowBars
            && adverseMoveSum >= Math.Max(0.0, _config.AdverseMoveSumPct);
        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAdverseMoves = updatedMoves
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentAdverseMoves = updatedMoves,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:rolling-window-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:rolling-window-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentAdverseMoves,
        bool Triggered
    );
}

public sealed class Tmg020UnderperformanceTimeoutExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_020_UNDERPERFORMANCE_TIMEOUT_EXIT";

    private readonly Tmg020UnderperformanceTimeoutExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg020UnderperformanceTimeoutExitStrategy(Tmg020UnderperformanceTimeoutExitConfig? config = null)
    {
        _config = config ?? Tmg020UnderperformanceTimeoutExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, HoldingBars: 0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, HoldingBars: 0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var holdingBars = state.HoldingBars + 1;
        var openProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) / entry
            : (entry - context.MarkPrice) / entry;

        var maxBars = Math.Max(0, _config.MaxBarsToReachMinProfit);
        var minProfit = Math.Max(0.0, _config.MinProfitPct);
        var shouldFlatten = holdingBars >= maxBars
            && openProfitPct < minProfit;
        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with { HoldingBars = holdingBars };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            HoldingBars = holdingBars,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:timeout-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:timeout-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        int HoldingBars,
        bool Triggered
    );
}

public sealed class Tmg021QuotePressureExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_021_QUOTE_PRESSURE_EXIT";

    private readonly Tmg021QuotePressureExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg021QuotePressureExitStrategy(Tmg021QuotePressureExitConfig? config = null)
    {
        _config = config ?? Tmg021QuotePressureExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        if (context.BidPrice <= 0 || context.AskPrice <= 0 || context.AskPrice < context.BidPrice)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var mid = (context.BidPrice + context.AskPrice) / 2.0;
        if (mid <= 1e-9)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var pressurePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, (mid - context.BidPrice) / mid)
            : Math.Max(0.0, (context.AskPrice - mid) / mid);
        if (pressurePct < Math.Max(0.0, _config.MinPressurePct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * Math.Abs(context.PositionQuantity)
            : (entry - context.MarkPrice) * Math.Abs(context.PositionQuantity);
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        _stateBySymbol[symbol] = state with { Triggered = true };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY" ? context.AskPrice : context.BidPrice)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:pressure-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:pressure-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}

public sealed class Tmg022VolatilityShockWindowExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_022_VOLATILITY_SHOCK_WINDOW_EXIT";

    private readonly Tmg022VolatilityShockWindowExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg022VolatilityShockWindowExitStrategy(Tmg022VolatilityShockWindowExitConfig? config = null)
    {
        _config = config ?? Tmg022VolatilityShockWindowExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteMoves: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteMoves: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var absoluteMovePct = Math.Abs(context.MarkPrice - previousMark) / previousMark;
        var windowBars = Math.Max(1, _config.WindowBars);
        var updatedMoves = state.RecentAbsoluteMoves.Count > 0
            ? state.RecentAbsoluteMoves.ToList()
            : [];
        updatedMoves.Add(absoluteMovePct);
        if (updatedMoves.Count > windowBars)
        {
            updatedMoves.RemoveAt(0);
        }

        var shockMoveSumPct = updatedMoves.Sum();
        var shocked = updatedMoves.Count >= windowBars
            && shockMoveSumPct >= Math.Max(0.0, _config.ShockMoveSumPct);
        if (!shocked)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteMoves = updatedMoves
            };
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * Math.Abs(context.PositionQuantity)
            : (entry - context.MarkPrice) * Math.Abs(context.PositionQuantity);
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteMoves = updatedMoves
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentAbsoluteMoves = updatedMoves,
            Triggered = true
        };

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:shock-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:shock-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentAbsoluteMoves,
        bool Triggered
    );
}

public sealed class Tmg023ProfitReversionFailsafeExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_023_PROFIT_REVERSION_FAILSAFE_EXIT";

    private readonly Tmg023ProfitReversionFailsafeExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg023ProfitReversionFailsafeExitStrategy(Tmg023ProfitReversionFailsafeExitConfig? config = null)
    {
        _config = config ?? Tmg023ProfitReversionFailsafeExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, PeakFavorableProfitPct: 0.0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, PeakFavorableProfitPct: 0.0, Triggered: false);
        }

        if (state.Triggered)
        {
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var positionQty = Math.Abs(context.PositionQuantity);
        var favorableProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) / entry
            : (entry - context.MarkPrice) / entry;
        var peakFavorableProfitPct = Math.Max(state.PeakFavorableProfitPct, favorableProfitPct);
        _stateBySymbol[symbol] = state with { PeakFavorableProfitPct = peakFavorableProfitPct };

        var activationProfitPct = Math.Max(0.0, _config.ActivationProfitPct);
        var reversionProfitFloorPct = Math.Max(0.0, _config.ReversionProfitFloorPct);
        var activated = peakFavorableProfitPct >= activationProfitPct;
        var reverted = favorableProfitPct <= reversionProfitFloorPct;
        if (!activated || !reverted)
        {
            return [];
        }

        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * positionQty
            : (entry - context.MarkPrice) * positionQty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            PeakFavorableProfitPct = peakFavorableProfitPct,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:reversion-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: positionQty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:reversion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double PeakFavorableProfitPct,
        bool Triggered
    );
}

public sealed class Tmg024RangeCompressionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_024_RANGE_COMPRESSION_EXIT";

    private readonly Tmg024RangeCompressionExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg024RangeCompressionExitStrategy(Tmg024RangeCompressionExitConfig? config = null)
    {
        _config = config ?? Tmg024RangeCompressionExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, RecentMarks: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, RecentMarks: [], Triggered: false);
        }

        if (state.Triggered)
        {
            return [];
        }

        var windowBars = Math.Max(1, _config.WindowBars);
        var marks = state.RecentMarks.Count > 0
            ? state.RecentMarks.ToList()
            : [];
        marks.Add(context.MarkPrice);
        if (marks.Count > windowBars)
        {
            marks.RemoveAt(0);
        }

        _stateBySymbol[symbol] = state with { RecentMarks = marks };
        if (marks.Count < windowBars)
        {
            return [];
        }

        var minMark = marks.Min();
        var maxMark = marks.Max();
        var baseMark = Math.Max(1e-9, minMark);
        var rangePct = (maxMark - minMark) / baseMark;
        if (rangePct > Math.Max(0.0, _config.MaxRangePct))
        {
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var qty = Math.Abs(context.PositionQuantity);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            RecentMarks = marks,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:compression-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:compression-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        IReadOnlyList<double> RecentMarks,
        bool Triggered
    );
}

public sealed class Tmg025RollingVolatilityFloorExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_025_ROLLING_VOLATILITY_FLOOR_EXIT";

    private readonly Tmg025RollingVolatilityFloorExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg025RollingVolatilityFloorExitStrategy(Tmg025RollingVolatilityFloorExitConfig? config = null)
    {
        _config = config ?? Tmg025RollingVolatilityFloorExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteReturns: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteReturns: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var absoluteReturn = Math.Abs(context.MarkPrice - previousMark) / previousMark;
        var windowBars = Math.Max(1, _config.WindowBars);
        var recentReturns = state.RecentAbsoluteReturns.Count > 0
            ? state.RecentAbsoluteReturns.ToList()
            : [];
        recentReturns.Add(absoluteReturn);
        if (recentReturns.Count > windowBars)
        {
            recentReturns.RemoveAt(0);
        }

        if (recentReturns.Count < windowBars)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        var mean = recentReturns.Average();
        var variance = recentReturns.Sum(value => Math.Pow(value - mean, 2.0)) / recentReturns.Count;
        var realizedVolPct = Math.Sqrt(Math.Max(0.0, variance));
        if (realizedVolPct > Math.Max(0.0, _config.MaxRealizedVolPct))
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        var qty = Math.Abs(context.PositionQuantity);
        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentAbsoluteReturns = recentReturns,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:vol-floor-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:vol-floor-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentAbsoluteReturns,
        bool Triggered
    );
}

public sealed class Tmg026ChopAdverseExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_026_CHOP_ADVERSE_EXIT";

    private readonly Tmg026ChopAdverseExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg026ChopAdverseExitStrategy(Tmg026ChopAdverseExitConfig? config = null)
    {
        _config = config ?? Tmg026ChopAdverseExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var signedMovePct = (context.MarkPrice - previousMark) / previousMark;
        var windowBars = Math.Max(2, _config.WindowBars);
        var signedMoves = state.RecentSignedMovesPct.Count > 0
            ? state.RecentSignedMovesPct.ToList()
            : [];
        signedMoves.Add(signedMovePct);
        if (signedMoves.Count > windowBars)
        {
            signedMoves.RemoveAt(0);
        }

        if (signedMoves.Count < windowBars)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = signedMoves
            };
            return [];
        }

        var alternations = 0;
        for (var i = 1; i < signedMoves.Count; i++)
        {
            var prevSign = Math.Sign(signedMoves[i - 1]);
            var currSign = Math.Sign(signedMoves[i]);
            if (prevSign != 0 && currSign != 0 && prevSign != currSign)
            {
                alternations++;
            }
        }

        var adverseMoveSumPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? signedMoves.Where(move => move < 0).Sum(move => Math.Abs(move))
            : signedMoves.Where(move => move > 0).Sum(move => move);
        var hasChop = alternations >= Math.Max(1, _config.MinSignAlternations);
        var hasAdverseBias = adverseMoveSumPct >= Math.Max(0.0, _config.MinAdverseMoveSumPct);
        if (!hasChop || !hasAdverseBias)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = signedMoves
            };
            return [];
        }

        var qty = Math.Abs(context.PositionQuantity);
        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = signedMoves
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentSignedMovesPct = signedMoves,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:chop-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:chop-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentSignedMovesPct,
        bool Triggered
    );
}

public sealed class Tmg027TrendExhaustionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_027_TREND_EXHAUSTION_EXIT";

    private readonly Tmg027TrendExhaustionExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg027TrendExhaustionExitStrategy(Tmg027TrendExhaustionExitConfig? config = null)
    {
        _config = config ?? Tmg027TrendExhaustionExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var signedMovePct = (context.MarkPrice - previousMark) / previousMark;
        var maxWindow = Math.Max(2, _config.FavorableBarsLookback + _config.ReversalConfirmBars);
        var moves = state.RecentSignedMovesPct.Count > 0
            ? state.RecentSignedMovesPct.ToList()
            : [];
        moves.Add(signedMovePct);
        if (moves.Count > maxWindow)
        {
            moves.RemoveAt(0);
        }

        if (moves.Count < maxWindow)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = moves
            };
            return [];
        }

        var favorableLookback = Math.Max(1, _config.FavorableBarsLookback);
        var reversalConfirmBars = Math.Max(1, _config.ReversalConfirmBars);
        var favorableSegment = moves.Take(favorableLookback).ToList();
        var reversalSegment = moves.Skip(favorableLookback).Take(reversalConfirmBars).ToList();

        var favorableMoveSumPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? favorableSegment.Where(move => move > 0).Sum()
            : favorableSegment.Where(move => move < 0).Sum(move => Math.Abs(move));
        var reversalConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? reversalSegment.All(move => move < 0)
            : reversalSegment.All(move => move > 0);
        if (favorableMoveSumPct < Math.Max(0.0, _config.MinFavorableMovePct) || !reversalConfirmed)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = moves
            };
            return [];
        }

        var qty = Math.Abs(context.PositionQuantity);
        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = moves
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentSignedMovesPct = moves,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:exhaustion-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:exhaustion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentSignedMovesPct,
        bool Triggered
    );
}

public sealed class Eod001ForceFlatStrategy : IReplayEndOfDayStrategy
{
    public const string StrategyId = "EOD_001_FORCE_FLAT";

    private readonly Eod001ForceFlatConfig _config;
    private readonly HashSet<string> _flattenedBySymbolAndDate;

    public Eod001ForceFlatStrategy(Eod001ForceFlatConfig? config = null)
    {
        _config = config ?? Eod001ForceFlatConfig.Default;
        _flattenedBySymbolAndDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            return [];
        }

        var ts = context.TimestampUtc;
        var sessionCloseUtc = new DateTime(
            ts.Year,
            ts.Month,
            ts.Day,
            Math.Clamp(_config.SessionCloseHourUtc, 0, 23),
            Math.Clamp(_config.SessionCloseMinuteUtc, 0, 59),
            0,
            DateTimeKind.Utc);
        var triggerAtUtc = sessionCloseUtc.AddMinutes(-Math.Max(0, _config.FlattenLeadMinutes));
        if (ts < triggerAtUtc)
        {
            return [];
        }

        var key = $"{symbol}:{ts:yyyyMMdd}";
        if (_flattenedBySymbolAndDate.Contains(key))
        {
            return [];
        }

        _flattenedBySymbolAndDate.Add(key);

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = context.PositionQuantity > 0 ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: ts,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"end-of-day:{StrategyId}:cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: ts,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"end-of-day:{StrategyId}:flatten",
                Route: _config.FlattenRoute)
        ];
    }
}
