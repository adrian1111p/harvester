namespace Harvester.App.IBKR.Runtime;

public sealed record L2MtfSignalRow(
    DateTime TimestampUtc,
    string Symbol,
    string Signal,
    double TriggerPrice,
    string Reason,
    double SpreadBps,
    int Samples30s
);

public static class L2MtfSignalStrategy
{
    public static IReadOnlyList<L2MtfSignalRow> BuildSignals(
        string symbol,
        IReadOnlyList<L2CandlestickRow> candles,
        double maxSpreadBps = 12.0)
    {
        if (candles.Count == 0)
        {
            return [];
        }

        var symbolUpper = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbolUpper))
        {
            symbolUpper = "UNKNOWN";
        }

        var byTf = candles
            .GroupBy(row => row.Timeframe, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.BucketStartUtc).ToList(),
                StringComparer.OrdinalIgnoreCase);

        if (!byTf.TryGetValue("1m", out var oneMinute) || oneMinute.Count == 0)
        {
            return [];
        }

        if (!byTf.TryGetValue("30s", out var thirtySecond)
            || !byTf.TryGetValue("5m", out var fiveMinute)
            || !byTf.TryGetValue("15m", out var fifteenMinute)
            || !byTf.TryGetValue("1h", out var oneHour)
            || !byTf.TryGetValue("1D", out var oneDay))
        {
            return [];
        }

        var signals = new List<L2MtfSignalRow>();

        foreach (var trigger in oneMinute)
        {
            var t = trigger.BucketEndUtc;
            var c30s = GetLatestAtOrBefore(thirtySecond, t);
            var c5m = GetLatestAtOrBefore(fiveMinute, t);
            var c15m = GetLatestAtOrBefore(fifteenMinute, t);
            var c1h = GetLatestAtOrBefore(oneHour, t);
            var c1d = GetLatestAtOrBefore(oneDay, t);

            if (c30s is null || c5m is null || c15m is null || c1h is null || c1d is null)
            {
                continue;
            }

            if (c30s.Samples <= 0 || c30s.AverageSpreadBps > maxSpreadBps)
            {
                continue;
            }

            var bullishStack = IsBull(c1d) && IsBull(c1h) && IsBull(c15m) && IsBull(c5m);
            var bearishStack = IsBear(c1d) && IsBear(c1h) && IsBear(c15m) && IsBear(c5m);
            var triggerBull = IsBull(trigger) && IsBull(c30s);
            var triggerBear = IsBear(trigger) && IsBear(c30s);

            if (bullishStack && triggerBull)
            {
                signals.Add(new L2MtfSignalRow(
                    TimestampUtc: t,
                    Symbol: symbolUpper,
                    Signal: "BUY",
                    TriggerPrice: trigger.Close,
                    Reason: "MTF bullish alignment (1D/1h/15m/5m) + 1m/30s bullish trigger",
                    SpreadBps: c30s.AverageSpreadBps,
                    Samples30s: c30s.Samples));
                continue;
            }

            if (bearishStack && triggerBear)
            {
                signals.Add(new L2MtfSignalRow(
                    TimestampUtc: t,
                    Symbol: symbolUpper,
                    Signal: "SELL",
                    TriggerPrice: trigger.Close,
                    Reason: "MTF bearish alignment (1D/1h/15m/5m) + 1m/30s bearish trigger",
                    SpreadBps: c30s.AverageSpreadBps,
                    Samples30s: c30s.Samples));
            }
        }

        return signals;
    }

    private static bool IsBull(L2CandlestickRow candle)
    {
        return candle.Close > candle.Open;
    }

    private static bool IsBear(L2CandlestickRow candle)
    {
        return candle.Close < candle.Open;
    }

    private static L2CandlestickRow? GetLatestAtOrBefore(IReadOnlyList<L2CandlestickRow> candles, DateTime timestampUtc)
    {
        L2CandlestickRow? latest = null;
        foreach (var candle in candles)
        {
            if (candle.BucketEndUtc <= timestampUtc)
            {
                latest = candle;
                continue;
            }

            break;
        }

        return latest;
    }
}