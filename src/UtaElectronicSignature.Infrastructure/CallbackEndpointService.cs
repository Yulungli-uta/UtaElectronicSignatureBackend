using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;
using UtaElectronicSignature.Domain;

namespace UtaElectronicSignature.Infrastructure;

/// <summary>
/// Administracion de la tabla tbl_CallbackEndpoints: a que URL avisar por cada
/// ClientId consumidor. Distinto de SigningProcessService, que resuelve el
/// callback de un proceso puntual a partir de esta configuracion.
/// </summary>
public sealed class CallbackEndpointService(SignatureDbContext db, Microsoft.Extensions.Configuration.IConfiguration config) : ICallbackEndpointService
{
    public async Task<IReadOnlyList<CallbackEndpointResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.CallbackEndpoints.AsNoTracking().OrderBy(x => x.ClientId).ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<CallbackEndpointResponse> CreateAsync(CallbackEndpointCreateRequest request, CancellationToken ct)
    {
        ValidateUrl(request.Url);
        if (string.IsNullOrWhiteSpace(request.ClientId))
            throw new ArgumentException("ClientId es obligatorio.");

        var existingActive = await db.CallbackEndpoints
            .Where(x => x.ClientId == request.ClientId && x.IsActive)
            .SingleOrDefaultAsync(ct);
        if (existingActive is not null)
            throw new InvalidOperationException("CALLBACK_ENDPOINT_ALREADY_ACTIVE: ya existe un endpoint activo para este ClientId.");

        var entity = new CallbackEndpoint
        {
            ClientId = request.ClientId,
            Url = request.Url.ToString(),
            EventsJson = JsonSerializer.Serialize(request.Events),
            IsActive = true
        };
        db.CallbackEndpoints.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<CallbackEndpointResponse> UpdateAsync(long id, CallbackEndpointUpdateRequest request, CancellationToken ct)
    {
        ValidateUrl(request.Url);
        var entity = await db.CallbackEndpoints.SingleOrDefaultAsync(x => x.CallbackEndpointID == id, ct)
            ?? throw new KeyNotFoundException("CALLBACK_ENDPOINT_NOT_FOUND");

        if (request.IsActive && !entity.IsActive)
        {
            var otherActive = await db.CallbackEndpoints
                .AnyAsync(x => x.ClientId == entity.ClientId && x.IsActive && x.CallbackEndpointID != id, ct);
            if (otherActive)
                throw new InvalidOperationException("CALLBACK_ENDPOINT_ALREADY_ACTIVE: ya existe un endpoint activo para este ClientId.");
        }

        entity.Url = request.Url.ToString();
        entity.EventsJson = JsonSerializer.Serialize(request.Events);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task DeactivateAsync(long id, CancellationToken ct)
    {
        var entity = await db.CallbackEndpoints.SingleOrDefaultAsync(x => x.CallbackEndpointID == id, ct)
            ?? throw new KeyNotFoundException("CALLBACK_ENDPOINT_NOT_FOUND");
        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private void ValidateUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("La URL de callback debe utilizar HTTPS.");
        if (uri.IsLoopback || System.Net.IPAddress.TryParse(uri.Host, out _))
            throw new ArgumentException("La URL de callback no está permitida.");
        var allowed = config.GetSection("Callbacks:AllowedHosts").GetChildren()
            .Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        if (allowed.Length == 0 || !allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("El host del callback no está autorizado.");
    }

    private static CallbackEndpointResponse ToResponse(CallbackEndpoint e) => new(
        e.CallbackEndpointID, e.ClientId, e.Url,
        JsonSerializer.Deserialize<string[]>(e.EventsJson) ?? [],
        e.IsActive, e.CreatedAt, e.UpdatedAt);
}
