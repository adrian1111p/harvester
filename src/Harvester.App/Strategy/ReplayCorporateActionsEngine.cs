using System.Text.Json;

namespace Harvester.App.Strategy;

public enum ReplayPriceNormalizationMode
{
    Raw,
    SplitAdjusted,
    TotalReturn
}

public sealed record ReplayCorporateActionRow(
    DateTime EffectiveTimestampUtc,
    string Symbol,
    string ActionType,
    double SplitRatio,
    double CashAmount,
    string Source
);

public sealed class ReplayCorporateActionsEngine
{
    public static IReadOnlyList<ReplayCorporateActionRow> LoadCorporateActions(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay corporate actions input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplayCorporateActionRow[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(x => x.EffectiveTimestampUtc != default)
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => x with
            {
                Symbol = x.Symbol.ToUpperInvariant(),
                ActionType = x.ActionType.ToUpperInvariant(),
                Source = string.IsNullOrWhiteSpace(x.Source) ? "external" : x.Source
            })
            .OrderBy(x => x.EffectiveTimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }

    public static ReplayPriceNormalizationMode ParseNormalizationMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "raw" => ReplayPriceNormalizationMode.Raw,
            "split-adjusted" => ReplayPriceNormalizationMode.SplitAdjusted,
            "splitadjusted" => ReplayPriceNormalizationMode.SplitAdjusted,
            "total-return" => ReplayPriceNormalizationMode.TotalReturn,
            "totalreturn" => ReplayPriceNormalizationMode.TotalReturn,
            _ => throw new ArgumentException($"Unknown replay price normalization mode '{value}'. Use raw|split-adjusted|total-return.")
        };
    }

    public static IReadOnlyList<StrategyReplayInputRow> NormalizeRows(
        IReadOnlyList<StrategyReplayInputRow> rows,
        IReadOnlyList<ReplayCorporateActionRow> corporateActions,
        ReplayPriceNormalizationMode mode)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        if (mode == ReplayPriceNormalizationMode.Raw || corporateActions.Count == 0)
        {
            return rows;
        }

        var ordered = rows
            .OrderBy(x => x.TimestampUtc)
            .ToArray();

        var splitActionsBySymbol = corporateActions
            .Where(x => x.ActionType == "SPLIT" && x.SplitRatio > 0)
            .GroupBy(x => x.Symbol)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.EffectiveTimestampUtc).ToArray());

        var symbolFactors = splitActionsBySymbol.ToDictionary(
            kvp => kvp.Key,
            _ => 1.0);
        var symbolActionIndex = splitActionsBySymbol.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Length - 1);

        var normalized = new StrategyReplayInputRow[ordered.Length];
        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            var row = ordered[index];
            var symbol = row.Symbol.ToUpperInvariant();
            var factor = symbolFactors.TryGetValue(symbol, out var currentFactor) ? currentFactor : 1.0;

            var safeFactor = factor <= 0 ? 1.0 : factor;
            normalized[index] = row with
            {
                Symbol = symbol,
                Open = row.Open / safeFactor,
                High = row.High / safeFactor,
                Low = row.Low / safeFactor,
                Close = row.Close / safeFactor,
                Volume = row.Volume * (decimal)safeFactor
            };

            if (!splitActionsBySymbol.TryGetValue(symbol, out var actions))
            {
                continue;
            }

            var actionCursor = symbolActionIndex[symbol];
            while (actionCursor >= 0 && actions[actionCursor].EffectiveTimestampUtc >= row.TimestampUtc)
            {
                var ratio = actions[actionCursor].SplitRatio;
                if (ratio > 0)
                {
                    symbolFactors[symbol] *= ratio;
                }
                actionCursor--;
            }

            symbolActionIndex[symbol] = actionCursor;
        }

        return normalized;
    }
}
