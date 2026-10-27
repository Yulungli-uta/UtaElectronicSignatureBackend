using Microsoft.Extensions.Configuration;
using UtaElectronicSignature.Infrastructure;

namespace UtaElectronicSignature.UnitTests;

public sealed class AppClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static AppClock Create(DateTimeOffset utcNow, string? zoneId = null)
    {
        var settings = new Dictionary<string, string?>();
        if (zoneId is not null) settings["Time:ZoneId"] = zoneId;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AppClock(new FixedTimeProvider(utcNow), configuration);
    }

    [Fact]
    public void Today_after_19h_ecuador_is_still_the_same_day()
    {
        // 00:30 UTC del 27/10 = 19:30 del 26/10 en Ecuador
        var clock = Create(new DateTimeOffset(2026, 10, 27, 0, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 26), clock.Today);
        Assert.Equal(TimeSpan.FromHours(-5), clock.NowOffset.Offset);
    }

    [Fact]
    public void Year_changes_at_ecuador_midnight_not_utc_midnight()
    {
        // 31/12 23:30 en Ecuador = 01/01 04:30 UTC: el año de negocio sigue siendo el anterior
        var clock = Create(new DateTimeOffset(2027, 1, 1, 4, 30, 0, TimeSpan.Zero));
        Assert.Equal(2026, clock.Today.Year);
    }

    [Fact]
    public void Zone_comes_from_configuration()
    {
        var clock = Create(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), "UTC");
        Assert.Equal(TimeSpan.Zero, clock.NowOffset.Offset);
    }
}
