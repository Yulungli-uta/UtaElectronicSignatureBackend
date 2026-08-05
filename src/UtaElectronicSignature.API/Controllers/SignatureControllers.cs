using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.API.Controllers;

[ApiController,Route("api/v1/signature")]
public sealed class SignatureControllers(ISigningProcessService service):ControllerBase
{
    [HttpPost("integrations/requests"),Authorize(Policy=SignaturePermissions.IntegrationCreate)]
    public async Task<ActionResult<CreateProcessResponse>> CreateIntegration(CreateIntegrationRequest request,[FromHeader(Name="Idempotency-Key")]Guid key,CancellationToken ct)
        =>Created("",await service.CreateAsync(request,key,request.Source.System,ct));

    [HttpPost("processes"),Authorize(Policy=SignaturePermissions.ProcessCreate)]
    public async Task<ActionResult<CreateProcessResponse>> Create(CreateIntegrationRequest request,[FromHeader(Name="Idempotency-Key")]Guid key,CancellationToken ct)
        =>Created("",await service.CreateAsync(request,key,"UTA-PORTAL",ct));

    [HttpGet("processes"),Authorize(Policy=SignaturePermissions.ProcessReadOwn)]
    public async Task<ActionResult<IReadOnlyList<ProcessListItem>>> List(CancellationToken ct)=>Ok(await service.ListAsync(false,ct));

    [HttpGet("inbox"),HttpGet("inbox/pending"),HttpGet("inbox/signed"),Authorize(Policy=SignaturePermissions.ProcessReadOwn)]
    public async Task<ActionResult<IReadOnlyList<ProcessListItem>>> Inbox(CancellationToken ct)=>Ok(await service.ListAsync(true,ct));

    [HttpGet("processes/{id:long}"),Authorize(Policy=SignaturePermissions.ProcessReadOwn)]
    public async Task<ActionResult<ProcessDetail>> Get(long id,CancellationToken ct)
        =>await service.GetAsync(id,ct) is { } result?Ok(result):NotFound(new{code="SIGNING_PROCESS_NOT_FOUND"});

    [HttpGet("processes/{id:long}/progress"),HttpGet("processes/{id:long}/signers"),Authorize(Policy=SignaturePermissions.ProcessReadOwn)]
    public async Task<ActionResult<ProcessProgress>> Progress(long id,CancellationToken ct)
        =>await service.GetProgressAsync(id,ct) is { } result?Ok(result):NotFound(new{code="SIGNING_PROCESS_NOT_FOUND"});

    [HttpPost("processes/{id:long}/cancel"),Authorize(Policy=SignaturePermissions.ProcessCancel)]
    public async Task<IActionResult> Cancel(long id,CancellationToken ct){await service.CancelAsync(id,ct);return NoContent();}

    [HttpPost("processes/{id:long}/remind"),Authorize(Policy=SignaturePermissions.ProcessRemind)]
    public async Task<IActionResult> Remind(long id,CancellationToken ct){await service.RemindAsync(id,ct);return Accepted();}

    [HttpPost("processes/{id:long}/participants"),Authorize(Policy=SignaturePermissions.ProcessCreate)]
    public async Task<ActionResult<SignerProgress>> AddParticipant(long id,ParticipantCreateRequest request,CancellationToken ct)=>Ok(await service.AddParticipantAsync(id,request,ct));

    [HttpDelete("processes/{id:long}/participants/{participantId:long}"),Authorize(Policy=SignaturePermissions.ProcessCreate)]
    public async Task<IActionResult> RemoveParticipant(long id,long participantId,CancellationToken ct){await service.RemoveParticipantAsync(id,participantId,ct);return NoContent();}

    [HttpPost("processes/{id:long}/participants/{participantId:long}/reject"),Authorize(Policy=SignaturePermissions.DocumentReject)]
    public async Task<IActionResult> Reject(long id,long participantId,RejectSigningRequest request,CancellationToken ct){await service.RejectAsync(id,participantId,request,ct);return NoContent();}

    [HttpGet("processes/{id:long}/audit"),Authorize(Policy=SignaturePermissions.AuditRead)]
    public async Task<ActionResult<IReadOnlyList<AuditEventResponse>>> Audit(long id,CancellationToken ct)=>Ok(await service.GetAuditAsync(id,ct));

    [HttpGet("processes/{id:long}/documents"),Authorize(Policy=SignaturePermissions.DocumentDownload)]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> Documents(long id,CancellationToken ct)=>Ok(await service.GetDocumentsAsync(id,ct));

    [HttpGet("integrations/{sourceSystem}/{entityType}/{entityId}"),Authorize(Policy=SignaturePermissions.IntegrationRead)]
    public async Task<ActionResult<ProcessDetail>> Integration(string sourceSystem,string entityType,string entityId,CancellationToken ct)
        =>await service.GetByIntegrationAsync(sourceSystem,entityType,entityId,ct) is { } result?Ok(result):NotFound(new{code="SIGNING_PROCESS_NOT_FOUND"});

    [HttpPost("processes/{id:long}/start-signing"),Authorize(Policy=SignaturePermissions.DocumentSign)]
    public async Task<ActionResult<StartSigningResponse>> Start(long id,[FromBody]StartSigningRequest? position,CancellationToken ct)=>Ok(await service.StartSigningAsync(id,position,ct));

    // Alternativa cuando el cliente movil de FirmaEC no notifica solo la finalizacion (su
    // pantalla final solo ofrece Visualizar/Verificar/Compartir/Regresar, sin ningun paso
    // que avise al sistema de origen): el firmante sube el PDF que la app le entrego.
    // Sin chequeo de Content-Type a proposito: en movil no siempre llega como
    // "application/pdf" (ej. al compartir el archivo desde otra app). El validador
    // interno ya revisa los bytes reales del PDF antes de aceptar la firma.
    [HttpPost("processes/{id:long}/upload-signed"),Authorize(Policy=SignaturePermissions.DocumentSign),RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadSigned(long id,IFormFile document,CancellationToken ct)
    {
        await using var stream=document.OpenReadStream();
        await service.CompleteSigningByUploadAsync(id,stream,ct);
        return NoContent();
    }
}
