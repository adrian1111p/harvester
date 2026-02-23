namespace Harvester.App.Strategy;

public sealed class UsEquitiesExchangeCalendarService : IExchangeCalendarService
{
    public bool TryGetSessionWindowUtc(string calendarId, DateTime utcNow, out ExchangeSessionWindow sessionWindow)
    {
        if (!string.Equals(calendarId, "US-EQUITIES", StringComparison.OrdinalIgnoreCase))
        {
            sessionWindow = default!;
            return false;
        }

        var eastern = ResolveEasternTimeZone();
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, eastern);
        var localDate = local.Date;

        var isTradingDay = local.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
        var sessionOpenLocal = localDate.AddHours(9).AddMinutes(30);
        var sessionCloseLocal = localDate.AddHours(16);

        var openUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionOpenLocal, DateTimeKind.Unspecified), eastern);
        var closeUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionCloseLocal, DateTimeKind.Unspecified), eastern);

        sessionWindow = new ExchangeSessionWindow(
            "US-EQUITIES",
            openUtc,
            closeUtc,
            isTradingDay);

        return true;
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
    }
}
