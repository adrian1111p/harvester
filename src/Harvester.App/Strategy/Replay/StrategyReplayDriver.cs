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
    decimal Volume,
    double Bid = 0,
    double Ask = 0
);

public sealed class StrategyReplayDriver
{
    public IReadOnlyList<StrategyDataSlice> LoadSlices(
        string inputPath,
        int maxRows,
        IReadOnlyList<ReplayCorporateActionRow> corporateActions,
        ReplayPriceNormalizationMode normalizationMode)
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

        var normalized = ReplayCorporateActionsEngine.NormalizeRows(ordered, corporateActions, normalizationMode);

        return normalized
            .Select((row, index) => BuildSlice(row, index))
            .ToArray();
    }

    private static StrategyDataSlice BuildSlice(StrategyReplayInputRow row, int index)
    {
        var topTicks = new List<TopTickRow>();

        if (row.Bid > 0)
        {
            topTicks.Add(new TopTickRow(
                row.TimestampUtc,
                790000 + index,
                row.Symbol,
                1,
                row.Bid,
                (int)Math.Max(0, Math.Min(int.MaxValue, row.Volume)),
                row.Bid.ToString("F4")));
        }

        if (row.Ask > 0)
        {
            topTicks.Add(new TopTickRow(
                row.TimestampUtc,
                795000 + index,
                row.Symbol,
                2,
                row.Ask,
                (int)Math.Max(0, Math.Min(int.MaxValue, row.Volume)),
                row.Ask.ToString("F4")));
        }

        topTicks.Add(new TopTickRow(
            row.TimestampUtc,
            800000 + index,
            row.Symbol,
            4,
            row.Close,
            (int)Math.Max(0, Math.Min(int.MaxValue, row.Volume)),
            row.Close.ToString("F4")));

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
            topTicks,
            [],
            [historicalBar],
            [],
            [],
            []);
    }
}
