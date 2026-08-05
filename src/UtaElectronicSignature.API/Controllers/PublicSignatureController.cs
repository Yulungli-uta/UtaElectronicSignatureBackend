using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.API.Controllers;

// Endpoints anonimos para firmantes externos (sin usuario/empleado interno): el acceso
// se controla por token de un solo uso, no por JWT. EnableRateLimiting mitiga fuerza
// bruta/enumeracion contra participantId+token.
[ApiController,Route("api/v1/signature/public"),AllowAnonymous,EnableRateLimiting("external-signing")]
public sealed class PublicSignatureController(ISigningProcessService service):ControllerBase
{
    [HttpGet("participants/{participantId:long}")]
    public async Task<ActionResult<PublicParticipantInfoResponse>> Get(long participantId,[FromQuery]string token,CancellationToken ct)
        =>Ok(await service.GetPublicParticipantAsync(participantId,token,ct));

    [HttpGet("participants/{participantId:long}/document")]
    public async Task<IActionResult> Document(long participantId,[FromQuery]string token,CancellationToken ct)
    {
        // Sin fileDownloadName a proposito: pasarlo agrega Content-Disposition:attachment,
        // lo que hace que el <embed> de la pagina publica dispare el dialogo de "abrir
        // archivo" del navegador en vez de mostrar el PDF en linea.
        var(content,_)=await service.GetPublicDocumentAsync(participantId,token,ct);
        return File(content,"application/pdf",enableRangeProcessing:true);
    }

    [HttpPost("participants/{participantId:long}/start-signing")]
    public async Task<ActionResult<StartSigningResponse>> Start(long participantId,[FromQuery]string token,[FromBody]StartSigningRequest? position,CancellationToken ct)
        =>Ok(await service.StartExternalSigningAsync(participantId,token,position,ct));

    // Alternativa cuando el cliente movil de FirmaEC no notifica solo la finalizacion (su
    // pantalla final solo ofrece Visualizar/Verificar/Compartir/Regresar, sin ningun paso
    // que avise al sistema de origen): el firmante externo sube el PDF que la app le entrego.
    // Sin chequeo de Content-Type a proposito: en movil no siempre llega como
    // "application/pdf" (ej. al compartir el archivo desde otra app). El validador
    // interno ya revisa los bytes reales del PDF antes de aceptar la firma.
    [HttpPost("participants/{participantId:long}/upload-signed"),RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadSigned(long participantId,[FromQuery]string token,IFormFile document,CancellationToken ct)
    {
        await using var stream=document.OpenReadStream();
        await service.CompleteExternalSigningByUploadAsync(participantId,token,stream,ct);
        return NoContent();
    }
}
