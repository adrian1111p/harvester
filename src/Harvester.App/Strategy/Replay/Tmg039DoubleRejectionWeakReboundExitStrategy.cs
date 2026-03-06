using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg039DoubleRejectionWeakReboundExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_039_DOUBLE_REJECTION_WEAK_REBOUND_EXIT";

    private readonly Tmg039DoubleRejectionWeakReboundExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg039DoubleRejectionWeakReboundExitStrategy(Tmg039DoubleRejectionWeakReboundExitConfig? config = null)
    {
        _config = config ?? Tmg039DoubleRejectionWeakReboundExitConfig.Default;
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
        var firstRejectionBars = Math.Max(1, _config.FirstRejectionBars);
        var microReboundBars = Math.Max(1, _config.MicroReboundBars);
        var secondRejectionBars = Math.Max(1, _config.SecondRejectionBars);
        var windowBars = adverseBarsLookback + reboundBars + firstRejectionBars + microReboundBars + secondRejectionBars;

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
        var firstRejectionSegment = moves.Skip(adverseBarsLookback + reboundBars).Take(firstRejectionBars).ToList();
        var microReboundSegment = moves.Skip(adverseBarsLookback + reboundBars + firstRejectionBars).Take(microReboundBars).ToList();
        var secondRejectionSegment = moves.Skip(adverseBarsLookback + reboundBars + firstRejectionBars + microReboundBars).Take(secondRejectionBars).ToList();

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

        var firstRejectionConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? firstRejectionSegment.All(move => move < 0)
            : firstRejectionSegment.All(move => move > 0);
        var firstRejectionMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? firstRejectionSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : firstRejectionSegment.Sum(move => Math.Max(0.0, move));

        var microReboundConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? microReboundSegment.All(move => move > 0)
            : microReboundSegment.All(move => move < 0);
        var microReboundMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? microReboundSegment.Sum(move => Math.Max(0.0, move))
            : microReboundSegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var secondRejectionConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? secondRejectionSegment.All(move => move < 0)
            : secondRejectionSegment.All(move => move > 0);
        var secondRejectionMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? secondRejectionSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : secondRejectionSegment.Sum(move => Math.Max(0.0, move));

        if (!adverseConfirmed
            || adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct)
            || !reboundConfirmed
            || reboundMovePct < Math.Max(0.0, _config.MinReboundMovePct)
            || !firstRejectionConfirmed
            || firstRejectionMovePct < Math.Max(0.0, _config.MinFirstRejectionMovePct)
            || !microReboundConfirmed
            || microReboundMovePct > Math.Max(0.0, _config.MaxMicroReboundMovePct)
            || !secondRejectionConfirmed
            || secondRejectionMovePct < Math.Max(0.0, _config.MinSecondRejectionMovePct))
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
                Source: $"trade-management:{StrategyId}:double-reject-cancel",
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
                Source: $"trade-management:{StrategyId}:double-reject-flatten",
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
