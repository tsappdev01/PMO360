namespace PMO360.Domain.Abstractions;

/// <summary>
/// Everything that reads "now" goes through here. The BRD is full of date-relative rules —
/// overdue milestones, the update cycle, the rolling milestone window — and they are only
/// testable if the current date is something a test can set.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    private readonly TimeZoneInfo _zone;

    /// <param name="timeZoneId">
    /// IANA or Windows id for the business time zone. The portfolio is run out of Dubai, so
    /// "today" has to be Dubai's today: a milestone is not overdue because a server in UTC has
    /// not yet reached midnight.
    /// </param>
    public SystemClock(string timeZoneId = "Asia/Dubai")
    {
        _zone = ResolveZone(timeZoneId);
    }

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Windows hosts carry "Arabian Standard Time" instead of the IANA id. Fall back to
            // the fixed +04:00 offset rather than silently reporting UTC dates: the Gulf has no
            // daylight saving, so the offset is correct all year.
            return TimeZoneInfo.CreateCustomTimeZone("PMO360-Gulf", TimeSpan.FromHours(4), "Gulf Standard Time", "Gulf Standard Time");
        }
    }
}
