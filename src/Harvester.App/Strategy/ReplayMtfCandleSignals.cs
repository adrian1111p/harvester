namespace Harvester.App.Strategy;

public sealed record ReplayMtfSignalSnapshot(
    DateTime LastUpdateUtc,
    bool HasAllTimeframes,
    bool BullishEntryReady,
    bool BearishEntryReady,
    bool ExitLongSignal,
    bool ExitShortSignal
);

public interface IReplayMtfSignalSource
{
    bool TryGetSnapshot(string symbol, out ReplayMtfSignalSnapshot snapshot);
}

public sealed class ReplayMtfCandleSignalEngine : IReplayMtfSignalSource
{
    private static readonly int[] TimeframesSeconds = [30, 60, 300, 900, 3600, 86400];

    private readonly Dictionary<string, SymbolState> _stateBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public void Update(string symbol, DateTime timestampUtc, double markPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol) || markPrice <= 0 || timestampUtc == default)
        {
            return;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (!_stateBySymbol.TryGetValue(normalizedSymbol, out var state))
        {
            state = new SymbolState();
            _stateBySymbol[normalizedSymbol] = state;
        }

        var tsUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        foreach (var timeframeSeconds in TimeframesSeconds)
        {
            var bucketStartUtc = AlignToBucketStart(tsUtc, timeframeSeconds);
            if (!state.CurrentByTimeframe.TryGetValue(timeframeSeconds, out var current))
            {
                state.CurrentByTimeframe[timeframeSeconds] = new CandleState(
                    BucketStartUtc: bucketStartUtc,
                    Open: markPrice,
                    High: markPrice,
                    Low: markPrice,
                    Close: markPrice,
                    Samples: 1);
                continue;
            }

            if (bucketStartUtc != current.BucketStartUtc)
            {
                state.LastCompletedByTimeframe[timeframeSeconds] = current;
                state.CurrentByTimeframe[timeframeSeconds] = new CandleState(
                    BucketStartUtc: bucketStartUtc,
                    Open: markPrice,
                    High: markPrice,
                    Low: markPrice,
                    Close: markPrice,
                    Samples: 1);
                continue;
            }

            state.CurrentByTimeframe[timeframeSeconds] = current with
            {
                High = Math.Max(current.High, markPrice),
                Low = Math.Min(current.Low, markPrice),
                Close = markPrice,
                Samples = current.Samples + 1
            };
        }

        state.LastSnapshot = BuildSnapshot(tsUtc, state.LastCompletedByTimeframe);
    }

    public bool TryGetSnapshot(string symbol, out ReplayMtfSignalSnapshot snapshot)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)
            || !_stateBySymbol.TryGetValue(normalized, out var state)
            || state.LastSnapshot is null)
        {
            snapshot = new ReplayMtfSignalSnapshot(DateTime.MinValue, false, false, false, false, false);
            return false;
        }

        snapshot = state.LastSnapshot;
        return true;
    }

    private static ReplayMtfSignalSnapshot BuildSnapshot(
        DateTime timestampUtc,
        IReadOnlyDictionary<int, CandleState> completedByTimeframe)
    {
        var hasAll = TimeframesSeconds.All(completedByTimeframe.ContainsKey);
        if (!hasAll)
        {
            return new ReplayMtfSignalSnapshot(timestampUtc, false, false, false, false, false);
        }

        var c30s = completedByTimeframe[30];
        var c1m = completedByTimeframe[60];
        var c5m = completedByTimeframe[300];
        var c15m = completedByTimeframe[900];
        var c1h = completedByTimeframe[3600];
        var c1d = completedByTimeframe[86400];

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

        return new ReplayMtfSignalSnapshot(
            LastUpdateUtc: timestampUtc,
            HasAllTimeframes: true,
            BullishEntryReady: bullishEntry,
            BearishEntryReady: bearishEntry,
            ExitLongSignal: exitLong,
            ExitShortSignal: exitShort);
    }

    private static bool IsBull(CandleState candle)
    {
        return candle.Close > candle.Open;
    }

    private static bool IsBear(CandleState candle)
    {
        return candle.Close < candle.Open;
    }

    private static DateTime AlignToBucketStart(DateTime timestampUtc, int bucketSeconds)
    {
        var epochSeconds = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
        var aligned = epochSeconds - (epochSeconds % Math.Max(1, bucketSeconds));
        return DateTime.UnixEpoch.AddSeconds(aligned);
    }

    private sealed class SymbolState
    {
        public Dictionary<int, CandleState> CurrentByTimeframe { get; } = [];
        public Dictionary<int, CandleState> LastCompletedByTimeframe { get; } = [];
        public ReplayMtfSignalSnapshot? LastSnapshot { get; set; }
    }

    private sealed record CandleState(
        DateTime BucketStartUtc,
        double Open,
        double High,
        double Low,
        double Close,
        int Samples
    );
}
