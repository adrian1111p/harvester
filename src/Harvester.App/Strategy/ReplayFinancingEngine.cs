using System.Text.Json;

namespace Harvester.App.Strategy;

public sealed record ReplayBorrowLocateProfileRow(
    DateTime EffectiveTimestampUtc,
    string Symbol,
    double BorrowRateBps,
    bool LocateAvailable,
    double LocateFeePerShare,
    string Source
);

public sealed record ReplayBorrowLocateEventArtifactRow(
    DateTime TimestampUtc,
    string Symbol,
    double BorrowRateBps,
    bool LocateAvailable,
    double LocateFeePerShare,
    string Source
);

public sealed class ReplayFinancingEngine
{
    public static IReadOnlyList<ReplayBorrowLocateProfileRow> LoadBorrowLocateProfiles(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay borrow/locate input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplayBorrowLocateProfileRow[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(x => x.EffectiveTimestampUtc != default)
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => x with
            {
                Symbol = x.Symbol.ToUpperInvariant(),
                BorrowRateBps = Math.Max(0, x.BorrowRateBps),
                LocateFeePerShare = Math.Max(0, x.LocateFeePerShare),
                Source = string.IsNullOrWhiteSpace(x.Source) ? "external" : x.Source
            })
            .OrderBy(x => x.EffectiveTimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }
}

public sealed class ReplayBorrowLocateTimeline
{
    private readonly IReadOnlyList<ReplayBorrowLocateProfileRow> _profiles;
    private readonly Dictionary<string, ReplayBorrowLocateProfileRow> _activeBySymbol;
    private int _cursor;

    public ReplayBorrowLocateTimeline(IReadOnlyList<ReplayBorrowLocateProfileRow> profiles)
    {
        _profiles = profiles;
        _activeBySymbol = new Dictionary<string, ReplayBorrowLocateProfileRow>(StringComparer.OrdinalIgnoreCase);
    }

    public ReplayBorrowLocateTimelineStep Apply(DateTime timestampUtc)
    {
        var events = new List<ReplayBorrowLocateEventArtifactRow>();
        while (_cursor < _profiles.Count && _profiles[_cursor].EffectiveTimestampUtc <= timestampUtc)
        {
            var profile = _profiles[_cursor];
            _cursor++;

            _activeBySymbol[profile.Symbol] = profile;
            events.Add(new ReplayBorrowLocateEventArtifactRow(
                profile.EffectiveTimestampUtc,
                profile.Symbol,
                profile.BorrowRateBps,
                profile.LocateAvailable,
                profile.LocateFeePerShare,
                profile.Source));
        }

        return new ReplayBorrowLocateTimelineStep(events);
    }

    public ReplayBorrowLocateProfileRow GetProfile(string symbol)
    {
        if (_activeBySymbol.TryGetValue(symbol, out var profile))
        {
            return profile;
        }

        return new ReplayBorrowLocateProfileRow(
            DateTime.MinValue,
            symbol.ToUpperInvariant(),
            0,
            true,
            0,
            "default");
    }
}

public sealed record ReplayBorrowLocateTimelineStep(
    IReadOnlyList<ReplayBorrowLocateEventArtifactRow> Events
);