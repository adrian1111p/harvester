using System.Text.Json;

namespace Harvester.App.Strategy;

public sealed record ReplaySymbolMappingRow(
    DateTime EffectiveTimestampUtc,
    string FromSymbol,
    string ToSymbol,
    string Source
);

public sealed record ReplayDelistEventRow(
    DateTime EffectiveTimestampUtc,
    string Symbol,
    bool IsTerminal,
    string Source
);

public sealed record ReplaySymbolEventArtifactRow(
    DateTime TimestampUtc,
    string EventType,
    string Symbol,
    string? MappedFromSymbol,
    bool IsTerminal,
    string Source
);

public sealed class ReplaySymbolEventsEngine
{
    public static IReadOnlyList<ReplaySymbolMappingRow> LoadSymbolMappings(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay symbol mappings input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplaySymbolMappingRow[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(x => x.EffectiveTimestampUtc != default)
            .Where(x => !string.IsNullOrWhiteSpace(x.FromSymbol))
            .Where(x => !string.IsNullOrWhiteSpace(x.ToSymbol))
            .Select(x => x with
            {
                FromSymbol = x.FromSymbol.ToUpperInvariant(),
                ToSymbol = x.ToSymbol.ToUpperInvariant(),
                Source = string.IsNullOrWhiteSpace(x.Source) ? "external" : x.Source
            })
            .OrderBy(x => x.EffectiveTimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }

    public static IReadOnlyList<ReplayDelistEventRow> LoadDelistEvents(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay delist events input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplayDelistEventRow[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(x => x.EffectiveTimestampUtc != default)
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => x with
            {
                Symbol = x.Symbol.ToUpperInvariant(),
                Source = string.IsNullOrWhiteSpace(x.Source) ? "external" : x.Source
            })
            .OrderBy(x => x.EffectiveTimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }
}

public sealed class ReplaySymbolTimeline
{
    private readonly IReadOnlyList<ReplaySymbolMappingRow> _mappings;
    private readonly IReadOnlyList<ReplayDelistEventRow> _delistEvents;
    private int _mappingCursor;
    private int _delistCursor;

    public ReplaySymbolTimeline(string initialSymbol, IReadOnlyList<ReplaySymbolMappingRow> mappings, IReadOnlyList<ReplayDelistEventRow> delistEvents)
    {
        CurrentSymbol = string.IsNullOrWhiteSpace(initialSymbol) ? string.Empty : initialSymbol.ToUpperInvariant();
        _mappings = mappings;
        _delistEvents = delistEvents;
    }

    public string CurrentSymbol { get; private set; }

    public ReplaySymbolTimelineStep Apply(DateTime timestampUtc)
    {
        var symbolEvents = new List<ReplaySymbolEventArtifactRow>();
        var dueDelists = new List<ReplayDelistEventRow>();

        while (_mappingCursor < _mappings.Count && _mappings[_mappingCursor].EffectiveTimestampUtc <= timestampUtc)
        {
            var mapping = _mappings[_mappingCursor];
            _mappingCursor++;

            if (!string.Equals(mapping.FromSymbol, CurrentSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prior = CurrentSymbol;
            CurrentSymbol = mapping.ToSymbol;
            symbolEvents.Add(new ReplaySymbolEventArtifactRow(
                mapping.EffectiveTimestampUtc,
                "SYMBOL_MAPPING",
                CurrentSymbol,
                prior,
                false,
                mapping.Source));
        }

        while (_delistCursor < _delistEvents.Count && _delistEvents[_delistCursor].EffectiveTimestampUtc <= timestampUtc)
        {
            var delist = _delistEvents[_delistCursor];
            _delistCursor++;

            if (!string.Equals(delist.Symbol, CurrentSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dueDelists.Add(delist);
            symbolEvents.Add(new ReplaySymbolEventArtifactRow(
                delist.EffectiveTimestampUtc,
                "DELIST",
                delist.Symbol,
                null,
                delist.IsTerminal,
                delist.Source));
        }

        return new ReplaySymbolTimelineStep(CurrentSymbol, symbolEvents, dueDelists);
    }
}

public sealed record ReplaySymbolTimelineStep(
    string CurrentSymbol,
    IReadOnlyList<ReplaySymbolEventArtifactRow> SymbolEvents,
    IReadOnlyList<ReplayDelistEventRow> DueDelistEvents
);