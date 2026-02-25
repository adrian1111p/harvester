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
        var endOfDay = new Eod001ForceFlatStrategy(BuildEndOfDayConfigFromEnvironment());
        _pipeline = new ReplayDayTradingPipeline(
            globalSafetyOverlays: [_overlay],
            entryStrategies: [entry],
            tradeManagementStrategies: [tradeManagement, tradeManagementBreakEven, tradeManagementTrailing, tradeManagementPartialRunner, tradeManagementTimeStop, tradeManagementAdaptive],
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

        var markPrice = ResolveMarkPrice(dataSlice);
        if (markPrice <= 0)
        {
            return [];
        }

        var dayTradingContext = new ReplayDayTradingContext(
            TimestampUtc: dataSlice.TimestampUtc,
            Symbol: symbol,
            MarkPrice: markPrice,
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

    private static double ResolveMarkPrice(StrategyDataSlice dataSlice)
    {
        var last = dataSlice.TopTicks
            .Where(x => x.Field == 4)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
        if (last > 0)
        {
            return last;
        }

        var bid = dataSlice.TopTicks
            .Where(x => x.Field == 1)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
        var ask = dataSlice.TopTicks
            .Where(x => x.Field == 2)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
        if (bid > 0 && ask > 0)
        {
            return (bid + ask) / 2.0;
        }

        return dataSlice.HistoricalBars.LastOrDefault()?.Close ?? 0;
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
