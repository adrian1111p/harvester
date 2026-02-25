namespace Harvester.App.IBKR.Runtime;

public sealed record L2CandlestickRow(
    string Timeframe,
    DateTime BucketStartUtc,
    DateTime BucketEndUtc,
    double Open,
    double High,
    double Low,
    double Close,
    double AverageMid,
    double AverageSpreadBps,
    int Samples
);

public static class L2CandlestickBuilder
{
    public static IReadOnlyList<L2CandlestickRow> BuildCandles(
        IReadOnlyList<DepthRow> depthRows,
        IReadOnlyList<TimeSpan> timeframes)
    {
        if (depthRows.Count == 0 || timeframes.Count == 0)
        {
            return [];
        }

        var orderedRows = depthRows
            .Where(row => row.TimestampUtc != default)
            .OrderBy(row => row.TimestampUtc)
            .ToList();
        if (orderedRows.Count == 0)
        {
            return [];
        }

        var bidLevels = new List<BookLevel>();
        var askLevels = new List<BookLevel>();
        var buckets = new Dictionary<string, CandleAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in orderedRows)
        {
            var levels = row.Side == 1 ? bidLevels : askLevels;
            ApplyBookUpdate(levels, row);

            var bestBid = TryGetBestLevelPrice(bidLevels);
            var bestAsk = TryGetBestLevelPrice(askLevels);
            if (!bestBid.HasValue || !bestAsk.HasValue)
            {
                continue;
            }

            if (bestBid.Value <= 0 || bestAsk.Value <= 0 || bestAsk.Value < bestBid.Value)
            {
                continue;
            }

            var midPrice = (bestBid.Value + bestAsk.Value) / 2.0;
            if (midPrice <= 0)
            {
                continue;
            }

            var spreadBps = ((bestAsk.Value - bestBid.Value) / midPrice) * 10_000.0;
            var timestampUtc = DateTime.SpecifyKind(row.TimestampUtc, DateTimeKind.Utc);

            foreach (var timeframe in timeframes)
            {
                var timeframeSeconds = Math.Max(1, (long)timeframe.TotalSeconds);
                var timeframeLabel = ToTimeframeLabel(timeframeSeconds);
                var bucketStartUtc = AlignToBucketStart(timestampUtc, timeframeSeconds);
                var bucketEndUtc = bucketStartUtc.AddSeconds(timeframeSeconds);
                var key = $"{timeframeLabel}:{bucketStartUtc:O}";

                if (!buckets.TryGetValue(key, out var accumulator))
                {
                    buckets[key] = new CandleAccumulator(
                        Timeframe: timeframeLabel,
                        BucketStartUtc: bucketStartUtc,
                        BucketEndUtc: bucketEndUtc,
                        Open: midPrice,
                        High: midPrice,
                        Low: midPrice,
                        Close: midPrice,
                        MidSum: midPrice,
                        SpreadBpsSum: spreadBps,
                        Samples: 1);
                    continue;
                }

                buckets[key] = accumulator with
                {
                    High = Math.Max(accumulator.High, midPrice),
                    Low = Math.Min(accumulator.Low, midPrice),
                    Close = midPrice,
                    MidSum = accumulator.MidSum + midPrice,
                    SpreadBpsSum = accumulator.SpreadBpsSum + spreadBps,
                    Samples = accumulator.Samples + 1
                };
            }
        }

        return buckets.Values
            .OrderBy(row => row.BucketStartUtc)
            .ThenBy(row => row.Timeframe, StringComparer.OrdinalIgnoreCase)
            .Select(row => new L2CandlestickRow(
                Timeframe: row.Timeframe,
                BucketStartUtc: row.BucketStartUtc,
                BucketEndUtc: row.BucketEndUtc,
                Open: row.Open,
                High: row.High,
                Low: row.Low,
                Close: row.Close,
                AverageMid: row.Samples > 0 ? row.MidSum / row.Samples : 0,
                AverageSpreadBps: row.Samples > 0 ? row.SpreadBpsSum / row.Samples : 0,
                Samples: row.Samples))
            .ToArray();
    }

    private static void ApplyBookUpdate(List<BookLevel> levels, DepthRow row)
    {
        var position = Math.Max(0, row.Position);

        if (row.Operation == 2)
        {
            if (position < levels.Count)
            {
                levels.RemoveAt(position);
            }

            return;
        }

        var level = new BookLevel(
            Price: row.Price,
            Size: row.Size,
            TimestampUtc: DateTime.SpecifyKind(row.TimestampUtc, DateTimeKind.Utc));

        if (row.Operation == 0)
        {
            if (position <= levels.Count)
            {
                levels.Insert(position, level);
            }
            else
            {
                while (levels.Count < position)
                {
                    levels.Add(new BookLevel(0, 0, DateTime.SpecifyKind(row.TimestampUtc, DateTimeKind.Utc)));
                }

                levels.Add(level);
            }

            return;
        }

        if (position < levels.Count)
        {
            levels[position] = level;
            return;
        }

        while (levels.Count <= position)
        {
            levels.Add(new BookLevel(0, 0, DateTime.SpecifyKind(row.TimestampUtc, DateTimeKind.Utc)));
        }

        levels[position] = level;
    }

    private static double? TryGetBestLevelPrice(List<BookLevel> levels)
    {
        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];
            if (level.Price > 0 && level.Size > 0)
            {
                return level.Price;
            }
        }

        return null;
    }

    private static DateTime AlignToBucketStart(DateTime timestampUtc, long bucketSizeSeconds)
    {
        var epochSeconds = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
        var alignedSeconds = epochSeconds - (epochSeconds % bucketSizeSeconds);
        return DateTime.UnixEpoch.AddSeconds(alignedSeconds);
    }

    private static string ToTimeframeLabel(long timeframeSeconds)
    {
        return timeframeSeconds switch
        {
            30 => "30s",
            60 => "1m",
            300 => "5m",
            900 => "15m",
            3600 => "1h",
            86400 => "1D",
            _ when timeframeSeconds < 60 => $"{timeframeSeconds}s",
            _ when timeframeSeconds % 3600 == 0 => $"{timeframeSeconds / 3600}h",
            _ when timeframeSeconds % 60 == 0 => $"{timeframeSeconds / 60}m",
            _ => $"{timeframeSeconds}s"
        };
    }

    private sealed record BookLevel(
        double Price,
        int Size,
        DateTime TimestampUtc
    );

    private sealed record CandleAccumulator(
        string Timeframe,
        DateTime BucketStartUtc,
        DateTime BucketEndUtc,
        double Open,
        double High,
        double Low,
        double Close,
        double MidSum,
        double SpreadBpsSum,
        int Samples
    );
}