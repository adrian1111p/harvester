using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
