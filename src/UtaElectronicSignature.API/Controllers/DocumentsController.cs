using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Infrastructure;

namespace UtaElectronicSignature.API.Controllers;

[ApiController,Route("api/v1/signature/documents")]
public sealed class DocumentsController(DocumentAccessService documents):ControllerBase
{
 [HttpGet("versions/{versionId:long}/download"),Authorize(Policy=SignaturePermissions.DocumentDownload)]
 public async Task<IActionResult> DownloadVersion(long versionId,CancellationToken ct)
 {
  var result=await documents.DownloadVersionAsync(versionId,ct);
  return File(result.Content,"application/pdf",result.FileName,enableRangeProcessing:true);
 }
}
