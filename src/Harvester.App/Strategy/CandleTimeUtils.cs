namespace Harvester.App.Strategy;

/// <summary>
/// Shared helpers for multi-timeframe candle aggregation.
/// Used by both <see cref="V3LiveCandleAggregator"/> (live) and
/// <see cref="ReplayMtfCandleSignalEngine"/> (replay) to ensure
/// bucket alignment is identical across both pipelines.
/// </summary>
internal static class CandleTimeUtils
{
    /// <summary>
    /// Align a UTC timestamp to the start of the enclosing candle bucket.
    /// For example, 14:37:22 at 300-second (5m) resolution → 14:35:00.
    /// </summary>
    public static DateTime AlignToBucketStart(DateTime timestampUtc, int bucketSeconds)
    {
        var epochSeconds = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;
        var aligned = epochSeconds - (epochSeconds % Math.Max(1, bucketSeconds));
        return DateTime.UnixEpoch.AddSeconds(aligned);
    }
}
