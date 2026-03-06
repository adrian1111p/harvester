using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

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
