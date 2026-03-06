using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg018StaleFavorableMoveExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_018_STALE_FAVORABLE_MOVE_EXIT";

    private readonly Tmg018StaleFavorableMoveExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg018StaleFavorableMoveExitStrategy(Tmg018StaleFavorableMoveExitConfig? config = null)
    {
        _config = config ?? Tmg018StaleFavorableMoveExitConfig.Default;
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
            : new GuardState(Side: side, BestFavorablePrice: context.MarkPrice, BarsWithoutExtension: 0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, BestFavorablePrice: context.MarkPrice, BarsWithoutExtension: 0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var improved = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice > state.BestFavorablePrice
            : context.MarkPrice < state.BestFavorablePrice;
        var bestFavorablePrice = improved
            ? context.MarkPrice
            : state.BestFavorablePrice;
        var barsWithoutExtension = improved
            ? 0
            : state.BarsWithoutExtension + 1;

        var openProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) / entry
            : (entry - context.MarkPrice) / entry;
        var minProfit = Math.Max(0.0, _config.MinOpenProfitPct);
        var shouldFlatten = barsWithoutExtension >= Math.Max(0, _config.MaxBarsWithoutFavorableExtension)
            && openProfitPct >= minProfit;
        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with
            {
                BestFavorablePrice = bestFavorablePrice,
                BarsWithoutExtension = barsWithoutExtension
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            BestFavorablePrice = bestFavorablePrice,
            BarsWithoutExtension = barsWithoutExtension,
            Triggered = true
        };

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
                Source: $"trade-management:{StrategyId}:stale-cancel",
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
                Source: $"trade-management:{StrategyId}:stale-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double BestFavorablePrice,
        int BarsWithoutExtension,
        bool Triggered
    );
}
