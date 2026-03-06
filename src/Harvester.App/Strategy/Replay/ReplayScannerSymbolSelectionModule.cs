using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ReplayScannerSymbolSelectionModule
{
    private readonly ReplayScannerSymbolSelectionSnapshotRow _snapshot;

    public ReplayScannerSymbolSelectionModule(string candidatesInputPath, int topN, double minScore)
    {
        if (string.IsNullOrWhiteSpace(candidatesInputPath))
        {
            throw new ArgumentException("Replay scanner candidates input path is required.", nameof(candidatesInputPath));
        }

        var fullPath = Path.GetFullPath(candidatesInputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay scanner candidates input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ScannerCandidateInputRow[]>(File.ReadAllText(fullPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var ranked = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => new ReplayScannerRankedSymbolRow(
                x.Symbol.Trim().ToUpperInvariant(),
                x.WeightedScore,
                x.Eligible is not false,
                x.AverageRank))
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .ToArray();

        var selected = ranked
            .Where(x => x.Eligible)
            .Where(x => x.WeightedScore >= minScore)
            .Take(Math.Max(1, topN))
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _snapshot = new ReplayScannerSymbolSelectionSnapshotRow(
            DateTime.UtcNow,
            fullPath,
            ranked,
            selected);
    }

    public ReplayScannerSymbolSelectionSnapshotRow GetSnapshot()
    {
        return _snapshot;
    }

    private sealed class ScannerCandidateInputRow
    {
        public string Symbol { get; set; } = string.Empty;
        public double WeightedScore { get; set; }
        public bool? Eligible { get; set; }
        public double AverageRank { get; set; }
    }
}
