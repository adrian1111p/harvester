using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

public sealed class V3LiveOrderBridge
{
    private readonly V3LiveConfig _config;

    public V3LiveOrderBridge(V3LiveConfig config)
    {
        _config = config;
    }

    public V3LiveProposedOrder? BuildOrder(
        string symbol,
        DateTime timestampUtc,
        TradeSide side,
        V3LiveFeatureSnapshot features,
        string setup)
    {
        var entryPrice = ResolveEntryPrice(features, side);
        if (entryPrice <= 0) return null;

        var stopDist = _config.HardStopR * features.Atr14;
        if (double.IsNaN(stopDist) || stopDist <= 0) return null;

        var stopPrice = side == TradeSide.Long
            ? entryPrice - stopDist
            : entryPrice + stopDist;

        var riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare < _config.MinRiskPerShare) return null;

        var qtyByRisk = Math.Max(1, (int)Math.Floor(_config.RiskPerTradeDollars / riskPerShare));
        var maxNotional = _config.AccountSize * _config.MaxPositionNotionalPctOfAccount;
        var qtyByNotional = entryPrice > 0 ? Math.Max(1, (int)Math.Floor(maxNotional / entryPrice)) : _config.MaxShares;
        var quantity = Math.Max(1, Math.Min(_config.MaxShares, Math.Min(qtyByRisk, qtyByNotional)));

        var takeProfitPrice = side == TradeSide.Long
            ? entryPrice + _config.Tp2R * features.Atr14
            : entryPrice - _config.Tp2R * features.Atr14;

        var estimatedRisk = riskPerShare * quantity;
        var action = side == TradeSide.Long ? "BUY" : "SELL";

        return new V3LiveProposedOrder(
            IntentId: $"V11LIVE-{symbol}-{timestampUtc:yyyyMMddHHmmssfff}",
            TimestampUtc: timestampUtc,
            Symbol: symbol,
            Side: action,
            OrderType: "MKT",
            TimeInForce: "IOC",
            Quantity: quantity,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            TakeProfitPrice: takeProfitPrice,
            EstimatedRiskDollars: estimatedRisk,
            Setup: setup,
            Source: "v11-live-runtime");
    }

    private static double ResolveEntryPrice(V3LiveFeatureSnapshot features, TradeSide side)
    {
        if (features.L1.HasQuote)
        {
            return side == TradeSide.Long ? features.L1.Ask : features.L1.Bid;
        }

        return features.Price;
    }
}

public sealed record V3LiveProposedOrder(
    string IntentId,
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    string TimeInForce,
    int Quantity,
    double EntryPrice,
    double StopPrice,
    double TakeProfitPrice,
    double EstimatedRiskDollars,
    string Setup,
    string Source);
