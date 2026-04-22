namespace PcsRemote.Automation;

/// <summary>
/// A <see cref="TimeProvider"/> that returns a fixed date for testing purposes.
/// Time-of-day advances normally; only the date is overridden.
/// </summary>
internal sealed class FixedDateTimeProvider : TimeProvider
{
    private readonly DateOnly _fixedDate;

    public FixedDateTimeProvider(DateOnly fixedDate)
    {
        _fixedDate = fixedDate;
    }

    public override DateTimeOffset GetUtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(_fixedDate.Year, _fixedDate.Month, _fixedDate.Day,
            now.Hour, now.Minute, now.Second, TimeSpan.Zero);
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
