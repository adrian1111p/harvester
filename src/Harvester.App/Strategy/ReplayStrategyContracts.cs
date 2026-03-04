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

/// <summary>
/// Represents a normalized replay decision context for a single symbol and timestamp.
/// </summary>
public sealed record ReplayDayTradingContext(
    DateTime TimestampUtc,
    string Symbol,
    double MarkPrice,
    double BidPrice,
    double AskPrice,
    double PositionQuantity,
    double AveragePrice,
    double BarOpen = 0,
    double BarHigh = 0,
    double BarLow = 0,
    double BarClose = 0,
    double BarVolume = 0
);

/// <summary>
/// Represents overlay strategy output for the current replay slice.
/// </summary>
public sealed record ReplayDayTradingDecision(
    IReadOnlyList<ReplayOrderIntent> Orders,
    bool StopFurtherProcessing
);

/// <summary>
/// Defines an account-level or position-level safety overlay that can preempt normal strategy flow.
/// </summary>
public interface IReplayGlobalSafetyOverlayStrategy
{
    /// <summary>
    /// Evaluates the current context and returns orders and stop behavior.
    /// </summary>
    ReplayDayTradingDecision Evaluate(ReplayDayTradingContext context);
}

/// <summary>
/// Defines entry logic for scanner-selected symbols in replay mode.
/// </summary>
public interface IReplayEntryStrategy
{
    /// <summary>
    /// Produces new entry intents for the current context and scanner selection.
    /// </summary>
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context, ReplayScannerSymbolSelectionSnapshotRow selection);
}

/// <summary>
/// Defines post-entry trade management logic (stops, exits, scaling).
/// </summary>
public interface IReplayTradeManagementStrategy
{
    /// <summary>
    /// Produces management and exit intents for the current context.
    /// </summary>
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

public sealed record Tmg028ReversalAccelerationExitConfig(
    bool Enabled,
    int ReversalBars,
    double MinAdverseMovePct,
    bool RequireAcceleration,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg028ReversalAccelerationExitConfig Default { get; } = new(
        Enabled: false,
        ReversalBars: 2,
        MinAdverseMovePct: 0.0015,
        RequireAcceleration: true,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg029SustainedReversionExitConfig(
    bool Enabled,
    double MinPeakProfitPct,
    int ConsecutiveAdverseBars,
    double MinAdverseMoveSumPct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg029SustainedReversionExitConfig Default { get; } = new(
        Enabled: false,
        MinPeakProfitPct: 0.003,
        ConsecutiveAdverseBars: 3,
        MinAdverseMoveSumPct: 0.002,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg030RecoveryFailureExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int RecoveryBars,
    double MaxRecoveryMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg030RecoveryFailureExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.002,
        RecoveryBars: 1,
        MaxRecoveryMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg031ReboundStallExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int StallBars,
    double MaxAbsoluteStallMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg031ReboundStallExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        StallBars: 2,
        MaxAbsoluteStallMovePct: 0.0004,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg032WeakBounceFailureExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int BounceBars,
    double MinBounceMovePct,
    bool RequireRenewedAdverseBar,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg032WeakBounceFailureExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        BounceBars: 1,
        MinBounceMovePct: 0.001,
        RequireRenewedAdverseBar: true,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg033ReboundRollunderExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int ReversalBars,
    double MinReversalMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg033ReboundRollunderExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        ReversalBars: 1,
        MinReversalMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg034PostReboundFadeExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int FadeBars,
    double MinFadeMovePct,
    double MinFadeRetracePctOfRebound,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg034PostReboundFadeExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        FadeBars: 1,
        MinFadeMovePct: 0.0008,
        MinFadeRetracePctOfRebound: 0.75,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg035ReboundRejectionAccelExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    double MinRejectionRetracePctOfRebound,
    double MinRejectionAccelerationRatio,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg035ReboundRejectionAccelExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        RejectionBars: 2,
        MinRejectionMovePct: 0.001,
        MinRejectionRetracePctOfRebound: 0.8,
        MinRejectionAccelerationRatio: 1.0,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg036RejectionStallBreakExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int StallBars,
    double MaxAbsoluteStallMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg036RejectionStallBreakExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        StallBars: 2,
        MaxAbsoluteStallMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg037RejectionReboundFailExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int FailReboundBars,
    double MaxFailReboundMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg037RejectionReboundFailExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        FailReboundBars: 1,
        MaxFailReboundMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg038RejectionContinuationConfirmExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int ConfirmationBars,
    double MinConfirmationMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg038RejectionContinuationConfirmExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        ConfirmationBars: 1,
        MinConfirmationMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg039DoubleRejectionWeakReboundExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int FirstRejectionBars,
    double MinFirstRejectionMovePct,
    int MicroReboundBars,
    double MaxMicroReboundMovePct,
    int SecondRejectionBars,
    double MinSecondRejectionMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg039DoubleRejectionWeakReboundExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        FirstRejectionBars: 1,
        MinFirstRejectionMovePct: 0.0008,
        MicroReboundBars: 1,
        MaxMicroReboundMovePct: 0.0005,
        SecondRejectionBars: 1,
        MinSecondRejectionMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg040DoubleReboundFailureExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int FirstReboundBars,
    double MinFirstReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int SecondReboundBars,
    double MaxSecondReboundMovePct,
    int FinalRejectionBars,
    double MinFinalRejectionMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg040DoubleReboundFailureExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        FirstReboundBars: 1,
        MinFirstReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        SecondReboundBars: 1,
        MaxSecondReboundMovePct: 0.0005,
        FinalRejectionBars: 1,
        MinFinalRejectionMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg041TripleStepBreakExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int FailedReboundBars,
    double MaxFailedReboundMovePct,
    int BreakdownBars,
    double MinBreakdownMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg041TripleStepBreakExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        FailedReboundBars: 1,
        MaxFailedReboundMovePct: 0.0005,
        BreakdownBars: 1,
        MinBreakdownMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg042ReboundPullbackFailExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MaxRecoveryMovePct,
    int BreakdownBars,
    double MinBreakdownMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg042ReboundPullbackFailExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MaxRecoveryMovePct: 0.0005,
        BreakdownBars: 1,
        MinBreakdownMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg043ReboundPullbackRejectionExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MinRecoveryMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    double MinRejectionRetracePctOfRecovery,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg043ReboundPullbackRejectionExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MinRecoveryMovePct: 0.0006,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        MinRejectionRetracePctOfRecovery: 0.8,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg044ReboundPullbackRejectionConfirmExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MinRecoveryMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int ConfirmationBars,
    double MinConfirmationMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg044ReboundPullbackRejectionConfirmExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MinRecoveryMovePct: 0.0006,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        ConfirmationBars: 1,
        MinConfirmationMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg045ReboundPullbackRejectionConfirmFailReboundExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MinRecoveryMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int ConfirmationBars,
    double MinConfirmationMovePct,
    int FailReboundBars,
    double MaxFailReboundMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg045ReboundPullbackRejectionConfirmFailReboundExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MinRecoveryMovePct: 0.0006,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        ConfirmationBars: 1,
        MinConfirmationMovePct: 0.0005,
        FailReboundBars: 1,
        MaxFailReboundMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg046ReboundPullbackRejectionConfirmFailReboundBreakdownExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MinRecoveryMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int ConfirmationBars,
    double MinConfirmationMovePct,
    int FailReboundBars,
    double MaxFailReboundMovePct,
    int BreakdownBars,
    double MinBreakdownMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg046ReboundPullbackRejectionConfirmFailReboundBreakdownExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MinRecoveryMovePct: 0.0006,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        ConfirmationBars: 1,
        MinConfirmationMovePct: 0.0005,
        FailReboundBars: 1,
        MaxFailReboundMovePct: 0.0005,
        BreakdownBars: 1,
        MinBreakdownMovePct: 0.0008,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig(
    bool Enabled,
    int AdverseBarsLookback,
    double MinAdverseMovePct,
    int ReboundBars,
    double MinReboundMovePct,
    int PullbackBars,
    double MinPullbackMovePct,
    int RecoveryBars,
    double MinRecoveryMovePct,
    int RejectionBars,
    double MinRejectionMovePct,
    int ConfirmationBars,
    double MinConfirmationMovePct,
    int FailReboundBars,
    double MaxFailReboundMovePct,
    int BreakdownBars,
    double MinBreakdownMovePct,
    int BreakdownConfirmBars,
    double MinBreakdownConfirmMovePct,
    bool RequireAdverseUnrealized,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig Default { get; } = new(
        Enabled: false,
        AdverseBarsLookback: 2,
        MinAdverseMovePct: 0.0015,
        ReboundBars: 1,
        MinReboundMovePct: 0.0008,
        PullbackBars: 1,
        MinPullbackMovePct: 0.0008,
        RecoveryBars: 1,
        MinRecoveryMovePct: 0.0006,
        RejectionBars: 1,
        MinRejectionMovePct: 0.0008,
        ConfirmationBars: 1,
        MinConfirmationMovePct: 0.0005,
        FailReboundBars: 1,
        MaxFailReboundMovePct: 0.0005,
        BreakdownBars: 1,
        MinBreakdownMovePct: 0.0008,
        BreakdownConfirmBars: 1,
        MinBreakdownConfirmMovePct: 0.0005,
        RequireAdverseUnrealized: true,
        FlattenRoute: "SMART",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg048MtfCandleReversalExitConfig(
    bool Enabled,
    bool RequireAllTimeframes,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg048MtfCandleReversalExitConfig Default { get; } = new(
        Enabled: false,
        RequireAllTimeframes: true,
        FlattenRoute: "MARKET",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

public sealed record Tmg049MtfRegimeAtrExitConfig(
    bool Enabled,
    bool RequireAllTimeframes,
    int AtrLookbackBars,
    double AtrStopMultiple,
    bool RegimeExitRequiresOppositeAlignment,
    string FlattenRoute,
    string FlattenTif,
    string FlattenOrderType
)
{
    public static Tmg049MtfRegimeAtrExitConfig Default { get; } = new(
        Enabled: false,
        RequireAllTimeframes: true,
        AtrLookbackBars: 14,
        AtrStopMultiple: 2.0,
        RegimeExitRequiresOppositeAlignment: true,
        FlattenRoute: "MARKET",
        FlattenTif: "DAY",
        FlattenOrderType: "MARKET");
}

/// <summary>
/// Defines session close handling logic for replay mode.
/// </summary>
public interface IReplayEndOfDayStrategy
{
    /// <summary>
    /// Produces close-of-day orders for the current context.
    /// </summary>
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

