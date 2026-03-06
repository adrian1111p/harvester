using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg023ProfitReversionFailsafeExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_023_PROFIT_REVERSION_FAILSAFE_EXIT";

    private readonly Tmg023ProfitReversionFailsafeExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg023ProfitReversionFailsafeExitStrategy(Tmg023ProfitReversionFailsafeExitConfig? config = null)
    {
        _config = config ?? Tmg023ProfitReversionFailsafeExitConfig.Default;
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
            : new GuardState(Side: side, PeakFavorableProfitPct: 0.0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, PeakFavorableProfitPct: 0.0, Triggered: false);
        }

        if (state.Triggered)
        {
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var positionQty = Math.Abs(context.PositionQuantity);
        var favorableProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) / entry
            : (entry - context.MarkPrice) / entry;
        var peakFavorableProfitPct = Math.Max(state.PeakFavorableProfitPct, favorableProfitPct);
        _stateBySymbol[symbol] = state with { PeakFavorableProfitPct = peakFavorableProfitPct };

        var activationProfitPct = Math.Max(0.0, _config.ActivationProfitPct);
        var reversionProfitFloorPct = Math.Max(0.0, _config.ReversionProfitFloorPct);
        var activated = peakFavorableProfitPct >= activationProfitPct;
        var reverted = favorableProfitPct <= reversionProfitFloorPct;
        if (!activated || !reverted)
        {
            return [];
        }

        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * positionQty
            : (entry - context.MarkPrice) * positionQty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            PeakFavorableProfitPct = peakFavorableProfitPct,
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
                Source: $"trade-management:{StrategyId}:reversion-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: positionQty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:reversion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double PeakFavorableProfitPct,
        bool Triggered
    );
}
