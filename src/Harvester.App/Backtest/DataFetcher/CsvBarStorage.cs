using System.Globalization;
using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.DataFetcher;

/// <summary>
/// CSV-based bar storage for backtest data.
/// Reads/writes OHLCV bars in the same format as the Python data_fetcher.py.
/// Storage path: backtest/data/{SYMBOL}/{timeframe}.csv
/// </summary>
public static class CsvBarStorage
{
    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "backtest", "data");

    /// <summary>Resolve the base data directory (supports both bin/ and repo root execution).</summary>
    public static string ResolveDataDir()
    {
        // Try relative from binary output (bin/Debug/net9.0/)
        var fromBin = Path.GetFullPath(DataDir);
        if (Directory.Exists(fromBin)) return fromBin;

        // Try from repo root
        var fromRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backtest", "data"));
        if (Directory.Exists(fromRoot)) return fromRoot;

        // Fallback: create at working directory
        var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backtest", "data"));
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>Check if data exists for a symbol + timeframe.</summary>
    public static bool Exists(string symbol, string timeframe)
    {
        var path = GetCsvPath(symbol, timeframe);
        return File.Exists(path);
    }

    /// <summary>Load previously saved CSV data. Returns BacktestBar[].</summary>
    public static BacktestBar[] LoadBars(string symbol, string timeframe)
    {
        var path = GetCsvPath(symbol, timeframe);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No data at {path}");

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return [];

        // Parse header to find column indices
        var header = lines[0].Split(',');
        int tsIdx = Array.FindIndex(header, h => h.Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
        int oIdx = Array.FindIndex(header, h => h.Trim().Equals("Open", StringComparison.OrdinalIgnoreCase));
        int hIdx = Array.FindIndex(header, h => h.Trim().Equals("High", StringComparison.OrdinalIgnoreCase));
        int lIdx = Array.FindIndex(header, h => h.Trim().Equals("Low", StringComparison.OrdinalIgnoreCase));
        int cIdx = Array.FindIndex(header, h => h.Trim().Equals("Close", StringComparison.OrdinalIgnoreCase));
        int vIdx = Array.FindIndex(header, h => h.Trim().Equals("Volume", StringComparison.OrdinalIgnoreCase));

        // If Timestamp is the index column (first col without header "Timestamp"),
        // it may be at position 0 unnamed
        if (tsIdx < 0) tsIdx = 0;

        var bars = new List<BacktestBar>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 5) continue;

            if (!DateTime.TryParse(parts[tsIdx], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts))
                continue;

            double open = double.Parse(parts[oIdx >= 0 ? oIdx : 1], CultureInfo.InvariantCulture);
            double high = double.Parse(parts[hIdx >= 0 ? hIdx : 2], CultureInfo.InvariantCulture);
            double low = double.Parse(parts[lIdx >= 0 ? lIdx : 3], CultureInfo.InvariantCulture);
            double close = double.Parse(parts[cIdx >= 0 ? cIdx : 4], CultureInfo.InvariantCulture);
            double volume = double.Parse(parts[vIdx >= 0 ? vIdx : 5], CultureInfo.InvariantCulture);

            bars.Add(new BacktestBar(ts, open, high, low, close, volume));
        }

        return bars.OrderBy(b => b.Timestamp).ToArray();
    }

    /// <summary>Save bars as CSV in backtest/data/{SYMBOL}/{timeframe}.csv.</summary>
    public static void SaveBars(string symbol, string timeframe, BacktestBar[] bars)
    {
        var dir = Path.Combine(ResolveDataDir(), symbol.ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{timeframe}.csv");

        using var writer = new StreamWriter(path);
        writer.WriteLine("Timestamp,Open,High,Low,Close,Volume");
        foreach (var b in bars.OrderBy(x => x.Timestamp))
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:sszzz},{1},{2},{3},{4},{5}",
                b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume));
        }
        Console.WriteLine($"  Saved {path} ({bars.Length} rows)");
    }

    /// <summary>List available symbols (directories in data/).</summary>
    public static string[] ListSymbols()
    {
        var dataDir = ResolveDataDir();
        if (!Directory.Exists(dataDir)) return [];
        return Directory.GetDirectories(dataDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .ToArray();
    }

    /// <summary>List available timeframes for a symbol.</summary>
    public static string[] ListTimeframes(string symbol)
    {
        var symDir = Path.Combine(ResolveDataDir(), symbol.ToUpperInvariant());
        if (!Directory.Exists(symDir)) return [];
        return Directory.GetFiles(symDir, "*.csv")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToArray();
    }

    private static string GetCsvPath(string symbol, string timeframe)
    {
        return Path.Combine(ResolveDataDir(), symbol.ToUpperInvariant(), $"{timeframe}.csv");
    }
}
