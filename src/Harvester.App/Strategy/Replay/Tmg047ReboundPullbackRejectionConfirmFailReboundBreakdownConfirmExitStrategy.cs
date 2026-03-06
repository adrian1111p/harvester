using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_047_REBOUND_PULLBACK_REJECTION_CONFIRM_FAIL_REBOUND_BREAKDOWN_CONFIRM_EXIT";

    private readonly Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitStrategy(Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig? config = null)
    {
        _config = config ?? Tmg047ReboundPullbackRejectionConfirmFailReboundBreakdownConfirmExitConfig.Default;
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
        var pullbackBars = Math.Max(1, _config.PullbackBars);
        var recoveryBars = Math.Max(1, _config.RecoveryBars);
        var rejectionBars = Math.Max(1, _config.RejectionBars);
        var confirmationBars = Math.Max(1, _config.ConfirmationBars);
        var failReboundBars = Math.Max(1, _config.FailReboundBars);
        var breakdownBars = Math.Max(1, _config.BreakdownBars);
        var breakdownConfirmBars = Math.Max(1, _config.BreakdownConfirmBars);
        var windowBars = adverseBarsLookback + reboundBars + pullbackBars + recoveryBars + rejectionBars + confirmationBars + failReboundBars + breakdownBars + breakdownConfirmBars;

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
        var pullbackSegment = moves.Skip(adverseBarsLookback + reboundBars).Take(pullbackBars).ToList();
        var recoverySegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars).Take(recoveryBars).ToList();
        var rejectionSegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars + recoveryBars).Take(rejectionBars).ToList();
        var confirmationSegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars + recoveryBars + rejectionBars).Take(confirmationBars).ToList();
        var failReboundSegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars + recoveryBars + rejectionBars + confirmationBars).Take(failReboundBars).ToList();
        var breakdownSegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars + recoveryBars + rejectionBars + confirmationBars + failReboundBars).Take(breakdownBars).ToList();
        var breakdownConfirmSegment = moves.Skip(adverseBarsLookback + reboundBars + pullbackBars + recoveryBars + rejectionBars + confirmationBars + failReboundBars + breakdownBars).Take(breakdownConfirmBars).ToList();

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

        var pullbackConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? pullbackSegment.All(move => move < 0)
            : pullbackSegment.All(move => move > 0);
        var pullbackMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? pullbackSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : pullbackSegment.Sum(move => Math.Max(0.0, move));

        var recoveryConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? recoverySegment.All(move => move > 0)
            : recoverySegment.All(move => move < 0);
        var recoveryMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? recoverySegment.Sum(move => Math.Max(0.0, move))
            : recoverySegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var rejectionConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? rejectionSegment.All(move => move < 0)
            : rejectionSegment.All(move => move > 0);
        var rejectionMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? rejectionSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : rejectionSegment.Sum(move => Math.Max(0.0, move));

        var confirmationConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? confirmationSegment.All(move => move < 0)
            : confirmationSegment.All(move => move > 0);
        var confirmationMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? confirmationSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : confirmationSegment.Sum(move => Math.Max(0.0, move));

        var failReboundConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? failReboundSegment.All(move => move > 0)
            : failReboundSegment.All(move => move < 0);
        var failReboundMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? failReboundSegment.Sum(move => Math.Max(0.0, move))
            : failReboundSegment.Sum(move => Math.Abs(Math.Min(0.0, move)));

        var breakdownConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? breakdownSegment.All(move => move < 0)
            : breakdownSegment.All(move => move > 0);
        var breakdownMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? breakdownSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : breakdownSegment.Sum(move => Math.Max(0.0, move));

        var breakdownConfirmConfirmed = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? breakdownConfirmSegment.All(move => move < 0)
            : breakdownConfirmSegment.All(move => move > 0);
        var breakdownConfirmMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? breakdownConfirmSegment.Sum(move => Math.Abs(Math.Min(0.0, move)))
            : breakdownConfirmSegment.Sum(move => Math.Max(0.0, move));

        if (!adverseConfirmed
            || adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct)
            || !reboundConfirmed
            || reboundMovePct < Math.Max(0.0, _config.MinReboundMovePct)
            || !pullbackConfirmed
            || pullbackMovePct < Math.Max(0.0, _config.MinPullbackMovePct)
            || !recoveryConfirmed
            || recoveryMovePct < Math.Max(0.0, _config.MinRecoveryMovePct)
            || !rejectionConfirmed
            || rejectionMovePct < Math.Max(0.0, _config.MinRejectionMovePct)
            || !confirmationConfirmed
            || confirmationMovePct < Math.Max(0.0, _config.MinConfirmationMovePct)
            || !failReboundConfirmed
            || failReboundMovePct > Math.Max(0.0, _config.MaxFailReboundMovePct)
            || !breakdownConfirmed
            || breakdownMovePct < Math.Max(0.0, _config.MinBreakdownMovePct)
            || !breakdownConfirmConfirmed
            || breakdownConfirmMovePct < Math.Max(0.0, _config.MinBreakdownConfirmMovePct))
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
                Source: $"trade-management:{StrategyId}:breakdown-confirm-cancel",
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
                Source: $"trade-management:{StrategyId}:breakdown-confirm-flatten",
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
