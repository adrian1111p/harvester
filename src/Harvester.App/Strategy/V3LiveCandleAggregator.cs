using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

/// <summary>
/// Maintains streaming candle aggregation across multiple timeframes (1m, 5m, 15m, 1h, 1D)
/// per symbol. Each timeframe retains a rolling history of completed candles for use in
/// indicator computation and exit monitoring. Fed from L1 tick data and historical bars.
/// </summary>
public sealed class V3LiveCandleAggregator
{
    public static readonly int[] TimeframeSeconds = [60, 300, 900, 3600, 86400];
    public static readonly string[] TimeframeLabels = ["1m", "5m", "15m", "1h", "1D"];

    /// <summary>Max completed candles to retain per timeframe (rolling window).</summary>
    private const int MaxHistoryPerTimeframe = 500;

    private readonly Dictionary<string, SymbolCandleState> _stateBySymbol = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Update from a live L1 tick (price update).
    /// </summary>
    public void UpdateFromTick(string symbol, DateTime timestampUtc, double price, double tickVolume = 0)
    {
        if (string.IsNullOrWhiteSpace(symbol) || price <= 0 || timestampUtc == default) return;

        var state = GetOrCreateState(symbol);
        var tsUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);

        foreach (var tfSec in TimeframeSeconds)
        {
            var bucketStart = AlignToBucketStart(tsUtc, tfSec);
            if (!state.CurrentByTimeframe.TryGetValue(tfSec, out var current))
            {
                state.CurrentByTimeframe[tfSec] = new LiveCandle(bucketStart, price, price, price, price, tickVolume, 1);
                continue;
            }

            if (bucketStart != current.BucketStartUtc)
            {
                // Previous candle completed — archive it
                AppendToHistory(state, tfSec, current);
                state.CurrentByTimeframe[tfSec] = new LiveCandle(bucketStart, price, price, price, price, tickVolume, 1);
                continue;
            }

            state.CurrentByTimeframe[tfSec] = current with
            {
                High = Math.Max(current.High, price),
                Low = Math.Min(current.Low, price),
                Close = price,
                Volume = current.Volume + tickVolume,
                Samples = current.Samples + 1
            };
        }
    }

    /// <summary>
    /// Seed / update from an IBKR historical bar (typically 1m bars from reqHistoricalData).
    /// </summary>
    public void UpdateFromHistoricalBar(string symbol, HistoricalBarRow bar)
    {
        if (string.IsNullOrWhiteSpace(symbol) || bar.Close <= 0 || bar.TimestampUtc == default) return;

        var state = GetOrCreateState(symbol);
        var tsUtc = DateTime.SpecifyKind(bar.TimestampUtc, DateTimeKind.Utc);
        var open = bar.Open > 0 ? bar.Open : bar.Close;
        var high = bar.High > 0 ? bar.High : bar.Close;
        var low = bar.Low > 0 ? bar.Low : bar.Close;
        var vol = (double)bar.Volume;

        foreach (var tfSec in TimeframeSeconds)
        {
            var bucketStart = AlignToBucketStart(tsUtc, tfSec);
            if (!state.CurrentByTimeframe.TryGetValue(tfSec, out var current))
            {
                state.CurrentByTimeframe[tfSec] = new LiveCandle(bucketStart, open, high, low, bar.Close, vol, 1);
                continue;
            }

            if (bucketStart != current.BucketStartUtc)
            {
                AppendToHistory(state, tfSec, current);
                state.CurrentByTimeframe[tfSec] = new LiveCandle(bucketStart, open, high, low, bar.Close, vol, 1);
                continue;
            }

            state.CurrentByTimeframe[tfSec] = current with
            {
                High = Math.Max(current.High, high),
                Low = Math.Min(current.Low, low),
                Close = bar.Close,
                Volume = current.Volume + vol,
                Samples = current.Samples + 1
            };
        }
    }

    /// <summary>
    /// Get a snapshot of all timeframe candles for a symbol, including completed history
    /// and the current (in-progress) candle for each timeframe.
    /// </summary>
    public V3LiveCandleSnapshot? GetSnapshot(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!_stateBySymbol.TryGetValue(normalized, out var state)) return null;

        var timeframes = new Dictionary<int, V3LiveTimeframeCandleData>();
        foreach (var tfSec in TimeframeSeconds)
        {
            var history = state.HistoryByTimeframe.TryGetValue(tfSec, out var h)
                ? h.ToArray()
                : Array.Empty<LiveCandle>();

            var current = state.CurrentByTimeframe.TryGetValue(tfSec, out var c) ? c : null;

            timeframes[tfSec] = new V3LiveTimeframeCandleData(
                TimeframeSeconds: tfSec,
                CompletedCandles: history,
                CurrentCandle: current);
        }

        return new V3LiveCandleSnapshot(normalized, DateTime.UtcNow, timeframes);
    }

    /// <summary>
    /// Get completed candle history for a specific symbol and timeframe as an array.
    /// Includes the current in-progress candle as the last element.
    /// </summary>
    public LiveCandle[] GetCandlesWithCurrent(string symbol, int timeframeSeconds)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!_stateBySymbol.TryGetValue(normalized, out var state)) return [];

        var history = state.HistoryByTimeframe.TryGetValue(timeframeSeconds, out var h)
            ? h.ToArray()
            : Array.Empty<LiveCandle>();

        if (!state.CurrentByTimeframe.TryGetValue(timeframeSeconds, out var current))
            return history;

        var result = new LiveCandle[history.Length + 1];
        Array.Copy(history, result, history.Length);
        result[^1] = current;
        return result;
    }

    /// <summary>
    /// Get the MTF (multi-timeframe) directional alignment snapshot for exit monitoring.
    /// </summary>
    public V3LiveMtfAlignment GetMtfAlignment(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!_stateBySymbol.TryGetValue(normalized, out var state))
            return V3LiveMtfAlignment.Empty;

        var bullCount = 0;
        var bearCount = 0;
        var total = 0;
        var details = new Dictionary<int, V3LiveMtfTimeframeStatus>();

        foreach (var tfSec in TimeframeSeconds)
        {
            // Use last completed candle for alignment (not the in-progress one)
            LiveCandle? candle = null;
            if (state.HistoryByTimeframe.TryGetValue(tfSec, out var history) && history.Count > 0)
                candle = history[^1];
            else if (state.CurrentByTimeframe.TryGetValue(tfSec, out var current))
                candle = current;

            if (candle is null)
            {
                details[tfSec] = new V3LiveMtfTimeframeStatus(tfSec, false, false, false);
                continue;
            }

            var isBullish = candle.Close > candle.Open;
            var isBearish = candle.Close < candle.Open;
            if (isBullish) bullCount++;
            if (isBearish) bearCount++;
            total++;

            details[tfSec] = new V3LiveMtfTimeframeStatus(tfSec, true, isBullish, isBearish);
        }

        return new V3LiveMtfAlignment(
            HasAllTimeframes: total == TimeframeSeconds.Length,
            BullishCount: bullCount,
            BearishCount: bearCount,
            TotalTimeframes: total,
            AllBullish: bullCount == total && total > 0,
            AllBearish: bearCount == total && total > 0,
            ShortTermBearish: IsShortTermBearish(state),
            ShortTermBullish: IsShortTermBullish(state),
            TimeframeDetails: details);
    }

    private bool IsShortTermBearish(SymbolCandleState state)
    {
        // 1m + 5m + 15m all bearish (last completed candle)
        int[] shortTfs = [60, 300, 900];
        foreach (var tf in shortTfs)
        {
            if (!state.HistoryByTimeframe.TryGetValue(tf, out var h) || h.Count == 0) return false;
            if (h[^1].Close >= h[^1].Open) return false;
        }
        return true;
    }

    private bool IsShortTermBullish(SymbolCandleState state)
    {
        int[] shortTfs = [60, 300, 900];
        foreach (var tf in shortTfs)
        {
            if (!state.HistoryByTimeframe.TryGetValue(tf, out var h) || h.Count == 0) return false;
            if (h[^1].Close <= h[^1].Open) return false;
        }
        return true;
    }

    private SymbolCandleState GetOrCreateState(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (!_stateBySymbol.TryGetValue(normalized, out var state))
        {
            state = new SymbolCandleState();
            _stateBySymbol[normalized] = state;
        }
        return state;
    }

    private static void AppendToHistory(SymbolCandleState state, int timeframeSeconds, LiveCandle completedCandle)
    {
        if (!state.HistoryByTimeframe.TryGetValue(timeframeSeconds, out var list))
        {
            list = [];
            state.HistoryByTimeframe[timeframeSeconds] = list;
        }

        list.Add(completedCandle);

        // Trim to max history size
        while (list.Count > MaxHistoryPerTimeframe)
            list.RemoveAt(0);
    }

    private static DateTime AlignToBucketStart(DateTime timestampUtc, int bucketSeconds)
    {
        var epochSeconds = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
        var aligned = epochSeconds - (epochSeconds % Math.Max(1, bucketSeconds));
        return DateTime.UnixEpoch.AddSeconds(aligned);
    }

    private sealed class SymbolCandleState
    {
        public Dictionary<int, LiveCandle> CurrentByTimeframe { get; } = [];
        public Dictionary<int, List<LiveCandle>> HistoryByTimeframe { get; } = [];
    }
}

// ─── Records ────────────────────────────────────────────────────────────────

/// <summary>A single candle (complete or in-progress) at any timeframe.</summary>
public sealed record LiveCandle(
    DateTime BucketStartUtc,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    int Samples);

/// <summary>Candle data for one timeframe: completed history + current in-progress.</summary>
public sealed record V3LiveTimeframeCandleData(
    int TimeframeSeconds,
    IReadOnlyList<LiveCandle> CompletedCandles,
    LiveCandle? CurrentCandle);

/// <summary>Full multi-timeframe candle snapshot for a symbol.</summary>
public sealed record V3LiveCandleSnapshot(
    string Symbol,
    DateTime TimestampUtc,
    IReadOnlyDictionary<int, V3LiveTimeframeCandleData> Timeframes);

/// <summary>Multi-timeframe alignment status for a single timeframe.</summary>
public sealed record V3LiveMtfTimeframeStatus(
    int TimeframeSeconds,
    bool HasData,
    bool IsBullish,
    bool IsBearish);

/// <summary>Aggregated multi-timeframe directional alignment for exit monitoring.</summary>
public sealed record V3LiveMtfAlignment(
    bool HasAllTimeframes,
    int BullishCount,
    int BearishCount,
    int TotalTimeframes,
    bool AllBullish,
    bool AllBearish,
    bool ShortTermBearish,
    bool ShortTermBullish,
    IReadOnlyDictionary<int, V3LiveMtfTimeframeStatus> TimeframeDetails)
{
    public static readonly V3LiveMtfAlignment Empty = new(
        false, 0, 0, 0, false, false, false, false,
        new Dictionary<int, V3LiveMtfTimeframeStatus>());
}
