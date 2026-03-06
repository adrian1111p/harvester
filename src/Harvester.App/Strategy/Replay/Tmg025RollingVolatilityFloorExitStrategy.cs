using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg025RollingVolatilityFloorExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_025_ROLLING_VOLATILITY_FLOOR_EXIT";

    private readonly Tmg025RollingVolatilityFloorExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg025RollingVolatilityFloorExitStrategy(Tmg025RollingVolatilityFloorExitConfig? config = null)
    {
        _config = config ?? Tmg025RollingVolatilityFloorExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
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
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteReturns: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentAbsoluteReturns: [], Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var absoluteReturn = Math.Abs(context.MarkPrice - previousMark) / previousMark;
        var windowBars = Math.Max(1, _config.WindowBars);
        var recentReturns = state.RecentAbsoluteReturns.Count > 0
            ? state.RecentAbsoluteReturns.ToList()
            : [];
        recentReturns.Add(absoluteReturn);
        if (recentReturns.Count > windowBars)
        {
            recentReturns.RemoveAt(0);
        }

        if (recentReturns.Count < windowBars)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        var mean = recentReturns.Average();
        var variance = recentReturns.Sum(value => Math.Pow(value - mean, 2.0)) / recentReturns.Count;
        var realizedVolPct = Math.Sqrt(Math.Max(0.0, variance));
        if (realizedVolPct > Math.Max(0.0, _config.MaxRealizedVolPct))
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        var qty = Math.Abs(context.PositionQuantity);
        var entry = Math.Max(1e-9, context.AveragePrice);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentAbsoluteReturns = recentReturns
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentAbsoluteReturns = recentReturns,
            Triggered = true
        };

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
                Source: $"trade-management:{StrategyId}:vol-floor-cancel",
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
                Source: $"trade-management:{StrategyId}:vol-floor-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentAbsoluteReturns,
        bool Triggered
    );
}
