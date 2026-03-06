using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg040DoubleReboundFailureExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_040_DOUBLE_REBOUND_FAILURE_EXIT";

    private readonly Tmg040DoubleReboundFailureExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg040DoubleReboundFailureExitStrategy(Tmg040DoubleReboundFailureExitConfig? config = null)
    {
        _config = config ?? Tmg040DoubleReboundFailureExitConfig.Default;
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
        var firstReboundBars = Math.Max(1, _config.FirstReboundBars);
        var pullbackBars = Math.Max(1, _config.PullbackBars);
        var secondReboundBars = Math.Max(1, _config.SecondReboundBars);
        var finalRejectionBars = Math.Max(1, _config.FinalRejectionBars);
        var windowBars = adverseBarsLookback + firstReboundBars + pullbackBars + secondReboundBars + finalRejectionBars;

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
        var firstReboundSegment = moves.Skip(adverseBarsLookback).Take(firstReboundBars).ToList();
        var pullbackSegment = moves.Skip(adverseBarsLookback + firstReboundBars).Take(pullbackBars).ToList();
        var secondReboundSegment = moves.Skip(adverseBarsLookback + firstReboundBars + pullbackBars).Take(secondReboundBars).ToList();
        var finalRejectionSegment = moves.Skip(adverseBarsLookback + firstReboundBars + pullbackBars + secondReboundBars).Take(finalRejectionBars).ToList();

        var adverseConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? adverseSegment.All(move => move < 0)
            : adverseSegment.All(move => move > 0);
        var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? adverseSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : adverseSegment.Sum(move => Math.Max(0.0, move));

        var firstReboundConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? firstReboundSegment.All(move => move > 0)
            : firstReboundSegment.All(move => move < 0);
        var firstReboundMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? firstReboundSegment.Sum(move => Math.Max(0.0, move))
            : firstReboundSegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var pullbackConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? pullbackSegment.All(move => move < 0)
            : pullbackSegment.All(move => move > 0);
        var pullbackMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? pullbackSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : pullbackSegment.Sum(move => Math.Max(0.0, move));

        var secondReboundConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? secondReboundSegment.All(move => move > 0)
            : secondReboundSegment.All(move => move < 0);
        var secondReboundMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? secondReboundSegment.Sum(move => Math.Max(0.0, move))
            : secondReboundSegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var finalRejectionConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? finalRejectionSegment.All(move => move < 0)
            : finalRejectionSegment.All(move => move > 0);
        var finalRejectionMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? finalRejectionSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : finalRejectionSegment.Sum(move => Math.Max(0.0, move));

        if (!adverseConfirmed
            || adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct)
            || !firstReboundConfirmed
            || firstReboundMovePct < Math.Max(0.0, _config.MinFirstReboundMovePct)
            || !pullbackConfirmed
            || pullbackMovePct < Math.Max(0.0, _config.MinPullbackMovePct)
            || !secondReboundConfirmed
            || secondReboundMovePct > Math.Max(0.0, _config.MaxSecondReboundMovePct)
            || !finalRejectionConfirmed
            || finalRejectionMovePct < Math.Max(0.0, _config.MinFinalRejectionMovePct))
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
                Source: $"trade-management:{StrategyId}:double-rebound-cancel",
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
                Source: $"trade-management:{StrategyId}:double-rebound-flatten",
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
