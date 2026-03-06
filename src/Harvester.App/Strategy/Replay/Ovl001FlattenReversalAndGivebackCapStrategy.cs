using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
