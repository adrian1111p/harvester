using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
