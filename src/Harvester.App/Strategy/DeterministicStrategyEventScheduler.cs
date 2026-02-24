using System.Collections.Concurrent;

namespace Harvester.App.Strategy;

public sealed class DeterministicStrategyEventScheduler : IStrategyEventScheduler
{
    private readonly IExchangeCalendarService _exchangeCalendarService;
    private readonly ConcurrentDictionary<string, long> _lastIntervalSlotByRun = new();
    private readonly ConcurrentDictionary<string, byte> _emittedOneShot = new();

    public DeterministicStrategyEventScheduler(IExchangeCalendarService? exchangeCalendarService = null)
    {
        _exchangeCalendarService = exchangeCalendarService ?? new UsEquitiesExchangeCalendarService();
    }

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

        var sessionWindowsAvailable = _exchangeCalendarService.TryGetSessionWindowUtc(context.ExchangeCalendar, utcNow, out var sessionWindow);
        if (sessionWindowsAvailable && sessionWindow.IsTradingDay)
        {
            var marketOpenKey = $"{runKey}|market_open";
            if (utcNow >= sessionWindow.SessionOpenUtc && utcNow < sessionWindow.SessionCloseUtc && _emittedOneShot.TryAdd(marketOpenKey, 1))
            {
                events.Add("market_open");
            }

            var beforeOpenKey = $"{runKey}|before_open";
            if (utcNow < sessionWindow.SessionOpenUtc && _emittedOneShot.TryAdd(beforeOpenKey, 1))
            {
                events.Add("before_open");
            }
        }

        if (sessionWindowsAvailable && sessionWindow.IsTradingDay)
        {
            var afterCloseKey = $"{runKey}|after_close";
            if (utcNow >= sessionWindow.SessionCloseUtc && _emittedOneShot.TryAdd(afterCloseKey, 1))
            {
                events.Add("after_close");
            }

            if (sessionWindow.IsEarlyClose)
            {
                var earlyCloseKey = $"{runKey}|early_close";
                if (utcNow >= sessionWindow.SessionCloseUtc && _emittedOneShot.TryAdd(earlyCloseKey, 1))
                {
                    events.Add("early_close");
                }
            }
        }
        else
        {
            if (TryParseUtcTime(context.SessionStartUtc, out var sessionStartUtc))
            {
                var marketOpenKey = $"{runKey}|market_open";
                var withinSession = !TryParseUtcTime(context.SessionEndUtc, out var sessionEndUtcForOpen)
                    ? utcNow.TimeOfDay >= sessionStartUtc
                    : utcNow.TimeOfDay >= sessionStartUtc && utcNow.TimeOfDay < sessionEndUtcForOpen;
                if (withinSession && _emittedOneShot.TryAdd(marketOpenKey, 1))
                {
                    events.Add("market_open");
                }

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
