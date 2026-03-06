namespace Harvester.App.Strategy;

public sealed record ReplayHistoricalCandlestickRow(
    string Symbol,
    string Timeframe,
    DateTime BucketStartUtc,
    DateTime BucketEndUtc,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume,
    int Samples,
    string Source
);

public sealed record ReplayScannerHistoricalEvaluationRow(
    DateTime TimestampUtc,
    string Symbol,
    double WeightedScore,
    bool Eligible,
    double AverageRank,
    bool HasAllTimeframes,
    bool BullishEntryReady,
    bool BearishEntryReady,
    bool ExitLongSignal,
    bool ExitShortSignal,
    DateTime LastUpdateUtc
);

public static class ReplayHistoricalCandlestickCharts
{
    private static readonly (string Label, int Seconds)[] Timeframes =
    [
        ("30s", 30),
        ("1m", 60),
        ("5m", 300),
        ("15m", 900),
        ("1h", 3600),
        ("1D", 86400)
    ];

    public static IReadOnlyList<ReplayHistoricalCandlestickRow> BuildFromSlices(IReadOnlyList<StrategyDataSlice> slices)
    {
        if (slices.Count == 0)
        {
            return [];
        }

        var buckets = new Dictionary<(string Symbol, string Timeframe, DateTime BucketStartUtc), CandleAccumulator>();

        foreach (var slice in slices)
        {
            var symbol = (slice.TopTicks.FirstOrDefault()?.Kind ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol) || slice.HistoricalBars.Count == 0)
            {
                continue;
            }

            foreach (var bar in slice.HistoricalBars)
            {
                if (bar.TimestampUtc == default || bar.Close <= 0)
                {
                    continue;
                }

                var barTimestamp = DateTime.SpecifyKind(bar.TimestampUtc, DateTimeKind.Utc);
                var open = bar.Open > 0 ? bar.Open : bar.Close;
                var high = bar.High > 0 ? bar.High : bar.Close;
                var low = bar.Low > 0 ? bar.Low : bar.Close;
                var close = bar.Close;
                var volume = bar.Volume > 0 ? bar.Volume : 0;

                foreach (var timeframe in Timeframes)
                {
                    var bucketStartUtc = AlignToBucketStart(barTimestamp, timeframe.Seconds);
                    var key = (symbol, timeframe.Label, bucketStartUtc);

                    if (!buckets.TryGetValue(key, out var current))
                    {
                        buckets[key] = new CandleAccumulator(
                            Symbol: symbol,
                            Timeframe: timeframe.Label,
                            BucketStartUtc: bucketStartUtc,
                            Open: open,
                            High: high,
                            Low: low,
                            Close: close,
                            Volume: volume,
                            Samples: 1);
                        continue;
                    }

                    buckets[key] = current with
                    {
                        High = Math.Max(current.High, high),
                        Low = Math.Min(current.Low, low),
                        Close = close,
                        Volume = current.Volume + volume,
                        Samples = current.Samples + 1
                    };
                }
            }
        }

        return buckets.Values
            .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BucketStartUtc)
            .ThenBy(x => x.Timeframe, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ReplayHistoricalCandlestickRow(
                Symbol: x.Symbol,
                Timeframe: x.Timeframe,
                BucketStartUtc: x.BucketStartUtc,
                BucketEndUtc: x.BucketStartUtc.AddSeconds(GetTimeframeSeconds(x.Timeframe)),
                Open: x.Open,
                High: x.High,
                Low: x.Low,
                Close: x.Close,
                Volume: x.Volume,
                Samples: x.Samples,
                Source: "historical-bars"))
            .ToArray();
    }

    public static IReadOnlyList<ReplayScannerHistoricalEvaluationRow> BuildScannerEvaluations(
        IReadOnlyList<ReplayHistoricalCandlestickRow> candles,
        ReplayScannerSymbolSelectionSnapshotRow selectionSnapshot)
    {
        if (candles.Count == 0 || selectionSnapshot.RankedSymbols.Count == 0)
        {
            return [];
        }

        var latestBySymbolAndTimeframe = candles
            .GroupBy(x => $"{x.Symbol}::{x.Timeframe}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.BucketStartUtc).First())
            .ToArray();

        var latestBySymbol = latestBySymbolAndTimeframe
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(x => x.Timeframe, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        return selectionSnapshot.RankedSymbols
            .Select(symbolRow =>
            {
                if (!latestBySymbol.TryGetValue(symbolRow.Symbol, out var byTimeframe))
                {
                    return new ReplayScannerHistoricalEvaluationRow(
                        TimestampUtc: DateTime.UtcNow,
                        Symbol: symbolRow.Symbol,
                        WeightedScore: symbolRow.WeightedScore,
                        Eligible: symbolRow.Eligible,
                        AverageRank: symbolRow.AverageRank,
                        HasAllTimeframes: false,
                        BullishEntryReady: false,
                        BearishEntryReady: false,
                        ExitLongSignal: false,
                        ExitShortSignal: false,
                        LastUpdateUtc: DateTime.MinValue);
                }

                var hasAll = Timeframes.All(tf => byTimeframe.ContainsKey(tf.Label));
                if (!hasAll)
                {
                    var lastUpdate = byTimeframe.Values
                        .Select(x => x.BucketEndUtc)
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Max();

                    return new ReplayScannerHistoricalEvaluationRow(
                        TimestampUtc: DateTime.UtcNow,
                        Symbol: symbolRow.Symbol,
                        WeightedScore: symbolRow.WeightedScore,
                        Eligible: symbolRow.Eligible,
                        AverageRank: symbolRow.AverageRank,
                        HasAllTimeframes: false,
                        BullishEntryReady: false,
                        BearishEntryReady: false,
                        ExitLongSignal: false,
                        ExitShortSignal: false,
                        LastUpdateUtc: lastUpdate);
                }

                var c30s = byTimeframe["30s"];
                var c1m = byTimeframe["1m"];
                var c5m = byTimeframe["5m"];
                var c15m = byTimeframe["15m"];
                var c1h = byTimeframe["1h"];
                var c1d = byTimeframe["1D"];

                var bullishEntry = IsBull(c30s)
                    && IsBull(c1m)
                    && IsBull(c5m)
                    && IsBull(c15m)
                    && IsBull(c1h)
                    && IsBull(c1d);

                var bearishEntry = IsBear(c30s)
                    && IsBear(c1m)
                    && IsBear(c5m)
                    && IsBear(c15m)
                    && IsBear(c1h)
                    && IsBear(c1d);

                var exitLong = IsBear(c30s) && IsBear(c1m) && IsBear(c5m);
                var exitShort = IsBull(c30s) && IsBull(c1m) && IsBull(c5m);
                var lastUpdateUtc = byTimeframe.Values.Max(x => x.BucketEndUtc);

                return new ReplayScannerHistoricalEvaluationRow(
                    TimestampUtc: DateTime.UtcNow,
                    Symbol: symbolRow.Symbol,
                    WeightedScore: symbolRow.WeightedScore,
                    Eligible: symbolRow.Eligible,
                    AverageRank: symbolRow.AverageRank,
                    HasAllTimeframes: true,
                    BullishEntryReady: bullishEntry,
                    BearishEntryReady: bearishEntry,
                    ExitLongSignal: exitLong,
                    ExitShortSignal: exitShort,
                    LastUpdateUtc: lastUpdateUtc);
            })
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTime AlignToBucketStart(DateTime timestampUtc, int bucketSeconds)
    {
        var epochSeconds = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
        var aligned = epochSeconds - (epochSeconds % Math.Max(1, bucketSeconds));
        return DateTime.UnixEpoch.AddSeconds(aligned);
    }

    private static int GetTimeframeSeconds(string timeframe)
    {
        return timeframe switch
        {
            "30s" => 30,
            "1m" => 60,
            "5m" => 300,
            "15m" => 900,
            "1h" => 3600,
            "1D" => 86400,
            _ => 60
        };
    }

    private static bool IsBull(ReplayHistoricalCandlestickRow candle)
    {
        return candle.Close > candle.Open;
    }

    private static bool IsBear(ReplayHistoricalCandlestickRow candle)
    {
        return candle.Close < candle.Open;
    }

    private sealed record CandleAccumulator(
        string Symbol,
        string Timeframe,
        DateTime BucketStartUtc,
        double Open,
        double High,
        double Low,
        double Close,
        decimal Volume,
        int Samples
    );
}