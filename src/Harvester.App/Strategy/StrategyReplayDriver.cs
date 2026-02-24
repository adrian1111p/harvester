using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record StrategyReplayInputRow(
    DateTime TimestampUtc,
    string Symbol,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume
);

public sealed class StrategyReplayDriver
{
    public IReadOnlyList<StrategyDataSlice> LoadSlices(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new InvalidOperationException("strategy-replay requires --replay-input path to a JSON dataset.");
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<StrategyReplayInputRow[]>(File.ReadAllText(fullPath)) ?? [];
        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Replay input is empty.");
        }

        var ordered = rows
            .Where(r => r.TimestampUtc != default)
            .OrderBy(r => r.TimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();

        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("Replay input did not contain valid TimestampUtc rows.");
        }

        return ordered
            .Select((row, index) => BuildSlice(row, index))
            .ToArray();
    }

    private static StrategyDataSlice BuildSlice(StrategyReplayInputRow row, int index)
    {
        var topTick = new TopTickRow(
            row.TimestampUtc,
            800000 + index,
            "replay",
            4,
            row.Close,
            (int)Math.Max(0, Math.Min(int.MaxValue, row.Volume)),
            row.Close.ToString("F4"));

        var historicalBar = new HistoricalBarRow(
            row.TimestampUtc,
            810000 + index,
            row.TimestampUtc.ToString("yyyyMMdd HH:mm:ss"),
            row.Open,
            row.High,
            row.Low,
            row.Close,
            row.Volume,
            row.Close,
            1);

        return new StrategyDataSlice(
            row.TimestampUtc,
            "StrategyReplay",
            [topTick],
            [historicalBar],
            [],
            [],
            []);
    }
}
