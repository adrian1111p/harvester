using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
