using Microsoft.Extensions.Configuration;
using UtaElectronicSignature.Application;

namespace UtaElectronicSignature.Infrastructure;

/// <summary>
/// Implementación de <see cref="IAppClock"/> sobre <see cref="TimeProvider"/> (las pruebas pueden inyectar un FakeTimeProvider).
/// La zona se lee de <c>Time:ZoneId</c>; sin configuración usa America/Guayaquil.
/// </summary>
public sealed class AppClock : IAppClock
{
    public const string DefaultZoneId = "America/Guayaquil";
    private const string WindowsFallbackZoneId = "SA Pacific Standard Time";
    private readonly TimeProvider _provider;

    public AppClock(TimeProvider provider, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configuration);
        _provider = provider;
        var zoneId = configuration["Time:ZoneId"];
        Zone = Resolve(string.IsNullOrWhiteSpace(zoneId) ? DefaultZoneId : zoneId.Trim());
    }

    public TimeZoneInfo Zone { get; }

    public DateTimeOffset NowOffset => TimeZoneInfo.ConvertTime(_provider.GetUtcNow(), Zone);

    public DateOnly Today => DateOnly.FromDateTime(NowOffset.DateTime);

    private static TimeZoneInfo Resolve(string zoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (TimeZoneNotFoundException) when (zoneId == DefaultZoneId)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallbackZoneId);
        }
    }
}
