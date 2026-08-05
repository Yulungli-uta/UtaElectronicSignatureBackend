using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.API.Controllers;

[ApiController]
[Route("api/v1/signature/callbacks/firmaec")]
public sealed class FirmaEcCallbackController(
    ISigningProcessService service,
    IConfiguration configuration) : ControllerBase
{
    private const int MaximumCallbackBytes = 21 * 1024 * 1024;

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumCallbackBytes)]
    [Consumes("application/json")]
    [Produces("text/plain")]
    public async Task<IActionResult> Receive(
        FirmaEcSignedDocumentCallback request,
        CancellationToken ct)
    {
        var expected = configuration["FirmaEc:CallbackApiKey"];
        var provided = Request.Headers["X-API-KEY"].ToString();
        if (string.IsNullOrWhiteSpace(expected)
            || string.IsNullOrWhiteSpace(provided)
            || !FixedTimeEquals(expected, provided))
        {
            return Unauthorized();
        }

        await service.CompleteFirmaEcCallbackAsync(request, ct);
        return Content("OK", "text/plain", Encoding.UTF8);
    }

    private static bool FixedTimeEquals(string expected, string provided)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
