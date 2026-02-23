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

        var isTradingDay = local.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
            && !IsUsEquitiesHoliday(localDate);
        var isEarlyClose = isTradingDay && IsUsEquitiesEarlyClose(localDate);
        var sessionOpenLocal = localDate.AddHours(9).AddMinutes(30);
        var sessionCloseLocal = isEarlyClose
            ? localDate.AddHours(13)
            : localDate.AddHours(16);

        var openUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionOpenLocal, DateTimeKind.Unspecified), eastern);
        var closeUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionCloseLocal, DateTimeKind.Unspecified), eastern);

        sessionWindow = new ExchangeSessionWindow(
            "US-EQUITIES",
            openUtc,
            closeUtc,
            isTradingDay,
            isEarlyClose);

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

    private static bool IsUsEquitiesHoliday(DateTime localDate)
    {
        var year = localDate.Year;
        var holidays = new HashSet<DateTime>
        {
            ObserveHoliday(new DateTime(year, 1, 1)),
            NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3),
            NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3),
            GetGoodFriday(year),
            LastWeekdayOfMonth(year, 5, DayOfWeek.Monday),
            ObserveHoliday(new DateTime(year, 6, 19)),
            ObserveHoliday(new DateTime(year, 7, 4)),
            NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1),
            NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4),
            ObserveHoliday(new DateTime(year, 12, 25))
        };

        return holidays.Contains(localDate.Date);
    }

    private static bool IsUsEquitiesEarlyClose(DateTime localDate)
    {
        var thanksgiving = NthWeekdayOfMonth(localDate.Year, 11, DayOfWeek.Thursday, 4);
        var dayAfterThanksgiving = thanksgiving.AddDays(1);
        if (localDate.Date == dayAfterThanksgiving.Date && localDate.DayOfWeek == DayOfWeek.Friday)
        {
            return true;
        }

        if (localDate.Month == 12 && localDate.Day == 24 && localDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return !IsUsEquitiesHoliday(localDate);
        }

        if (localDate.Month == 7 && localDate.Day == 3 && localDate.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
        {
            var independenceDay = new DateTime(localDate.Year, 7, 4);
            return independenceDay.DayOfWeek is >= DayOfWeek.Tuesday and <= DayOfWeek.Friday;
        }

        return false;
    }

    private static DateTime ObserveHoliday(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date.AddDays(-1).Date,
            DayOfWeek.Sunday => date.AddDays(1).Date,
            _ => date.Date
        };
    }

    private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
    {
        var date = new DateTime(year, month, 1);
        var delta = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(delta + 7 * (nth - 1)).Date;
    }

    private static DateTime LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var date = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var delta = ((int)date.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return date.AddDays(-delta).Date;
    }

    private static DateTime GetGoodFriday(int year)
    {
        var easter = GetWesternEasterSunday(year);
        return easter.AddDays(-2).Date;
    }

    private static DateTime GetWesternEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }
}
