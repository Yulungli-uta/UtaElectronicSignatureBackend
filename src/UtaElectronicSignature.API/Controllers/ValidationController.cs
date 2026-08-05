using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UtaElectronicSignature.Application;
namespace UtaElectronicSignature.API.Controllers;
[ApiController,Route("api/v1/signature/validation")]
public sealed class ValidationController(IPdfSignatureValidator validator,ICertificateValidator certValidator):ControllerBase
{
 // Sin chequeo de Content-Type a proposito: en movil no siempre llega como
 // "application/pdf" (ej. al compartir el archivo desde otra app), aunque el archivo SI
 // sea un PDF valido. El validador ya revisa los bytes reales (firma "%PDF-") y responde
 // 400 con un mensaje claro si de verdad no es un PDF.
 [HttpPost("documents"),Authorize(Policy=SignaturePermissions.DocumentValidate),RequestSizeLimit(25_000_000)]
 public async Task<IActionResult> Validate(IFormFile document,CancellationToken ct)
 {
  await using var stream=document.OpenReadStream();return Ok(await validator.ValidateDocumentAsync(stream,ct));
 }

 // La contraseña llega en el form-data solo para abrirse en memoria aqui — nunca se
 // guarda ni se registra en logs (ver CertificateValidationService).
 [HttpPost("certificate"),Authorize(Policy=SignaturePermissions.DocumentValidate),RequestSizeLimit(5_000_000)]
 public async Task<IActionResult> ValidateCertificate([FromForm]IFormFile certificate,[FromForm]string password,CancellationToken ct)
 {
  using var ms=new MemoryStream();
  await certificate.CopyToAsync(ms,ct);
  try
  {
   return Ok(certValidator.Validate(ms.ToArray(),password));
  }
  catch(CryptographicException)
  {
   return BadRequest(new{code="INVALID_CERTIFICATE_OR_PASSWORD",message="No se pudo abrir el certificado. Verifica el archivo y la contraseña."});
  }
 }
}
