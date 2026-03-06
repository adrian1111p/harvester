using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
