using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg034PostReboundFadeExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_034_POST_REBOUND_FADE_EXIT";

    private readonly Tmg034PostReboundFadeExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg034PostReboundFadeExitStrategy(Tmg034PostReboundFadeExitConfig? config = null)
    {
        _config = config ?? Tmg034PostReboundFadeExitConfig.Default;
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
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, RecentSignedMovesPct: [], Triggered: false);
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

        var signedMovePct = (context.MarkPrice - previousMark) / previousMark;
        var adverseBarsLookback = Math.Max(1, _config.AdverseBarsLookback);
        var reboundBars = Math.Max(1, _config.ReboundBars);
        var fadeBars = Math.Max(1, _config.FadeBars);
        var windowBars = adverseBarsLookback + reboundBars + fadeBars;

        var moves = state.RecentSignedMovesPct.Count > 0
            ? state.RecentSignedMovesPct.ToList()
            : [];
        moves.Add(signedMovePct);
        if (moves.Count > windowBars)
        {
            moves.RemoveAt(0);
        }

        if (moves.Count < windowBars)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = moves
            };
            return [];
        }

        var adverseSegment = moves.Take(adverseBarsLookback).ToList();
        var reboundSegment = moves.Skip(adverseBarsLookback).Take(reboundBars).ToList();
        var fadeSegment = moves.Skip(adverseBarsLookback + reboundBars).Take(fadeBars).ToList();

        var adverseConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? adverseSegment.All(move => move < 0)
            : adverseSegment.All(move => move > 0);
        var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? adverseSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : adverseSegment.Sum(move => Math.Max(0.0, move));

        var reboundConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? reboundSegment.All(move => move > 0)
            : reboundSegment.All(move => move < 0);
        var reboundMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? reboundSegment.Sum(move => Math.Max(0.0, move))
            : reboundSegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var fadeConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? fadeSegment.All(move => move < 0)
            : fadeSegment.All(move => move > 0);
        var fadeMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? fadeSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : fadeSegment.Sum(move => Math.Max(0.0, move));

        var retraceThreshold = Math.Max(0.0, _config.MinFadeRetracePctOfRebound) * reboundMovePct;
        if (!adverseConfirmed
            || adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct)
            || !reboundConfirmed
            || reboundMovePct < Math.Max(0.0, _config.MinReboundMovePct)
            || !fadeConfirmed
            || fadeMovePct < Math.Max(0.0, _config.MinFadeMovePct)
            || fadeMovePct < retraceThreshold)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                RecentSignedMovesPct = moves
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
                RecentSignedMovesPct = moves
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            RecentSignedMovesPct = moves,
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
                Source: $"trade-management:{StrategyId}:fade-cancel",
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
                Source: $"trade-management:{StrategyId}:fade-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        IReadOnlyList<double> RecentSignedMovesPct,
        bool Triggered
    );
}
