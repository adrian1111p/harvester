namespace Harvester.App.Strategy;

/// <summary>
/// Centralizes construction of all Tmg trade management strategies.
/// Reads configuration from environment variables, instantiates each strategy,
/// and returns them as an array for use in <see cref="ReplayDayTradingPipeline"/>.
/// </summary>
internal static class TmgStrategyFactory
{
    /// <summary>
    /// Build all 49 Tmg trade management strategies from environment configuration.
    /// Two strategies (Tmg048, Tmg049) require a shared MTF signal engine instance.
    /// </summary>
    public static IReplayTradeManagementStrategy[] CreateAll(ReplayMtfCandleSignalEngine mtfEngine)
        => CreateAll(mtfEngine, enableAll: TryReadEnvironmentBool("TMG_ENABLE_ALL", false));

    /// <summary>
    /// Build all 49 Tmg strategies. When <paramref name="enableAll"/> is true, every strategy
    /// is force-enabled regardless of its individual TMG_NNN_ENABLED env var — useful for
    /// comparative backtesting across all exit strategies.
    /// </summary>
    public static IReplayTradeManagementStrategy[] CreateAll(ReplayMtfCandleSignalEngine mtfEngine, bool enableAll)
    {
        // When enableAll=true, pass true as the fallback default to each TMG_NNN_ENABLED check
        // so all strategies are force-enabled even without individual env vars set.
        var fallback = enableAll;

        return [
            new Tmg001BracketExitStrategy(BuildTmg001Config()),   // Tmg001 always defaults to true
            new Tmg002BreakEvenEscalationStrategy(BuildTmg002Config(fallback)),
            new Tmg003TrailingProgressionStrategy(BuildTmg003Config(fallback)),
            new Tmg004PartialTakeProfitRunnerTrailStrategy(BuildTmg004Config(fallback)),
            new Tmg005TimeStopStrategy(BuildTmg005Config(fallback)),
            new Tmg006VolatilityAdaptiveExitStrategy(BuildTmg006Config(fallback)),
            new Tmg007DrawdownDeriskStrategy(BuildTmg007Config(fallback)),
            new Tmg008SessionVwapReversionExitStrategy(BuildTmg008Config(fallback)),
            new Tmg009LiquiditySpreadExitStrategy(BuildTmg009Config(fallback)),
            new Tmg010EventRiskCooldownGuardStrategy(BuildTmg010Config(fallback)),
            new Tmg011StallExitGuardStrategy(BuildTmg011Config(fallback)),
            new Tmg012PnlCapExitStrategy(BuildTmg012Config(fallback)),
            new Tmg013SpreadPersistenceExitStrategy(BuildTmg013Config(fallback)),
            new Tmg014GapRiskExitStrategy(BuildTmg014Config(fallback)),
            new Tmg015AdverseDriftExitStrategy(BuildTmg015Config(fallback)),
            new Tmg016PeakPullbackExitStrategy(BuildTmg016Config(fallback)),
            new Tmg017MicrostructureStressExitStrategy(BuildTmg017Config(fallback)),
            new Tmg018StaleFavorableMoveExitStrategy(BuildTmg018Config(fallback)),
            new Tmg019RollingAdverseWindowExitStrategy(BuildTmg019Config(fallback)),
            new Tmg020UnderperformanceTimeoutExitStrategy(BuildTmg020Config(fallback)),
            new Tmg021QuotePressureExitStrategy(BuildTmg021Config(fallback)),
            new Tmg022VolatilityShockWindowExitStrategy(BuildTmg022Config(fallback)),
            new Tmg023ProfitReversionFailsafeExitStrategy(BuildTmg023Config(fallback)),
            new Tmg024RangeCompressionExitStrategy(BuildTmg024Config(fallback)),
            new Tmg025RollingVolatilityFloorExitStrategy(BuildTmg025Config(fallback)),
            new Tmg026ChopAdverseExitStrategy(BuildTmg026Config(fallback)),
            new Tmg027TrendExhaustionExitStrategy(BuildTmg027Config(fallback)),
            new Tmg028ReversalAccelerationExitStrategy(BuildTmg028Config(fallback)),
            new Tmg029SustainedReversionExitStrategy(BuildTmg029Config(fallback)),
            new Tmg030RecoveryFailureExitStrategy(BuildTmg030Config(fallback)),
            new Tmg031ReboundStallExitStrategy(BuildTmg031Config(fallback)),
            new Tmg032WeakBounceFailureExitStrategy(BuildTmg032Config(fallback)),
            new Tmg033ReboundRollunderExitStrategy(BuildTmg033Config(fallback)),
            new Tmg034PostReboundFadeExitStrategy(BuildTmg034Config(fallback)),
            new Tmg035ReboundRejectionAccelExitStrategy(BuildTmg035Config(fallback)),
            new Tmg036RejectionStallBreakExitStrategy(BuildTmg036Config(fallback)),
            new Tmg037RejectionReboundFailExitStrategy(BuildTmg037Config(fallback)),
            new Tmg038RejectionContinuationConfirmExitStrategy(BuildTmg038Config(fallback)),
            new Tmg039DoubleRejectionWeakReboundExitStrategy(BuildTmg039Config(fallback)),
            new Tmg040DoubleReboundFailureExitStrategy(BuildTmg040Config(fallback)),
            new Tmg041TripleStepBreakExitStrategy(BuildTmg041Config(fallback)),
            new Tmg042ReboundPullbackFailExitStrategy(BuildTmg042Config(fallback)),
            new Tmg043ReboundPullbackRejectionExitStrategy(BuildTmg043Config(fallback)),
            new Tmg044ReboundPullbackRejectionConfirmExitStrategy(BuildTmg044Config(fallback)),
            new Tmg045ReboundPullbackRejectionConfirmFailReboundExitStrategy(BuildTmg045Config(fallback)),
            new Tmg046ReboundPullbackRejectionConfirmFailReboundBreakdownExitStrategy(BuildTmg046Config(fallback)),
            new Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitStrategy(BuildTmg047Config(fallback)),
            new Tmg048MtfCandleReversalExitStrategy(mtfEngine, BuildTmg048Config(fallback)),
            new Tmg049MtfRegimeAtrExitStrategy(mtfEngine, BuildTmg049Config(fallback)),
        ];
    }

    // ─────────────────────────────────────────────────────────────────
    // CONFIG BUILDERS
    // ─────────────────────────────────────────────────────────────────

    private static Tmg001BracketConfig BuildTmg001Config()
    {
        return new Tmg001BracketConfig(
            Enabled: TryReadEnvironmentBool("TMG_001_ENABLED", true),
            StopLossPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_001_STOP_LOSS_PCT", 0.003)),
            TakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_001_TAKE_PROFIT_PCT", 0.006)),
            TimeInForce: TryReadEnvironmentString("TMG_001_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg002BreakEvenConfig BuildTmg002Config(bool enabledFallback = false)
    {
        return new Tmg002BreakEvenConfig(
            Enabled: TryReadEnvironmentBool("TMG_002_ENABLED", enabledFallback),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_002_TRIGGER_PROFIT_PCT", 0.003)),
            StopOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_002_STOP_OFFSET_PCT", 0.0)),
            TimeInForce: TryReadEnvironmentString("TMG_002_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg003TrailingProgressionConfig BuildTmg003Config(bool enabledFallback = false)
    {
        return new Tmg003TrailingProgressionConfig(
            Enabled: TryReadEnvironmentBool("TMG_003_ENABLED", enabledFallback),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_003_TRIGGER_PROFIT_PCT", 0.006)),
            TrailOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_003_TRAIL_OFFSET_PCT", 0.002)),
            TimeInForce: TryReadEnvironmentString("TMG_003_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg004PartialTakeProfitRunnerTrailConfig BuildTmg004Config(bool enabledFallback = false)
    {
        return new Tmg004PartialTakeProfitRunnerTrailConfig(
            Enabled: TryReadEnvironmentBool("TMG_004_ENABLED", enabledFallback),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_TRIGGER_PROFIT_PCT", 0.008)),
            TakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_TAKE_PROFIT_PCT", 0.008)),
            TakeProfitFraction: Math.Clamp(TryReadEnvironmentDouble("TMG_004_TAKE_PROFIT_FRACTION", 0.5), 0.0, 1.0),
            RunnerTrailOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_RUNNER_TRAIL_OFFSET_PCT", 0.002)),
            TimeInForce: TryReadEnvironmentString("TMG_004_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg005TimeStopConfig BuildTmg005Config(bool enabledFallback = false)
    {
        return new Tmg005TimeStopConfig(
            Enabled: TryReadEnvironmentBool("TMG_005_ENABLED", enabledFallback),
            MaxHoldingBars: Math.Max(0, TryReadEnvironmentInt("TMG_005_MAX_HOLDING_BARS", 30)),
            MaxHoldingMinutes: Math.Max(0, TryReadEnvironmentInt("TMG_005_MAX_HOLDING_MINUTES", 120)),
            FlattenRoute: TryReadEnvironmentString("TMG_005_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_005_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_005_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg006VolatilityAdaptiveExitConfig BuildTmg006Config(bool enabledFallback = false)
    {
        return new Tmg006VolatilityAdaptiveExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_006_ENABLED", enabledFallback),
            LowVolThresholdPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_LOW_VOL_THRESHOLD_PCT", 0.002)),
            HighVolThresholdPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_HIGH_VOL_THRESHOLD_PCT", 0.006)),
            LowStopLossPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_LOW_STOP_LOSS_PCT", 0.002)),
            LowTakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_LOW_TAKE_PROFIT_PCT", 0.004)),
            MidStopLossPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_MID_STOP_LOSS_PCT", 0.003)),
            MidTakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_MID_TAKE_PROFIT_PCT", 0.006)),
            HighStopLossPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_HIGH_STOP_LOSS_PCT", 0.004)),
            HighTakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_006_HIGH_TAKE_PROFIT_PCT", 0.010)),
            TimeInForce: TryReadEnvironmentString("TMG_006_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg007DrawdownDeriskConfig BuildTmg007Config(bool enabledFallback = false)
    {
        return new Tmg007DrawdownDeriskConfig(
            Enabled: TryReadEnvironmentBool("TMG_007_ENABLED", enabledFallback),
            DeriskDrawdownPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_007_DERISK_DRAWDOWN_PCT", 0.003)),
            FlattenDrawdownPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_007_FLATTEN_DRAWDOWN_PCT", 0.006)),
            DeriskFraction: Math.Clamp(TryReadEnvironmentDouble("TMG_007_DERISK_FRACTION", 0.5), 0.0, 1.0),
            FlattenRoute: TryReadEnvironmentString("TMG_007_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_007_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_007_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg008SessionVwapReversionConfig BuildTmg008Config(bool enabledFallback = false)
    {
        return new Tmg008SessionVwapReversionConfig(
            Enabled: TryReadEnvironmentBool("TMG_008_ENABLED", enabledFallback),
            MinSamples: Math.Max(1, TryReadEnvironmentInt("TMG_008_MIN_SAMPLES", 5)),
            AdverseDeviationPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_008_ADVERSE_DEVIATION_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_008_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_008_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_008_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg009LiquiditySpreadExitConfig BuildTmg009Config(bool enabledFallback = false)
    {
        return new Tmg009LiquiditySpreadExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_009_ENABLED", enabledFallback),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_009_SPREAD_TRIGGER_PCT", 0.003)),
            RequireUnrealizedLoss: TryReadEnvironmentBool("TMG_009_REQUIRE_UNREALIZED_LOSS", true),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_009_MIN_ADVERSE_MOVE_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_009_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_009_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_009_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg010EventRiskCooldownConfig BuildTmg010Config(bool enabledFallback = false)
    {
        return new Tmg010EventRiskCooldownConfig(
            Enabled: TryReadEnvironmentBool("TMG_010_ENABLED", enabledFallback),
            ShockMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_010_SHOCK_MOVE_PCT", 0.015)),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_010_SPREAD_TRIGGER_PCT", 0.010)),
            CooldownBars: Math.Max(0, TryReadEnvironmentInt("TMG_010_COOLDOWN_BARS", 5)),
            FlattenRoute: TryReadEnvironmentString("TMG_010_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_010_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_010_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg011StallExitConfig BuildTmg011Config(bool enabledFallback = false)
    {
        return new Tmg011StallExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_011_ENABLED", enabledFallback),
            MinHoldingBars: Math.Max(0, TryReadEnvironmentInt("TMG_011_MIN_HOLDING_BARS", 10)),
            MaxAbsoluteMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_011_MAX_ABSOLUTE_MOVE_PCT", 0.0015)),
            FlattenRoute: TryReadEnvironmentString("TMG_011_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_011_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_011_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg012PnlCapExitConfig BuildTmg012Config(bool enabledFallback = false)
    {
        return new Tmg012PnlCapExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_012_ENABLED", enabledFallback),
            StopLossUsd: Math.Max(0.0, TryReadEnvironmentDouble("TMG_012_STOP_LOSS_USD", 20.0)),
            TakeProfitUsd: Math.Max(0.0, TryReadEnvironmentDouble("TMG_012_TAKE_PROFIT_USD", 40.0)),
            FlattenRoute: TryReadEnvironmentString("TMG_012_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_012_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_012_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg013SpreadPersistenceExitConfig BuildTmg013Config(bool enabledFallback = false)
    {
        return new Tmg013SpreadPersistenceExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_013_ENABLED", enabledFallback),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_013_SPREAD_TRIGGER_PCT", 0.004)),
            MinConsecutiveBars: Math.Max(1, TryReadEnvironmentInt("TMG_013_MIN_CONSECUTIVE_BARS", 3)),
            FlattenRoute: TryReadEnvironmentString("TMG_013_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_013_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_013_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg014GapRiskExitConfig BuildTmg014Config(bool enabledFallback = false)
    {
        return new Tmg014GapRiskExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_014_ENABLED", enabledFallback),
            GapMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_014_GAP_MOVE_PCT", 0.01)),
            RequireAdverseDirection: TryReadEnvironmentBool("TMG_014_REQUIRE_ADVERSE_DIRECTION", true),
            FlattenRoute: TryReadEnvironmentString("TMG_014_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_014_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_014_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg015AdverseDriftExitConfig BuildTmg015Config(bool enabledFallback = false)
    {
        return new Tmg015AdverseDriftExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_015_ENABLED", enabledFallback),
            MinConsecutiveAdverseBars: Math.Max(1, TryReadEnvironmentInt("TMG_015_MIN_CONSECUTIVE_ADVERSE_BARS", 3)),
            MinCumulativeAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_015_MIN_CUMULATIVE_ADVERSE_MOVE_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_015_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_015_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_015_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg016PeakPullbackExitConfig BuildTmg016Config(bool enabledFallback = false)
    {
        return new Tmg016PeakPullbackExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_016_ENABLED", enabledFallback),
            ActivationProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_016_ACTIVATION_PROFIT_PCT", 0.004)),
            PullbackFromPeakPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_016_PULLBACK_FROM_PEAK_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_016_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_016_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_016_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg017MicrostructureStressExitConfig BuildTmg017Config(bool enabledFallback = false)
    {
        return new Tmg017MicrostructureStressExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_017_ENABLED", enabledFallback),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_017_SPREAD_TRIGGER_PCT", 0.003)),
            MidDislocationPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_017_MID_DISLOCATION_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_017_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_017_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_017_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_017_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg018StaleFavorableMoveExitConfig BuildTmg018Config(bool enabledFallback = false)
    {
        return new Tmg018StaleFavorableMoveExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_018_ENABLED", enabledFallback),
            MaxBarsWithoutFavorableExtension: Math.Max(0, TryReadEnvironmentInt("TMG_018_MAX_BARS_WITHOUT_FAVORABLE_EXTENSION", 10)),
            MinOpenProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_018_MIN_OPEN_PROFIT_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_018_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_018_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_018_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg019RollingAdverseWindowExitConfig BuildTmg019Config(bool enabledFallback = false)
    {
        return new Tmg019RollingAdverseWindowExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_019_ENABLED", enabledFallback),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_019_WINDOW_BARS", 5)),
            AdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_019_ADVERSE_MOVE_SUM_PCT", 0.003)),
            FlattenRoute: TryReadEnvironmentString("TMG_019_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_019_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_019_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg020UnderperformanceTimeoutExitConfig BuildTmg020Config(bool enabledFallback = false)
    {
        return new Tmg020UnderperformanceTimeoutExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_020_ENABLED", enabledFallback),
            MaxBarsToReachMinProfit: Math.Max(0, TryReadEnvironmentInt("TMG_020_MAX_BARS_TO_REACH_MIN_PROFIT", 20)),
            MinProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_020_MIN_PROFIT_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_020_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_020_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_020_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg021QuotePressureExitConfig BuildTmg021Config(bool enabledFallback = false)
    {
        return new Tmg021QuotePressureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_021_ENABLED", enabledFallback),
            MinPressurePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_021_MIN_PRESSURE_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_021_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_021_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_021_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_021_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg022VolatilityShockWindowExitConfig BuildTmg022Config(bool enabledFallback = false)
    {
        return new Tmg022VolatilityShockWindowExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_022_ENABLED", enabledFallback),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_022_WINDOW_BARS", 3)),
            ShockMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_022_SHOCK_MOVE_SUM_PCT", 0.01)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_022_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_022_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_022_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_022_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg023ProfitReversionFailsafeExitConfig BuildTmg023Config(bool enabledFallback = false)
    {
        return new Tmg023ProfitReversionFailsafeExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_023_ENABLED", enabledFallback),
            ActivationProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_023_ACTIVATION_PROFIT_PCT", 0.004)),
            ReversionProfitFloorPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_023_REVERSION_PROFIT_FLOOR_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_023_REQUIRE_ADVERSE_UNREALIZED", false),
            FlattenRoute: TryReadEnvironmentString("TMG_023_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_023_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_023_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg024RangeCompressionExitConfig BuildTmg024Config(bool enabledFallback = false)
    {
        return new Tmg024RangeCompressionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_024_ENABLED", enabledFallback),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_024_WINDOW_BARS", 5)),
            MaxRangePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_024_MAX_RANGE_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_024_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_024_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_024_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_024_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg025RollingVolatilityFloorExitConfig BuildTmg025Config(bool enabledFallback = false)
    {
        return new Tmg025RollingVolatilityFloorExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_025_ENABLED", enabledFallback),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_025_WINDOW_BARS", 5)),
            MaxRealizedVolPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_025_MAX_REALIZED_VOL_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_025_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_025_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_025_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_025_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg026ChopAdverseExitConfig BuildTmg026Config(bool enabledFallback = false)
    {
        return new Tmg026ChopAdverseExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_026_ENABLED", enabledFallback),
            WindowBars: Math.Max(2, TryReadEnvironmentInt("TMG_026_WINDOW_BARS", 6)),
            MinSignAlternations: Math.Max(1, TryReadEnvironmentInt("TMG_026_MIN_SIGN_ALTERNATIONS", 4)),
            MinAdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_026_MIN_ADVERSE_MOVE_SUM_PCT", 0.002)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_026_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_026_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_026_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_026_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg027TrendExhaustionExitConfig BuildTmg027Config(bool enabledFallback = false)
    {
        return new Tmg027TrendExhaustionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_027_ENABLED", enabledFallback),
            FavorableBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_027_FAVORABLE_BARS_LOOKBACK", 4)),
            MinFavorableMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_027_MIN_FAVORABLE_MOVE_PCT", 0.002)),
            ReversalConfirmBars: Math.Max(1, TryReadEnvironmentInt("TMG_027_REVERSAL_CONFIRM_BARS", 2)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_027_REQUIRE_ADVERSE_UNREALIZED", false),
            FlattenRoute: TryReadEnvironmentString("TMG_027_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_027_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_027_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg028ReversalAccelerationExitConfig BuildTmg028Config(bool enabledFallback = false)
    {
        return new Tmg028ReversalAccelerationExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_028_ENABLED", enabledFallback),
            ReversalBars: Math.Max(2, TryReadEnvironmentInt("TMG_028_REVERSAL_BARS", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_028_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            RequireAcceleration: TryReadEnvironmentBool("TMG_028_REQUIRE_ACCELERATION", true),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_028_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_028_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_028_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_028_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg029SustainedReversionExitConfig BuildTmg029Config(bool enabledFallback = false)
    {
        return new Tmg029SustainedReversionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_029_ENABLED", enabledFallback),
            MinPeakProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_029_MIN_PEAK_PROFIT_PCT", 0.003)),
            ConsecutiveAdverseBars: Math.Max(1, TryReadEnvironmentInt("TMG_029_CONSECUTIVE_ADVERSE_BARS", 3)),
            MinAdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_029_MIN_ADVERSE_MOVE_SUM_PCT", 0.002)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_029_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_029_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_029_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_029_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg030RecoveryFailureExitConfig BuildTmg030Config(bool enabledFallback = false)
    {
        return new Tmg030RecoveryFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_030_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_030_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_030_MIN_ADVERSE_MOVE_PCT", 0.002)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_030_RECOVERY_BARS", 1)),
            MaxRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_030_MAX_RECOVERY_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_030_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_030_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_030_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_030_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg031ReboundStallExitConfig BuildTmg031Config(bool enabledFallback = false)
    {
        return new Tmg031ReboundStallExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_031_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_031_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_031_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            StallBars: Math.Max(1, TryReadEnvironmentInt("TMG_031_STALL_BARS", 2)),
            MaxAbsoluteStallMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_031_MAX_ABSOLUTE_STALL_MOVE_PCT", 0.0004)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_031_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_031_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_031_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_031_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg032WeakBounceFailureExitConfig BuildTmg032Config(bool enabledFallback = false)
    {
        return new Tmg032WeakBounceFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_032_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_032_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_032_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            BounceBars: Math.Max(1, TryReadEnvironmentInt("TMG_032_BOUNCE_BARS", 1)),
            MinBounceMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_032_MIN_BOUNCE_MOVE_PCT", 0.001)),
            RequireRenewedAdverseBar: TryReadEnvironmentBool("TMG_032_REQUIRE_RENEWED_ADVERSE_BAR", true),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_032_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_032_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_032_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_032_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg033ReboundRollunderExitConfig BuildTmg033Config(bool enabledFallback = false)
    {
        return new Tmg033ReboundRollunderExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_033_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_033_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_033_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_033_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_033_MIN_REBOUND_MOVE_PCT", 0.0008)),
            ReversalBars: Math.Max(1, TryReadEnvironmentInt("TMG_033_REVERSAL_BARS", 1)),
            MinReversalMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_033_MIN_REVERSAL_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_033_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_033_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_033_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_033_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg034PostReboundFadeExitConfig BuildTmg034Config(bool enabledFallback = false)
    {
        return new Tmg034PostReboundFadeExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_034_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_034_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_034_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_034_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_034_MIN_REBOUND_MOVE_PCT", 0.0008)),
            FadeBars: Math.Max(1, TryReadEnvironmentInt("TMG_034_FADE_BARS", 1)),
            MinFadeMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_034_MIN_FADE_MOVE_PCT", 0.0008)),
            MinFadeRetracePctOfRebound: Math.Max(0.0, TryReadEnvironmentDouble("TMG_034_MIN_FADE_RETRACE_PCT_OF_REBOUND", 0.75)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_034_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_034_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_034_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_034_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg035ReboundRejectionAccelExitConfig BuildTmg035Config(bool enabledFallback = false)
    {
        return new Tmg035ReboundRejectionAccelExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_035_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_035_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_035_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_035_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_035_MIN_REBOUND_MOVE_PCT", 0.0008)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_035_REJECTION_BARS", 2)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_035_MIN_REJECTION_MOVE_PCT", 0.001)),
            MinRejectionRetracePctOfRebound: Math.Max(0.0, TryReadEnvironmentDouble("TMG_035_MIN_REJECTION_RETRACE_PCT_OF_REBOUND", 0.8)),
            MinRejectionAccelerationRatio: Math.Max(0.0, TryReadEnvironmentDouble("TMG_035_MIN_REJECTION_ACCELERATION_RATIO", 1.0)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_035_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_035_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_035_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_035_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg036RejectionStallBreakExitConfig BuildTmg036Config(bool enabledFallback = false)
    {
        return new Tmg036RejectionStallBreakExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_036_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_036_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_036_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_036_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_036_MIN_REBOUND_MOVE_PCT", 0.0008)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_036_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_036_MIN_REJECTION_MOVE_PCT", 0.0008)),
            StallBars: Math.Max(1, TryReadEnvironmentInt("TMG_036_STALL_BARS", 2)),
            MaxAbsoluteStallMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_036_MAX_ABSOLUTE_STALL_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_036_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_036_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_036_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_036_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg037RejectionReboundFailExitConfig BuildTmg037Config(bool enabledFallback = false)
    {
        return new Tmg037RejectionReboundFailExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_037_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_037_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_037_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_037_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_037_MIN_REBOUND_MOVE_PCT", 0.0008)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_037_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_037_MIN_REJECTION_MOVE_PCT", 0.0008)),
            FailReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_037_FAIL_REBOUND_BARS", 1)),
            MaxFailReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_037_MAX_FAIL_REBOUND_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_037_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_037_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_037_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_037_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg038RejectionContinuationConfirmExitConfig BuildTmg038Config(bool enabledFallback = false)
    {
        return new Tmg038RejectionContinuationConfirmExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_038_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_038_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_038_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_038_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_038_MIN_REBOUND_MOVE_PCT", 0.0008)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_038_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_038_MIN_REJECTION_MOVE_PCT", 0.0008)),
            ConfirmationBars: Math.Max(1, TryReadEnvironmentInt("TMG_038_CONFIRMATION_BARS", 1)),
            MinConfirmationMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_038_MIN_CONFIRMATION_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_038_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_038_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_038_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_038_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg039DoubleRejectionWeakReboundExitConfig BuildTmg039Config(bool enabledFallback = false)
    {
        return new Tmg039DoubleRejectionWeakReboundExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_039_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_039_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_039_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_039_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_039_MIN_REBOUND_MOVE_PCT", 0.0008)),
            FirstRejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_039_FIRST_REJECTION_BARS", 1)),
            MinFirstRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_039_MIN_FIRST_REJECTION_MOVE_PCT", 0.0008)),
            MicroReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_039_MICRO_REBOUND_BARS", 1)),
            MaxMicroReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_039_MAX_MICRO_REBOUND_MOVE_PCT", 0.0005)),
            SecondRejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_039_SECOND_REJECTION_BARS", 1)),
            MinSecondRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_039_MIN_SECOND_REJECTION_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_039_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_039_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_039_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_039_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg040DoubleReboundFailureExitConfig BuildTmg040Config(bool enabledFallback = false)
    {
        return new Tmg040DoubleReboundFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_040_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_040_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_040_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            FirstReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_040_FIRST_REBOUND_BARS", 1)),
            MinFirstReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_040_MIN_FIRST_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_040_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_040_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            SecondReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_040_SECOND_REBOUND_BARS", 1)),
            MaxSecondReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_040_MAX_SECOND_REBOUND_MOVE_PCT", 0.0005)),
            FinalRejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_040_FINAL_REJECTION_BARS", 1)),
            MinFinalRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_040_MIN_FINAL_REJECTION_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_040_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_040_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_040_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_040_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg041TripleStepBreakExitConfig BuildTmg041Config(bool enabledFallback = false)
    {
        return new Tmg041TripleStepBreakExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_041_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_041_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_041_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_041_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_041_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_041_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_041_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            FailedReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_041_FAILED_REBOUND_BARS", 1)),
            MaxFailedReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_041_MAX_FAILED_REBOUND_MOVE_PCT", 0.0005)),
            BreakdownBars: Math.Max(1, TryReadEnvironmentInt("TMG_041_BREAKDOWN_BARS", 1)),
            MinBreakdownMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_041_MIN_BREAKDOWN_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_041_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_041_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_041_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_041_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg042ReboundPullbackFailExitConfig BuildTmg042Config(bool enabledFallback = false)
    {
        return new Tmg042ReboundPullbackFailExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_042_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_042_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_042_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_042_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_042_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_042_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_042_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_042_RECOVERY_BARS", 1)),
            MaxRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_042_MAX_RECOVERY_MOVE_PCT", 0.0005)),
            BreakdownBars: Math.Max(1, TryReadEnvironmentInt("TMG_042_BREAKDOWN_BARS", 1)),
            MinBreakdownMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_042_MIN_BREAKDOWN_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_042_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_042_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_042_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_042_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg043ReboundPullbackRejectionExitConfig BuildTmg043Config(bool enabledFallback = false)
    {
        return new Tmg043ReboundPullbackRejectionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_043_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_043_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_043_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_043_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_043_RECOVERY_BARS", 1)),
            MinRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_RECOVERY_MOVE_PCT", 0.0006)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_043_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_REJECTION_MOVE_PCT", 0.0008)),
            MinRejectionRetracePctOfRecovery: Math.Max(0.0, TryReadEnvironmentDouble("TMG_043_MIN_REJECTION_RETRACE_PCT_OF_RECOVERY", 0.8)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_043_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_043_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_043_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_043_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg044ReboundPullbackRejectionConfirmExitConfig BuildTmg044Config(bool enabledFallback = false)
    {
        return new Tmg044ReboundPullbackRejectionConfirmExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_044_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_044_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_044_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_044_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_044_RECOVERY_BARS", 1)),
            MinRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_RECOVERY_MOVE_PCT", 0.0006)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_044_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_REJECTION_MOVE_PCT", 0.0008)),
            ConfirmationBars: Math.Max(1, TryReadEnvironmentInt("TMG_044_CONFIRMATION_BARS", 1)),
            MinConfirmationMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_044_MIN_CONFIRMATION_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_044_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_044_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_044_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_044_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg045ReboundPullbackRejectionConfirmFailReboundExitConfig BuildTmg045Config(bool enabledFallback = false)
    {
        return new Tmg045ReboundPullbackRejectionConfirmFailReboundExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_045_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_045_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_RECOVERY_BARS", 1)),
            MinRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_RECOVERY_MOVE_PCT", 0.0006)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_REJECTION_MOVE_PCT", 0.0008)),
            ConfirmationBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_CONFIRMATION_BARS", 1)),
            MinConfirmationMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MIN_CONFIRMATION_MOVE_PCT", 0.0005)),
            FailReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_045_FAIL_REBOUND_BARS", 1)),
            MaxFailReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_045_MAX_FAIL_REBOUND_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_045_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_045_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_045_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_045_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg046ReboundPullbackRejectionConfirmFailReboundBreakdownExitConfig BuildTmg046Config(bool enabledFallback = false)
    {
        return new Tmg046ReboundPullbackRejectionConfirmFailReboundBreakdownExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_046_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_046_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_RECOVERY_BARS", 1)),
            MinRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_RECOVERY_MOVE_PCT", 0.0006)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_REJECTION_MOVE_PCT", 0.0008)),
            ConfirmationBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_CONFIRMATION_BARS", 1)),
            MinConfirmationMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_CONFIRMATION_MOVE_PCT", 0.0005)),
            FailReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_FAIL_REBOUND_BARS", 1)),
            MaxFailReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MAX_FAIL_REBOUND_MOVE_PCT", 0.0005)),
            BreakdownBars: Math.Max(1, TryReadEnvironmentInt("TMG_046_BREAKDOWN_BARS", 1)),
            MinBreakdownMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_046_MIN_BREAKDOWN_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_046_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_046_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_046_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_046_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig BuildTmg047Config(bool enabledFallback = false)
    {
        return new Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_047_ENABLED", enabledFallback),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_047_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            ReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_REBOUND_BARS", 1)),
            MinReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_REBOUND_MOVE_PCT", 0.0008)),
            PullbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_PULLBACK_BARS", 1)),
            MinPullbackMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_PULLBACK_MOVE_PCT", 0.0008)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_RECOVERY_BARS", 1)),
            MinRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_RECOVERY_MOVE_PCT", 0.0006)),
            RejectionBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_REJECTION_BARS", 1)),
            MinRejectionMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_REJECTION_MOVE_PCT", 0.0008)),
            ConfirmationBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_CONFIRMATION_BARS", 1)),
            MinConfirmationMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_CONFIRMATION_MOVE_PCT", 0.0005)),
            FailReboundBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_FAIL_REBOUND_BARS", 1)),
            MaxFailReboundMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MAX_FAIL_REBOUND_MOVE_PCT", 0.0005)),
            BreakdownBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_BREAKDOWN_BARS", 1)),
            MinBreakdownMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_BREAKDOWN_MOVE_PCT", 0.0008)),
            BreakdownConfirmBars: Math.Max(1, TryReadEnvironmentInt("TMG_047_BREAKDOWN_CONFIRM_BARS", 1)),
            MinBreakdownConfirmMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_047_MIN_BREAKDOWN_CONFIRM_MOVE_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_047_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_047_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_047_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_047_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg048MtfCandleReversalExitConfig BuildTmg048Config(bool enabledFallback = false)
    {
        return new Tmg048MtfCandleReversalExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_048_ENABLED", enabledFallback),
            RequireAllTimeframes: TryReadEnvironmentBool("TMG_048_REQUIRE_ALL_TIMEFRAMES", true),
            FlattenRoute: TryReadEnvironmentString("TMG_048_FLATTEN_ROUTE", "MARKET"),
            FlattenTif: TryReadEnvironmentString("TMG_048_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_048_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg049MtfRegimeAtrExitConfig BuildTmg049Config(bool enabledFallback = false)
    {
        return new Tmg049MtfRegimeAtrExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_049_ENABLED", enabledFallback),
            RequireAllTimeframes: TryReadEnvironmentBool("TMG_049_REQUIRE_ALL_TIMEFRAMES", true),
            AtrLookbackBars: Math.Max(1, TryReadEnvironmentInt("TMG_049_ATR_LOOKBACK_BARS", 14)),
            AtrStopMultiple: Math.Max(0.0, TryReadEnvironmentDouble("TMG_049_ATR_STOP_MULTIPLE", 2.0)),
            RegimeExitRequiresOppositeAlignment: TryReadEnvironmentBool("TMG_049_REGIME_EXIT_REQUIRES_OPPOSITE_ALIGNMENT", true),
            FlattenRoute: TryReadEnvironmentString("TMG_049_FLATTEN_ROUTE", "MARKET"),
            FlattenTif: TryReadEnvironmentString("TMG_049_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_049_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    // ─────────────────────────────────────────────────────────────────
    // ENVIRONMENT HELPERS
    // ─────────────────────────────────────────────────────────────────

    private static bool TryReadEnvironmentBool(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "1" or "Y" or "YES" or "TRUE" => true,
            "0" or "N" or "NO" or "FALSE" => false,
            _ => fallback
        };
    }

    private static int TryReadEnvironmentInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static double TryReadEnvironmentDouble(string name, double fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return double.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string TryReadEnvironmentString(string name, string fallback)
    {
        if (name.EndsWith("_FLATTEN_ROUTE", StringComparison.OrdinalIgnoreCase))
        {
            return "MARKET";
        }

        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
