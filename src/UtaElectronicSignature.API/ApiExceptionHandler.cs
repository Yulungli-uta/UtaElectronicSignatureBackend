using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace UtaElectronicSignature.API;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger):IExceptionHandler
{
 public async ValueTask<bool> TryHandleAsync(HttpContext context,Exception exception,CancellationToken ct)
 {
  logger.LogError(exception,"Request failed. TraceId {TraceId}",context.TraceIdentifier);
  var (status,code,message)=exception switch
  {
   DbUpdateConcurrencyException=>(409,"DOCUMENT_VERSION_CHANGED","El documento fue actualizado por otro firmante. Debe revisar y firmar nuevamente la versión más reciente."),
   // Enlace inexistente o token que no coincide: mismo codigo/mensaje que "no existe" para
   // no permitir enumerar participantId validos por diferencia de respuesta.
   UnauthorizedAccessException e when e.Message=="EXTERNAL_LINK_INVALID"=>(404,"EXTERNAL_LINK_INVALID","El enlace no es válido."),
   // Estos dos solo se lanzan despues de que el hash SI coincidio, asi que distinguirlos
   // no revela nada que quien tiene el enlace correcto no supiera ya.
   UnauthorizedAccessException e when e.Message=="EXTERNAL_LINK_EXPIRED"=>(410,"EXTERNAL_LINK_EXPIRED","Este enlace ha expirado."),
   UnauthorizedAccessException e when e.Message=="EXTERNAL_LINK_ALREADY_USED"=>(410,"EXTERNAL_LINK_ALREADY_USED","Este enlace ya fue utilizado."),
   UnauthorizedAccessException=>(403,"SIGNER_NOT_ALLOWED","El usuario no está autorizado para esta operación."),
   KeyNotFoundException=>(404,"SIGNING_PROCESS_NOT_FOUND","No se encontró el proceso de firma."),
   ArgumentException e=>(400,"VALIDATION_ERROR",e.Message),
   InvalidOperationException e when e.Message.StartsWith("FIRMAEC_NOT_ENABLED")=>(503,"FIRMAEC_NOT_ENABLED","FirmaEC no está habilitado para este ambiente."),
   InvalidOperationException e when e.Message.StartsWith("FIRMAEC_CONTRACT_NOT_CONFIGURED")=>(503,"FIRMAEC_CONTRACT_NOT_CONFIGURED","El contrato institucional de FirmaEC todavía no está configurado."),
   InvalidOperationException e when e.Message=="SIGNING_SESSION_EXPIRED"=>(409,"SIGNING_SESSION_EXPIRED","La sesión de firma expiró."),
   InvalidOperationException e when e.Message=="IDEMPOTENCY_CONFLICT"=>(409,"IDEMPOTENCY_CONFLICT","La clave de idempotencia ya fue utilizada con una solicitud diferente."),
   InvalidOperationException e=>(409,e.Message,e.Message.Replace('_',' ')),
   _=>(500,"UNEXPECTED_ERROR","Ocurrió un error inesperado.")
  };
  context.Response.StatusCode=status;
  await context.Response.WriteAsJsonAsync(new ProblemDetails{Status=status,Title=message,Extensions={{"code",code},{"traceId",context.TraceIdentifier}}},ct);
  return true;
 }
}
