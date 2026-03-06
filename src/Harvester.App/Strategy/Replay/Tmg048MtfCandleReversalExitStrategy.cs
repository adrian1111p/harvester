using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg048MtfCandleReversalExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_048_MTF_CANDLE_REVERSAL_EXIT";

    private readonly Tmg048MtfCandleReversalExitConfig _config;
    private readonly IReplayMtfSignalSource _mtfSignalSource;
    private readonly HashSet<string> _triggeredSymbols;

    public Tmg048MtfCandleReversalExitStrategy(
        IReplayMtfSignalSource mtfSignalSource,
        Tmg048MtfCandleReversalExitConfig? config = null)
    {
        _mtfSignalSource = mtfSignalSource;
        _config = config ?? Tmg048MtfCandleReversalExitConfig.Default;
        _triggeredSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _triggeredSymbols.Remove(symbol);
            return [];
        }

        if (_triggeredSymbols.Contains(symbol))
        {
            return [];
        }

        if (!_mtfSignalSource.TryGetSnapshot(symbol, out var snapshot))
        {
            return [];
        }

        if (_config.RequireAllTimeframes && !snapshot.HasAllTimeframes)
        {
            return [];
        }

        var isLong = context.PositionQuantity > 0;
        var isShort = context.PositionQuantity < 0;
        var shouldExit = (isLong && snapshot.ExitLongSignal) || (isShort && snapshot.ExitShortSignal);
        if (!shouldExit)
        {
            return [];
        }

        _triggeredSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = isLong ? "SELL" : "BUY";
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
                Source: $"trade-management:{StrategyId}:mtf-reversal-cancel",
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
                Source: $"trade-management:{StrategyId}:mtf-reversal-flatten",
                Route: _config.FlattenRoute)
        ];
    }
}
