using System.Collections.Concurrent;

namespace Harvester.App.Strategy;

public sealed class DeterministicStrategyEventScheduler : IStrategyEventScheduler
{
    private readonly ConcurrentDictionary<string, long> _lastIntervalSlotByRun = new();
    private readonly ConcurrentDictionary<string, byte> _emittedOneShot = new();

    public IReadOnlyList<string> GetDueEvents(StrategyRuntimeContext context, DateTime utcNow)
    {
        var events = new List<string>();
        var runKey = BuildRunKey(context);

        var elapsedSeconds = Math.Max(0, (utcNow - context.RunStartedUtc).TotalSeconds);
        var intervalSeconds = Math.Max(1, context.ScheduledIntervalSeconds);
        var slot = (long)Math.Floor(elapsedSeconds / intervalSeconds);

        var lastSlot = _lastIntervalSlotByRun.GetOrAdd(runKey, -1);
        if (slot > lastSlot)
        {
            _lastIntervalSlotByRun[runKey] = slot;
            events.Add("interval");
        }

        if (TryParseUtcTime(context.SessionStartUtc, out var sessionStartUtc))
        {
            var beforeOpenKey = $"{runKey}|before_open";
            if (utcNow.TimeOfDay < sessionStartUtc && _emittedOneShot.TryAdd(beforeOpenKey, 1))
            {
                events.Add("before_open");
            }
        }

        if (TryParseUtcTime(context.SessionEndUtc, out var sessionEndUtc))
        {
            var afterCloseKey = $"{runKey}|after_close";
            if (utcNow.TimeOfDay >= sessionEndUtc && _emittedOneShot.TryAdd(afterCloseKey, 1))
            {
                events.Add("after_close");
            }
        }

        return events;
    }

    private static string BuildRunKey(StrategyRuntimeContext context)
    {
        return $"{context.RunStartedUtc:O}|{context.Mode}|{context.Account}|{context.Symbol}";
    }

    private static bool TryParseUtcTime(string value, out TimeSpan timeOfDay)
    {
        var parsed = DateTime.TryParseExact(value, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var hhmm)
            || DateTime.TryParseExact(value, "HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out hhmm);

        if (!parsed)
        {
            timeOfDay = default;
            return false;
        }

        timeOfDay = hhmm.TimeOfDay;
        return true;
    }
}
