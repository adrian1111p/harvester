using Harvester.App.Backtest.Engine;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Backtest.DataFetcher;

/// <summary>
/// Fetches historical bars from IBKR TWS and saves as CSV.
/// Uses the existing SnapshotEWrapper/SnapshotRuntime infrastructure —
/// this is a higher-level convenience wrapper for backtest data acquisition.
/// 
/// For offline usage (no TWS), load cached CSV via CsvBarStorage directly.
/// </summary>
public static class BacktestDataFetcher
{
    /// <summary>IBKR timeframe configuration mapping (matches Python TIMEFRAME_CONFIG).</summary>
    public static readonly IReadOnlyDictionary<string, (string BarSize, string Duration, string WhatToShow)> TimeframeConfig =
        new Dictionary<string, (string, string, string)>
        {
            ["30s"] = ("30 secs", "2 D", "TRADES"),
            ["1m"] = ("1 min", "5 D", "TRADES"),
            ["5m"] = ("5 mins", "20 D", "TRADES"),
            ["15m"] = ("15 mins", "40 D", "TRADES"),
            ["1h"] = ("1 hour", "90 D", "TRADES"),
            ["1D"] = ("1 day", "365 D", "TRADES"),
        };

    /// <summary>All standard timeframe labels.</summary>
    public static readonly string[] AllTimeframes = ["30s", "1m", "5m", "15m", "1h", "1D"];

    /// <summary>
    /// Convert HistoricalBarRow (from TWS callback) to BacktestBar.
    /// </summary>
    public static BacktestBar FromHistoricalBarRow(HistoricalBarRow row)
    {
        return new BacktestBar(
            row.TimestampUtc,
            row.Open,
            row.High,
            row.Low,
            row.Close,
            (double)row.Volume);
    }

    /// <summary>
    /// Convert BacktestBar to HistoricalBarRow (for interop with replay system).
    /// </summary>
    public static HistoricalBarRow ToHistoricalBarRow(BacktestBar bar, int requestId)
    {
        return new HistoricalBarRow(
            bar.Timestamp,
            requestId,
            bar.Timestamp.ToString("yyyyMMdd HH:mm:ss"),
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            (decimal)bar.Volume,
            bar.Close,
            1);
    }

    /// <summary>
    /// Load bars from CSV cache. Falls back gracefully if file doesn't exist.
    /// </summary>
    public static BacktestBar[]? TryLoadBars(string symbol, string timeframe)
    {
        try
        {
            if (!CsvBarStorage.Exists(symbol, timeframe)) return null;
            return CsvBarStorage.LoadBars(symbol, timeframe);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Load all available timeframes for a symbol from CSV cache.
    /// Returns a dictionary: timeframe → bars.
    /// </summary>
    public static Dictionary<string, BacktestBar[]> LoadAllTimeframes(string symbol)
    {
        var result = new Dictionary<string, BacktestBar[]>();
        foreach (var tf in AllTimeframes)
        {
            var bars = TryLoadBars(symbol, tf);
            if (bars != null && bars.Length > 0)
                result[tf] = bars;
        }
        return result;
    }
}
