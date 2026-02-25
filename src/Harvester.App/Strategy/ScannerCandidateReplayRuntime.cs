using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ScannerCandidateReplayRuntime :
    IStrategyRuntime,
    IReplayOrderSignalSource,
    IReplaySimulationFeedbackSink,
    IReplayScannerSelectionSource
{
    private readonly ReplayScannerSymbolSelectionSnapshotRow _selectionSnapshot;
    private readonly Ovl001FlattenReversalAndGivebackCapStrategy _overlay;
    private readonly ReplayDayTradingPipeline _pipeline;
    private double _positionQuantity;
    private double _averagePrice;

    public ScannerCandidateReplayRuntime(
        string candidatesInputPath,
        int topN,
        double minScore,
        double orderQuantity,
        string orderSide,
        string orderType,
        string timeInForce,
        double limitOffsetBps)
    {
        var selectionModule = new ReplayScannerSymbolSelectionModule(candidatesInputPath, topN, minScore);
        _selectionSnapshot = selectionModule.GetSnapshot();
        _overlay = new Ovl001FlattenReversalAndGivebackCapStrategy(BuildOverlayConfigFromEnvironment());
        var entry = new ReplayScannerSingleShotEntryStrategy(
            orderQuantity,
            orderSide,
            orderType,
            timeInForce,
            limitOffsetBps);
        var tradeManagement = new Tmg001BracketExitStrategy(BuildTradeManagementConfigFromEnvironment());
        var tradeManagementBreakEven = new Tmg002BreakEvenEscalationStrategy(BuildTradeManagementBreakEvenConfigFromEnvironment());
        var tradeManagementTrailing = new Tmg003TrailingProgressionStrategy(BuildTradeManagementTrailingConfigFromEnvironment());
        var tradeManagementPartialRunner = new Tmg004PartialTakeProfitRunnerTrailStrategy(BuildTradeManagementPartialRunnerConfigFromEnvironment());
        var tradeManagementTimeStop = new Tmg005TimeStopStrategy(BuildTradeManagementTimeStopConfigFromEnvironment());
        var tradeManagementAdaptive = new Tmg006VolatilityAdaptiveExitStrategy(BuildTradeManagementAdaptiveConfigFromEnvironment());
        var tradeManagementDrawdownDerisk = new Tmg007DrawdownDeriskStrategy(BuildTradeManagementDrawdownDeriskConfigFromEnvironment());
        var tradeManagementVwapReversion = new Tmg008SessionVwapReversionExitStrategy(BuildTradeManagementVwapReversionConfigFromEnvironment());
        var tradeManagementSpreadGuard = new Tmg009LiquiditySpreadExitStrategy(BuildTradeManagementSpreadGuardConfigFromEnvironment());
        var tradeManagementEventRisk = new Tmg010EventRiskCooldownGuardStrategy(BuildTradeManagementEventRiskConfigFromEnvironment());
        var tradeManagementStallExit = new Tmg011StallExitGuardStrategy(BuildTradeManagementStallExitConfigFromEnvironment());
        var tradeManagementPnlCapExit = new Tmg012PnlCapExitStrategy(BuildTradeManagementPnlCapExitConfigFromEnvironment());
        var tradeManagementSpreadPersistence = new Tmg013SpreadPersistenceExitStrategy(BuildTradeManagementSpreadPersistenceConfigFromEnvironment());
        var tradeManagementGapRisk = new Tmg014GapRiskExitStrategy(BuildTradeManagementGapRiskConfigFromEnvironment());
        var tradeManagementAdverseDrift = new Tmg015AdverseDriftExitStrategy(BuildTradeManagementAdverseDriftConfigFromEnvironment());
        var tradeManagementPeakPullback = new Tmg016PeakPullbackExitStrategy(BuildTradeManagementPeakPullbackConfigFromEnvironment());
        var tradeManagementMicroStress = new Tmg017MicrostructureStressExitStrategy(BuildTradeManagementMicrostructureStressConfigFromEnvironment());
        var tradeManagementStaleFavorable = new Tmg018StaleFavorableMoveExitStrategy(BuildTradeManagementStaleFavorableConfigFromEnvironment());
        var tradeManagementRollingAdverse = new Tmg019RollingAdverseWindowExitStrategy(BuildTradeManagementRollingAdverseConfigFromEnvironment());
        var tradeManagementUnderperformanceTimeout = new Tmg020UnderperformanceTimeoutExitStrategy(BuildTradeManagementUnderperformanceTimeoutConfigFromEnvironment());
        var tradeManagementQuotePressure = new Tmg021QuotePressureExitStrategy(BuildTradeManagementQuotePressureConfigFromEnvironment());
        var tradeManagementVolatilityShockWindow = new Tmg022VolatilityShockWindowExitStrategy(BuildTradeManagementVolatilityShockWindowConfigFromEnvironment());
        var tradeManagementProfitReversionFailsafe = new Tmg023ProfitReversionFailsafeExitStrategy(BuildTradeManagementProfitReversionFailsafeConfigFromEnvironment());
        var tradeManagementRangeCompression = new Tmg024RangeCompressionExitStrategy(BuildTradeManagementRangeCompressionConfigFromEnvironment());
        var tradeManagementRollingVolatilityFloor = new Tmg025RollingVolatilityFloorExitStrategy(BuildTradeManagementRollingVolatilityFloorConfigFromEnvironment());
        var tradeManagementChopAdverse = new Tmg026ChopAdverseExitStrategy(BuildTradeManagementChopAdverseConfigFromEnvironment());
        var tradeManagementTrendExhaustion = new Tmg027TrendExhaustionExitStrategy(BuildTradeManagementTrendExhaustionConfigFromEnvironment());
        var tradeManagementReversalAcceleration = new Tmg028ReversalAccelerationExitStrategy(BuildTradeManagementReversalAccelerationConfigFromEnvironment());
        var tradeManagementSustainedReversion = new Tmg029SustainedReversionExitStrategy(BuildTradeManagementSustainedReversionConfigFromEnvironment());
        var tradeManagementRecoveryFailure = new Tmg030RecoveryFailureExitStrategy(BuildTradeManagementRecoveryFailureConfigFromEnvironment());
        var tradeManagementReboundStall = new Tmg031ReboundStallExitStrategy(BuildTradeManagementReboundStallConfigFromEnvironment());
        var tradeManagementWeakBounceFailure = new Tmg032WeakBounceFailureExitStrategy(BuildTradeManagementWeakBounceFailureConfigFromEnvironment());
        var tradeManagementReboundRollunder = new Tmg033ReboundRollunderExitStrategy(BuildTradeManagementReboundRollunderConfigFromEnvironment());
        var tradeManagementPostReboundFade = new Tmg034PostReboundFadeExitStrategy(BuildTradeManagementPostReboundFadeConfigFromEnvironment());
        var tradeManagementReboundRejectionAccel = new Tmg035ReboundRejectionAccelExitStrategy(BuildTradeManagementReboundRejectionAccelConfigFromEnvironment());
        var tradeManagementRejectionStallBreak = new Tmg036RejectionStallBreakExitStrategy(BuildTradeManagementRejectionStallBreakConfigFromEnvironment());
        var tradeManagementRejectionReboundFail = new Tmg037RejectionReboundFailExitStrategy(BuildTradeManagementRejectionReboundFailConfigFromEnvironment());
        var tradeManagementRejectionContinuationConfirm = new Tmg038RejectionContinuationConfirmExitStrategy(BuildTradeManagementRejectionContinuationConfirmConfigFromEnvironment());
        var tradeManagementDoubleRejectionWeakRebound = new Tmg039DoubleRejectionWeakReboundExitStrategy(BuildTradeManagementDoubleRejectionWeakReboundConfigFromEnvironment());
        var tradeManagementDoubleReboundFailure = new Tmg040DoubleReboundFailureExitStrategy(BuildTradeManagementDoubleReboundFailureConfigFromEnvironment());
        var tradeManagementTripleStepBreak = new Tmg041TripleStepBreakExitStrategy(BuildTradeManagementTripleStepBreakConfigFromEnvironment());
        var endOfDay = new Eod001ForceFlatStrategy(BuildEndOfDayConfigFromEnvironment());
        _pipeline = new ReplayDayTradingPipeline(
            globalSafetyOverlays: [_overlay],
            entryStrategies: [entry],
            tradeManagementStrategies: [tradeManagement, tradeManagementBreakEven, tradeManagementTrailing, tradeManagementPartialRunner, tradeManagementTimeStop, tradeManagementAdaptive, tradeManagementDrawdownDerisk, tradeManagementVwapReversion, tradeManagementSpreadGuard, tradeManagementEventRisk, tradeManagementStallExit, tradeManagementPnlCapExit, tradeManagementSpreadPersistence, tradeManagementGapRisk, tradeManagementAdverseDrift, tradeManagementPeakPullback, tradeManagementMicroStress, tradeManagementStaleFavorable, tradeManagementRollingAdverse, tradeManagementUnderperformanceTimeout, tradeManagementQuotePressure, tradeManagementVolatilityShockWindow, tradeManagementProfitReversionFailsafe, tradeManagementRangeCompression, tradeManagementRollingVolatilityFloor, tradeManagementChopAdverse, tradeManagementTrendExhaustion, tradeManagementReversalAcceleration, tradeManagementSustainedReversion, tradeManagementRecoveryFailure, tradeManagementReboundStall, tradeManagementWeakBounceFailure, tradeManagementReboundRollunder, tradeManagementPostReboundFade, tradeManagementReboundRejectionAccel, tradeManagementRejectionStallBreak, tradeManagementRejectionReboundFail, tradeManagementRejectionContinuationConfirm, tradeManagementDoubleRejectionWeakRebound, tradeManagementDoubleReboundFailure, tradeManagementTripleStepBreak],
            endOfDayStrategies: [endOfDay]);
        _positionQuantity = 0;
        _averagePrice = 0;
    }

    public Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnShutdownAsync(StrategyRuntimeContext context, int exitCode, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ReplayScannerSymbolSelectionSnapshotRow GetScannerSelectionSnapshot()
    {
        return _selectionSnapshot;
    }

    public IReadOnlyList<ReplayOrderIntent> GetReplayOrderIntents(StrategyDataSlice dataSlice, StrategyRuntimeContext context)
    {
        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (!_selectionSnapshot.SelectedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        var bidPrice = ResolveBidPrice(dataSlice);
        var askPrice = ResolveAskPrice(dataSlice);
        var markPrice = ResolveMarkPrice(dataSlice, bidPrice, askPrice);
        if (markPrice <= 0)
        {
            return [];
        }

        if (bidPrice <= 0)
        {
            bidPrice = markPrice;
        }

        if (askPrice <= 0)
        {
            askPrice = markPrice;
        }

        var dayTradingContext = new ReplayDayTradingContext(
            TimestampUtc: dataSlice.TimestampUtc,
            Symbol: symbol,
            MarkPrice: markPrice,
            BidPrice: bidPrice,
            AskPrice: askPrice,
            PositionQuantity: _positionQuantity,
            AveragePrice: _averagePrice);

        return _pipeline.Evaluate(dayTradingContext, _selectionSnapshot);
    }

    public void OnReplaySliceResult(StrategyDataSlice dataSlice, ReplaySliceSimulationResult result, string activeSymbol)
    {
        _positionQuantity = result.Portfolio.PositionQuantity;
        _averagePrice = result.Portfolio.AveragePrice;
        _overlay.OnPositionEvent(
            activeSymbol,
            result.Portfolio.TimestampUtc,
            result.Portfolio.PositionQuantity,
            result.Portfolio.AveragePrice,
            result.Fills);
    }

    private static double ResolveMarkPrice(StrategyDataSlice dataSlice, double bidPrice, double askPrice)
    {
        var last = dataSlice.TopTicks
            .Where(x => x.Field == 4)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
        if (last > 0)
        {
            return last;
        }

        if (bidPrice > 0 && askPrice > 0)
        {
            return (bidPrice + askPrice) / 2.0;
        }

        return dataSlice.HistoricalBars.LastOrDefault()?.Close ?? 0;
    }

    private static double ResolveBidPrice(StrategyDataSlice dataSlice)
    {
        return dataSlice.TopTicks
            .Where(x => x.Field == 1)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
    }

    private static double ResolveAskPrice(StrategyDataSlice dataSlice)
    {
        return dataSlice.TopTicks
            .Where(x => x.Field == 2)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
    }

    private static Ovl001FlattenConfig BuildOverlayConfigFromEnvironment()
    {
        return new Ovl001FlattenConfig(
            ImmediateWindowSec: Math.Max(1, TryReadEnvironmentInt("OVL_001_IMMEDIATE_WINDOW_SEC", 5)),
            ImmediateAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_IMMEDIATE_ADVERSE_MOVE_PCT", 0.002)),
            ImmediateAdverseMoveUsd: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_IMMEDIATE_ADVERSE_MOVE_USD", 10.0)),
            GivebackPctOfNotional: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_GIVEBACK_PCT_OF_NOTIONAL", 0.01)),
            GivebackUsdCap: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_GIVEBACK_USD_CAP", 30.0)),
            TrailingActivatesOnlyAfterProfit: TryReadEnvironmentBool("OVL_001_TRAILING_ACTIVATES_ONLY_AFTER_PROFIT", true),
            FlattenRoute: TryReadEnvironmentString("OVL_001_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("OVL_001_FLATTEN_TIF", "DAY+"),
            FlattenOrderType: TryReadEnvironmentString("OVL_001_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg001BracketConfig BuildTradeManagementConfigFromEnvironment()
    {
        return new Tmg001BracketConfig(
            Enabled: TryReadEnvironmentBool("TMG_001_ENABLED", true),
            StopLossPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_001_STOP_LOSS_PCT", 0.003)),
            TakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_001_TAKE_PROFIT_PCT", 0.006)),
            TimeInForce: TryReadEnvironmentString("TMG_001_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg002BreakEvenConfig BuildTradeManagementBreakEvenConfigFromEnvironment()
    {
        return new Tmg002BreakEvenConfig(
            Enabled: TryReadEnvironmentBool("TMG_002_ENABLED", false),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_002_TRIGGER_PROFIT_PCT", 0.003)),
            StopOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_002_STOP_OFFSET_PCT", 0.0)),
            TimeInForce: TryReadEnvironmentString("TMG_002_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg003TrailingProgressionConfig BuildTradeManagementTrailingConfigFromEnvironment()
    {
        return new Tmg003TrailingProgressionConfig(
            Enabled: TryReadEnvironmentBool("TMG_003_ENABLED", false),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_003_TRIGGER_PROFIT_PCT", 0.006)),
            TrailOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_003_TRAIL_OFFSET_PCT", 0.002)),
            TimeInForce: TryReadEnvironmentString("TMG_003_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg004PartialTakeProfitRunnerTrailConfig BuildTradeManagementPartialRunnerConfigFromEnvironment()
    {
        return new Tmg004PartialTakeProfitRunnerTrailConfig(
            Enabled: TryReadEnvironmentBool("TMG_004_ENABLED", false),
            TriggerProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_TRIGGER_PROFIT_PCT", 0.008)),
            TakeProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_TAKE_PROFIT_PCT", 0.008)),
            TakeProfitFraction: Math.Clamp(TryReadEnvironmentDouble("TMG_004_TAKE_PROFIT_FRACTION", 0.5), 0.0, 1.0),
            RunnerTrailOffsetPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_004_RUNNER_TRAIL_OFFSET_PCT", 0.002)),
            TimeInForce: TryReadEnvironmentString("TMG_004_TIF", "DAY").ToUpperInvariant());
    }

    private static Tmg005TimeStopConfig BuildTradeManagementTimeStopConfigFromEnvironment()
    {
        return new Tmg005TimeStopConfig(
            Enabled: TryReadEnvironmentBool("TMG_005_ENABLED", false),
            MaxHoldingBars: Math.Max(0, TryReadEnvironmentInt("TMG_005_MAX_HOLDING_BARS", 30)),
            MaxHoldingMinutes: Math.Max(0, TryReadEnvironmentInt("TMG_005_MAX_HOLDING_MINUTES", 120)),
            FlattenRoute: TryReadEnvironmentString("TMG_005_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_005_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_005_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg006VolatilityAdaptiveExitConfig BuildTradeManagementAdaptiveConfigFromEnvironment()
    {
        return new Tmg006VolatilityAdaptiveExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_006_ENABLED", false),
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

    private static Tmg007DrawdownDeriskConfig BuildTradeManagementDrawdownDeriskConfigFromEnvironment()
    {
        return new Tmg007DrawdownDeriskConfig(
            Enabled: TryReadEnvironmentBool("TMG_007_ENABLED", false),
            DeriskDrawdownPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_007_DERISK_DRAWDOWN_PCT", 0.003)),
            FlattenDrawdownPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_007_FLATTEN_DRAWDOWN_PCT", 0.006)),
            DeriskFraction: Math.Clamp(TryReadEnvironmentDouble("TMG_007_DERISK_FRACTION", 0.5), 0.0, 1.0),
            FlattenRoute: TryReadEnvironmentString("TMG_007_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_007_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_007_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg008SessionVwapReversionConfig BuildTradeManagementVwapReversionConfigFromEnvironment()
    {
        return new Tmg008SessionVwapReversionConfig(
            Enabled: TryReadEnvironmentBool("TMG_008_ENABLED", false),
            MinSamples: Math.Max(1, TryReadEnvironmentInt("TMG_008_MIN_SAMPLES", 5)),
            AdverseDeviationPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_008_ADVERSE_DEVIATION_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_008_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_008_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_008_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg009LiquiditySpreadExitConfig BuildTradeManagementSpreadGuardConfigFromEnvironment()
    {
        return new Tmg009LiquiditySpreadExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_009_ENABLED", false),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_009_SPREAD_TRIGGER_PCT", 0.003)),
            RequireUnrealizedLoss: TryReadEnvironmentBool("TMG_009_REQUIRE_UNREALIZED_LOSS", true),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_009_MIN_ADVERSE_MOVE_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_009_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_009_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_009_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg010EventRiskCooldownConfig BuildTradeManagementEventRiskConfigFromEnvironment()
    {
        return new Tmg010EventRiskCooldownConfig(
            Enabled: TryReadEnvironmentBool("TMG_010_ENABLED", false),
            ShockMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_010_SHOCK_MOVE_PCT", 0.015)),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_010_SPREAD_TRIGGER_PCT", 0.010)),
            CooldownBars: Math.Max(0, TryReadEnvironmentInt("TMG_010_COOLDOWN_BARS", 5)),
            FlattenRoute: TryReadEnvironmentString("TMG_010_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_010_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_010_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg011StallExitConfig BuildTradeManagementStallExitConfigFromEnvironment()
    {
        return new Tmg011StallExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_011_ENABLED", false),
            MinHoldingBars: Math.Max(0, TryReadEnvironmentInt("TMG_011_MIN_HOLDING_BARS", 10)),
            MaxAbsoluteMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_011_MAX_ABSOLUTE_MOVE_PCT", 0.0015)),
            FlattenRoute: TryReadEnvironmentString("TMG_011_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_011_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_011_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg012PnlCapExitConfig BuildTradeManagementPnlCapExitConfigFromEnvironment()
    {
        return new Tmg012PnlCapExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_012_ENABLED", false),
            StopLossUsd: Math.Max(0.0, TryReadEnvironmentDouble("TMG_012_STOP_LOSS_USD", 20.0)),
            TakeProfitUsd: Math.Max(0.0, TryReadEnvironmentDouble("TMG_012_TAKE_PROFIT_USD", 40.0)),
            FlattenRoute: TryReadEnvironmentString("TMG_012_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_012_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_012_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg013SpreadPersistenceExitConfig BuildTradeManagementSpreadPersistenceConfigFromEnvironment()
    {
        return new Tmg013SpreadPersistenceExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_013_ENABLED", false),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_013_SPREAD_TRIGGER_PCT", 0.004)),
            MinConsecutiveBars: Math.Max(1, TryReadEnvironmentInt("TMG_013_MIN_CONSECUTIVE_BARS", 3)),
            FlattenRoute: TryReadEnvironmentString("TMG_013_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_013_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_013_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg014GapRiskExitConfig BuildTradeManagementGapRiskConfigFromEnvironment()
    {
        return new Tmg014GapRiskExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_014_ENABLED", false),
            GapMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_014_GAP_MOVE_PCT", 0.01)),
            RequireAdverseDirection: TryReadEnvironmentBool("TMG_014_REQUIRE_ADVERSE_DIRECTION", true),
            FlattenRoute: TryReadEnvironmentString("TMG_014_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_014_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_014_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg015AdverseDriftExitConfig BuildTradeManagementAdverseDriftConfigFromEnvironment()
    {
        return new Tmg015AdverseDriftExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_015_ENABLED", false),
            MinConsecutiveAdverseBars: Math.Max(1, TryReadEnvironmentInt("TMG_015_MIN_CONSECUTIVE_ADVERSE_BARS", 3)),
            MinCumulativeAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_015_MIN_CUMULATIVE_ADVERSE_MOVE_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_015_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_015_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_015_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg016PeakPullbackExitConfig BuildTradeManagementPeakPullbackConfigFromEnvironment()
    {
        return new Tmg016PeakPullbackExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_016_ENABLED", false),
            ActivationProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_016_ACTIVATION_PROFIT_PCT", 0.004)),
            PullbackFromPeakPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_016_PULLBACK_FROM_PEAK_PCT", 0.002)),
            FlattenRoute: TryReadEnvironmentString("TMG_016_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_016_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_016_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg017MicrostructureStressExitConfig BuildTradeManagementMicrostructureStressConfigFromEnvironment()
    {
        return new Tmg017MicrostructureStressExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_017_ENABLED", false),
            SpreadTriggerPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_017_SPREAD_TRIGGER_PCT", 0.003)),
            MidDislocationPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_017_MID_DISLOCATION_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_017_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_017_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_017_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_017_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg018StaleFavorableMoveExitConfig BuildTradeManagementStaleFavorableConfigFromEnvironment()
    {
        return new Tmg018StaleFavorableMoveExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_018_ENABLED", false),
            MaxBarsWithoutFavorableExtension: Math.Max(0, TryReadEnvironmentInt("TMG_018_MAX_BARS_WITHOUT_FAVORABLE_EXTENSION", 10)),
            MinOpenProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_018_MIN_OPEN_PROFIT_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_018_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_018_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_018_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg019RollingAdverseWindowExitConfig BuildTradeManagementRollingAdverseConfigFromEnvironment()
    {
        return new Tmg019RollingAdverseWindowExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_019_ENABLED", false),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_019_WINDOW_BARS", 5)),
            AdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_019_ADVERSE_MOVE_SUM_PCT", 0.003)),
            FlattenRoute: TryReadEnvironmentString("TMG_019_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_019_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_019_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg020UnderperformanceTimeoutExitConfig BuildTradeManagementUnderperformanceTimeoutConfigFromEnvironment()
    {
        return new Tmg020UnderperformanceTimeoutExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_020_ENABLED", false),
            MaxBarsToReachMinProfit: Math.Max(0, TryReadEnvironmentInt("TMG_020_MAX_BARS_TO_REACH_MIN_PROFIT", 20)),
            MinProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_020_MIN_PROFIT_PCT", 0.001)),
            FlattenRoute: TryReadEnvironmentString("TMG_020_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_020_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_020_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg021QuotePressureExitConfig BuildTradeManagementQuotePressureConfigFromEnvironment()
    {
        return new Tmg021QuotePressureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_021_ENABLED", false),
            MinPressurePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_021_MIN_PRESSURE_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_021_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_021_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_021_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_021_FLATTEN_ORDER_TYPE", "MARKETABLE_LIMIT"));
    }

    private static Tmg022VolatilityShockWindowExitConfig BuildTradeManagementVolatilityShockWindowConfigFromEnvironment()
    {
        return new Tmg022VolatilityShockWindowExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_022_ENABLED", false),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_022_WINDOW_BARS", 3)),
            ShockMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_022_SHOCK_MOVE_SUM_PCT", 0.01)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_022_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_022_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_022_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_022_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg023ProfitReversionFailsafeExitConfig BuildTradeManagementProfitReversionFailsafeConfigFromEnvironment()
    {
        return new Tmg023ProfitReversionFailsafeExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_023_ENABLED", false),
            ActivationProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_023_ACTIVATION_PROFIT_PCT", 0.004)),
            ReversionProfitFloorPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_023_REVERSION_PROFIT_FLOOR_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_023_REQUIRE_ADVERSE_UNREALIZED", false),
            FlattenRoute: TryReadEnvironmentString("TMG_023_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_023_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_023_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg024RangeCompressionExitConfig BuildTradeManagementRangeCompressionConfigFromEnvironment()
    {
        return new Tmg024RangeCompressionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_024_ENABLED", false),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_024_WINDOW_BARS", 5)),
            MaxRangePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_024_MAX_RANGE_PCT", 0.001)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_024_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_024_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_024_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_024_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg025RollingVolatilityFloorExitConfig BuildTradeManagementRollingVolatilityFloorConfigFromEnvironment()
    {
        return new Tmg025RollingVolatilityFloorExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_025_ENABLED", false),
            WindowBars: Math.Max(1, TryReadEnvironmentInt("TMG_025_WINDOW_BARS", 5)),
            MaxRealizedVolPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_025_MAX_REALIZED_VOL_PCT", 0.0005)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_025_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_025_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_025_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_025_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg026ChopAdverseExitConfig BuildTradeManagementChopAdverseConfigFromEnvironment()
    {
        return new Tmg026ChopAdverseExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_026_ENABLED", false),
            WindowBars: Math.Max(2, TryReadEnvironmentInt("TMG_026_WINDOW_BARS", 6)),
            MinSignAlternations: Math.Max(1, TryReadEnvironmentInt("TMG_026_MIN_SIGN_ALTERNATIONS", 4)),
            MinAdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_026_MIN_ADVERSE_MOVE_SUM_PCT", 0.002)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_026_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_026_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_026_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_026_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg027TrendExhaustionExitConfig BuildTradeManagementTrendExhaustionConfigFromEnvironment()
    {
        return new Tmg027TrendExhaustionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_027_ENABLED", false),
            FavorableBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_027_FAVORABLE_BARS_LOOKBACK", 4)),
            MinFavorableMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_027_MIN_FAVORABLE_MOVE_PCT", 0.002)),
            ReversalConfirmBars: Math.Max(1, TryReadEnvironmentInt("TMG_027_REVERSAL_CONFIRM_BARS", 2)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_027_REQUIRE_ADVERSE_UNREALIZED", false),
            FlattenRoute: TryReadEnvironmentString("TMG_027_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_027_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_027_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg028ReversalAccelerationExitConfig BuildTradeManagementReversalAccelerationConfigFromEnvironment()
    {
        return new Tmg028ReversalAccelerationExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_028_ENABLED", false),
            ReversalBars: Math.Max(2, TryReadEnvironmentInt("TMG_028_REVERSAL_BARS", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_028_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            RequireAcceleration: TryReadEnvironmentBool("TMG_028_REQUIRE_ACCELERATION", true),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_028_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_028_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_028_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_028_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg029SustainedReversionExitConfig BuildTradeManagementSustainedReversionConfigFromEnvironment()
    {
        return new Tmg029SustainedReversionExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_029_ENABLED", false),
            MinPeakProfitPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_029_MIN_PEAK_PROFIT_PCT", 0.003)),
            ConsecutiveAdverseBars: Math.Max(1, TryReadEnvironmentInt("TMG_029_CONSECUTIVE_ADVERSE_BARS", 3)),
            MinAdverseMoveSumPct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_029_MIN_ADVERSE_MOVE_SUM_PCT", 0.002)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_029_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_029_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_029_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_029_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg030RecoveryFailureExitConfig BuildTradeManagementRecoveryFailureConfigFromEnvironment()
    {
        return new Tmg030RecoveryFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_030_ENABLED", false),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_030_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_030_MIN_ADVERSE_MOVE_PCT", 0.002)),
            RecoveryBars: Math.Max(1, TryReadEnvironmentInt("TMG_030_RECOVERY_BARS", 1)),
            MaxRecoveryMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_030_MAX_RECOVERY_MOVE_PCT", 0.0008)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_030_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_030_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_030_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_030_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg031ReboundStallExitConfig BuildTradeManagementReboundStallConfigFromEnvironment()
    {
        return new Tmg031ReboundStallExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_031_ENABLED", false),
            AdverseBarsLookback: Math.Max(1, TryReadEnvironmentInt("TMG_031_ADVERSE_BARS_LOOKBACK", 2)),
            MinAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_031_MIN_ADVERSE_MOVE_PCT", 0.0015)),
            StallBars: Math.Max(1, TryReadEnvironmentInt("TMG_031_STALL_BARS", 2)),
            MaxAbsoluteStallMovePct: Math.Max(0.0, TryReadEnvironmentDouble("TMG_031_MAX_ABSOLUTE_STALL_MOVE_PCT", 0.0004)),
            RequireAdverseUnrealized: TryReadEnvironmentBool("TMG_031_REQUIRE_ADVERSE_UNREALIZED", true),
            FlattenRoute: TryReadEnvironmentString("TMG_031_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("TMG_031_FLATTEN_TIF", "DAY").ToUpperInvariant(),
            FlattenOrderType: TryReadEnvironmentString("TMG_031_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static Tmg032WeakBounceFailureExitConfig BuildTradeManagementWeakBounceFailureConfigFromEnvironment()
    {
        return new Tmg032WeakBounceFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_032_ENABLED", false),
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

    private static Tmg033ReboundRollunderExitConfig BuildTradeManagementReboundRollunderConfigFromEnvironment()
    {
        return new Tmg033ReboundRollunderExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_033_ENABLED", false),
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

    private static Tmg034PostReboundFadeExitConfig BuildTradeManagementPostReboundFadeConfigFromEnvironment()
    {
        return new Tmg034PostReboundFadeExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_034_ENABLED", false),
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

    private static Tmg035ReboundRejectionAccelExitConfig BuildTradeManagementReboundRejectionAccelConfigFromEnvironment()
    {
        return new Tmg035ReboundRejectionAccelExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_035_ENABLED", false),
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

    private static Tmg036RejectionStallBreakExitConfig BuildTradeManagementRejectionStallBreakConfigFromEnvironment()
    {
        return new Tmg036RejectionStallBreakExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_036_ENABLED", false),
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

    private static Tmg037RejectionReboundFailExitConfig BuildTradeManagementRejectionReboundFailConfigFromEnvironment()
    {
        return new Tmg037RejectionReboundFailExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_037_ENABLED", false),
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

    private static Tmg038RejectionContinuationConfirmExitConfig BuildTradeManagementRejectionContinuationConfirmConfigFromEnvironment()
    {
        return new Tmg038RejectionContinuationConfirmExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_038_ENABLED", false),
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

    private static Tmg039DoubleRejectionWeakReboundExitConfig BuildTradeManagementDoubleRejectionWeakReboundConfigFromEnvironment()
    {
        return new Tmg039DoubleRejectionWeakReboundExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_039_ENABLED", false),
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

    private static Tmg040DoubleReboundFailureExitConfig BuildTradeManagementDoubleReboundFailureConfigFromEnvironment()
    {
        return new Tmg040DoubleReboundFailureExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_040_ENABLED", false),
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

    private static Tmg041TripleStepBreakExitConfig BuildTradeManagementTripleStepBreakConfigFromEnvironment()
    {
        return new Tmg041TripleStepBreakExitConfig(
            Enabled: TryReadEnvironmentBool("TMG_041_ENABLED", false),
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

    private static Eod001ForceFlatConfig BuildEndOfDayConfigFromEnvironment()
    {
        return new Eod001ForceFlatConfig(
            Enabled: TryReadEnvironmentBool("EOD_001_ENABLED", true),
            SessionCloseHourUtc: Math.Clamp(TryReadEnvironmentInt("EOD_001_SESSION_CLOSE_HOUR_UTC", 21), 0, 23),
            SessionCloseMinuteUtc: Math.Clamp(TryReadEnvironmentInt("EOD_001_SESSION_CLOSE_MINUTE_UTC", 0), 0, 59),
            FlattenLeadMinutes: Math.Max(0, TryReadEnvironmentInt("EOD_001_FLATTEN_LEAD_MINUTES", 5)),
            FlattenRoute: TryReadEnvironmentString("EOD_001_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("EOD_001_FLATTEN_TIF", "DAY+"),
            FlattenOrderType: TryReadEnvironmentString("EOD_001_FLATTEN_ORDER_TYPE", "MARKET"));
    }

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
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
