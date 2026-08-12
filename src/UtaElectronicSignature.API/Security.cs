using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using UtaElectronicSignature.Application;

namespace UtaElectronicSignature.API;

/// <summary>
/// Lee el JWKS (solo la lista de llaves, sin metadata OIDC completa) desde
/// RepositoryUta. No hay retriever generico para esto en Microsoft.IdentityModel,
/// asi que se implementa uno minimo para poder usar ConfigurationManager y
/// obtener refresco automatico en vez de leerlo una sola vez al arrancar.
/// </summary>
public sealed class JsonWebKeySetRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        return new JsonWebKeySet(json);
    }
}

public sealed class CurrentUserService(IHttpContextAccessor accessor):ICurrentUserService
{
    private ClaimsPrincipal? User=>accessor.HttpContext?.User;
    public Guid? UserId=>Guid.TryParse(User?.FindFirstValue("sub")??User?.FindFirstValue(ClaimTypes.NameIdentifier),out var x)?x:null;
    public long? EmployeeId=>long.TryParse(User?.FindFirstValue("employeeId"),out var x)?x:null;
    public string? Email=>User?.FindFirstValue("email")??User?.FindFirstValue(ClaimTypes.Email);
    public string? SessionId=>User?.FindFirstValue("sid");
    // RepositoryUta emite el ClientId de tokens de aplicacion en el claim "client_id"
    // (ver JwtTokenService); fallback a ClaimTypes.Name mientras ese claim no exista
    // en tokens ya emitidos con el diseno anterior.
    public string? ClientId=>User?.FindFirstValue("client_id")??User?.FindFirstValue(ClaimTypes.Name);
    public bool IsAuthenticated=>User?.Identity?.IsAuthenticated==true;
}
public sealed record PermissionRequirement(string Code):IAuthorizationRequirement;
public sealed class PermissionHandler(IHttpClientFactory clients):AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,PermissionRequirement requirement)
    {
        var roles=context.User.FindAll(ClaimTypes.Role).Select(x=>x.Value).Concat(context.User.FindAll("role").Select(x=>x.Value)).Distinct().ToArray();
        if(roles.Length==0)return;
        var client=clients.CreateClient("RepositoryUta");
        var query=string.Join("&",roles.Select(role=>$"roles={Uri.EscapeDataString(role)}"));
        var result=await client.GetFromJsonAsync<PermissionEnvelope>($"api/role-permissions/effective?{query}");
        var permissions=result?.Data??[];
        var allowed=permissions.Contains(requirement.Code,StringComparer.OrdinalIgnoreCase)
            ||permissions.Contains("ADMIN.ACCESS",StringComparer.OrdinalIgnoreCase)
            ||(requirement.Code==SignaturePermissions.ProcessReadOwn&&permissions.Contains(SignaturePermissions.ProcessReadAll,StringComparer.OrdinalIgnoreCase));
        if(allowed)context.Succeed(requirement);
    }
    private sealed record PermissionEnvelope(bool Success,string[]? Data);
}
