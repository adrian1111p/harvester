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
