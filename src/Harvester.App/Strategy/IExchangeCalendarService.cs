namespace Harvester.App.Strategy;

public interface IExchangeCalendarService
{
    bool TryGetSessionWindowUtc(string calendarId, DateTime utcNow, out ExchangeSessionWindow sessionWindow);
}

public sealed record ExchangeSessionWindow(
    string CalendarId,
    DateTime SessionOpenUtc,
    DateTime SessionCloseUtc,
    bool IsTradingDay
);
