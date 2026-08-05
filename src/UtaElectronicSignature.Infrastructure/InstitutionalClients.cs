using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Domain;

namespace UtaElectronicSignature.Infrastructure;

public sealed class ServiceTokenProvider(IHttpClientFactory clients,IConfiguration config)
{
 private string? _token; private DateTimeOffset _expiresAt;
 public async Task<string> GetAsync(CancellationToken ct)
 {
  if(_token is not null&&_expiresAt>DateTimeOffset.UtcNow.AddMinutes(2))return _token;
  var response=await clients.CreateClient("RepositoryUta").PostAsJsonAsync("/api/app-auth/token",new{
   clientId=config["RepositoryUta:ServiceClientId"],clientSecret=config["RepositoryUta:ServiceClientSecret"]},ct);
  response.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
  _token=json.RootElement.GetProperty("data").GetProperty("accessToken").GetString()??throw new InvalidOperationException("RepositoryUta no devolvió accessToken.");
  _expiresAt=json.RootElement.GetProperty("data").GetProperty("expiresAt").GetDateTimeOffset();return _token;
 }
}

public sealed class HrBackendClient(IHttpClientFactory clients,ServiceTokenProvider tokens,IConfiguration config)
{
 private async Task<HttpClient> AuthorizedClientAsync(CancellationToken ct)
 {
  var client=clients.CreateClient("HrBackend");
  client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",await tokens.GetAsync(ct));
  return client;
 }
 public async Task SendFinalDocumentAsync(string recipient,string processNumber,string title,Guid fileGuid,CancellationToken ct)
 {
  var client=await AuthorizedClientAsync(ct);
  var request=new{to=recipient,subject=$"Documento firmado completado - {processNumber}",
   bodyHtml=$"<h2>Proceso de firma completado</h2><p>El documento <strong>{System.Net.WebUtility.HtmlEncode(title)}</strong> ha sido firmado por todos los participantes.</p><p>Número de proceso: {System.Net.WebUtility.HtmlEncode(processNumber)}</p>",
   layoutSlug=config["HrBackend:FinalEmailLayoutSlug"]??"firma-electronica-final",
   attachments=new[]{new{storedFileGuid=fileGuid,fileName=$"{processNumber}-firmado.pdf",contentType="application/pdf"}}};
  using var response=await client.PostAsJsonAsync("/api/v1/rh/email/send-by-guid",request,ct);response.EnsureSuccessStatusCode();
 }
 public async Task SendReminderAsync(string recipient,string fullName,string processNumber,string title,string? description,CancellationToken ct)
 {
  var client=await AuthorizedClientAsync(ct);
  var descriptionHtml=string.IsNullOrWhiteSpace(description)?"":$"<p>{System.Net.WebUtility.HtmlEncode(description)}</p>";
  var request=new{to=recipient,subject=$"Recordatorio de firma - {processNumber}",
   bodyHtml=$"<p>Estimado/a {System.Net.WebUtility.HtmlEncode(fullName)}, tiene pendiente la firma del documento <strong>{System.Net.WebUtility.HtmlEncode(title)}</strong>.</p>{descriptionHtml}<p>Número de proceso: {System.Net.WebUtility.HtmlEncode(processNumber)}</p>",
   layoutSlug=config["HrBackend:FinalEmailLayoutSlug"]??"firma-electronica-final",attachments=Array.Empty<object>()};
  using var response=await client.PostAsJsonAsync("/api/v1/rh/email/send-by-guid",request,ct);response.EnsureSuccessStatusCode();
 }
 public async Task SendExternalInvitationAsync(string recipient,string fullName,string processNumber,string title,string link,CancellationToken ct)
 {
  var client=await AuthorizedClientAsync(ct);
  var request=new{to=recipient,subject=$"Invitación a firmar documento - {processNumber}",
   bodyHtml=$"<p>Estimado/a {System.Net.WebUtility.HtmlEncode(fullName)}, se le invita a firmar electrónicamente el documento <strong>{System.Net.WebUtility.HtmlEncode(title)}</strong>.</p>"+
    $"<p>Número de proceso: {System.Net.WebUtility.HtmlEncode(processNumber)}</p>"+
    $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Ver y firmar el documento</a></p>"+
    "<p>Este enlace es de un solo uso y expira en 72 horas.</p>",
   layoutSlug=config["HrBackend:FinalEmailLayoutSlug"]??"firma-electronica-final",attachments=Array.Empty<object>()};
  using var response=await client.PostAsJsonAsync("/api/v1/rh/email/send-by-guid",request,ct);response.EnsureSuccessStatusCode();
 }
 public async Task<byte[]> DownloadDocumentAsync(Guid fileGuid,CancellationToken ct)
 {
  var client=await AuthorizedClientAsync(ct);
  using var response=await client.GetAsync($"/api/v1/rh/documents/download/{fileGuid}",HttpCompletionOption.ResponseHeadersRead,ct);
  response.EnsureSuccessStatusCode();
  var maximum=config.GetValue("HrBackend:MaxAttachmentBytes",15_728_640);
  if(response.Content.Headers.ContentLength>maximum)throw new InvalidOperationException("DOCUMENT_SIZE_LIMIT_EXCEEDED");
  var bytes=await response.Content.ReadAsByteArrayAsync(ct);
  if(bytes.Length>maximum)throw new InvalidOperationException("DOCUMENT_SIZE_LIMIT_EXCEEDED");
  return bytes;
 }
 public async Task<Guid> UploadSignedDocumentAsync(
  byte[] document,
  string fileName,
  long signingProcessId,
  CancellationToken ct)
 {
  var directoryCode=config["HrBackend:SignatureDirectoryCode"];
  if(string.IsNullOrWhiteSpace(directoryCode))
   throw new InvalidOperationException("HRBACKEND_SIGNATURE_DIRECTORY_NOT_CONFIGURED");

  var client=await AuthorizedClientAsync(ct);
  using var form=new MultipartFormDataContent();
  form.Add(new StringContent(directoryCode),"DirectoryCode");
  form.Add(new StringContent("SIGNATURE_PROCESS"),"EntityType");
  form.Add(new StringContent(
   signingProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
   "EntityId");
  form.Add(new StringContent("signed"),"RelativePath");
  if(int.TryParse(config["HrBackend:SignatureDocumentTypeId"],out var documentTypeId))
   form.Add(new StringContent(
    documentTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    "DocumentTypeId");

  using var fileContent=new ByteArrayContent(document);
  fileContent.Headers.ContentType=new MediaTypeHeaderValue("application/pdf");
  form.Add(fileContent,"File",Path.GetFileName(fileName));

  using var response=await client.PostAsync(
   "/api/v1/rh/documents/upload-single",
   form,
   ct);
  var payload=await response.Content.ReadFromJsonAsync<DocumentUploadResult>(
   cancellationToken:ct);
  if(!response.IsSuccessStatusCode||payload is null||!payload.Success)
   throw new InvalidOperationException(
    $"HRBACKEND_DOCUMENT_UPLOAD_FAILED: HrBackend respondiÃ³ HTTP {(int)response.StatusCode}.");
  var fileGuid=payload.Items
   .FirstOrDefault(x=>x.Success)
   ?.StoredFile
   ?.FileGuid;
  return fileGuid is { } value&&value!=Guid.Empty
   ?value
   :throw new InvalidOperationException(
    "HRBACKEND_DOCUMENT_UPLOAD_FAILED: HrBackend no devolviÃ³ FileGuid.");
 }

 private sealed record DocumentUploadResult(
  bool Success,
  IReadOnlyList<DocumentUploadItem> Items);
 private sealed record DocumentUploadItem(
  bool Success,
  StoredFileResult? StoredFile);
 private sealed record StoredFileResult(Guid FileGuid);
}

public sealed class DocumentAccessService(SignatureDbContext db,ISigningProcessService processes,HrBackendClient hr)
{
 public async Task<(byte[] Content,string FileName)> DownloadVersionAsync(long versionId,CancellationToken ct)
 {
  var item=await db.DocumentVersions.AsNoTracking().Where(x=>x.DocumentVersionID==versionId)
   .Select(x=>new{x.FileGuid,x.Document.FileName,x.Document.SigningProcessID}).SingleOrDefaultAsync(ct)??throw new KeyNotFoundException();
  await processes.GetDocumentsAsync(item.SigningProcessID,ct);
  return(await hr.DownloadDocumentAsync(item.FileGuid,ct),item.FileName);
 }
}

public sealed class OutboxWorker(IServiceScopeFactory scopes,ILogger<OutboxWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  while(!stoppingToken.IsCancellationRequested)
  {
   try{
    using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<SignatureDbContext>();var hr=scope.ServiceProvider.GetRequiredService<HrBackendClient>();
    var messages=await db.OutboxMessages.Where(x=>(x.Type=="SIGNATURE_FINAL_DOCUMENT_EMAIL"||x.Type=="SIGNATURE_REMINDER_EMAIL"||x.Type=="SIGNATURE_EXTERNAL_INVITATION_EMAIL")&&x.Status=="PENDING"&&(x.NextAttemptAt==null||x.NextAttemptAt<=DateTimeOffset.UtcNow)).OrderBy(x=>x.CreatedAt).Take(10).ToListAsync(stoppingToken);
    foreach(var m in messages){try{using var j=JsonDocument.Parse(m.Payload);var r=j.RootElement;
      if(m.Type=="SIGNATURE_FINAL_DOCUMENT_EMAIL")
       await hr.SendFinalDocumentAsync(r.GetProperty("RecipientEmail").GetString()!,r.GetProperty("ProcessNumber").GetString()!,r.GetProperty("Title").GetString()!,r.GetProperty("FileGuid").GetGuid(),stoppingToken);
      else if(m.Type=="SIGNATURE_REMINDER_EMAIL")
       await hr.SendReminderAsync(r.GetProperty("Email").GetString()!,r.GetProperty("FullName").GetString()!,r.GetProperty("ProcessNumber").GetString()!,r.GetProperty("Title").GetString()!,
        r.TryGetProperty("Description",out var descEl)?descEl.GetString():null,stoppingToken);
      else
       await hr.SendExternalInvitationAsync(r.GetProperty("Email").GetString()!,r.GetProperty("FullName").GetString()!,r.GetProperty("ProcessNumber").GetString()!,r.GetProperty("Title").GetString()!,r.GetProperty("Link").GetString()!,stoppingToken);
      m.Status="SENT";m.ProcessedAt=DateTimeOffset.UtcNow;}catch(Exception ex){m.AttemptCount++;m.Status=m.AttemptCount>=5?"FAILED":"PENDING";m.NextAttemptAt=DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,m.AttemptCount));logger.LogError(ex,"Outbox {Id} failed",m.OutboxMessageID);}}
    await db.SaveChangesAsync(stoppingToken);
   }catch(Exception ex){logger.LogError(ex,"Outbox cycle failed");}
   await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);
  }
 }
}

public sealed class SignatureMaintenanceWorker(IServiceScopeFactory scopes,ILogger<SignatureMaintenanceWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
  while(await timer.WaitForNextTickAsync(stoppingToken))
  {
   try
   {
    using var scope=scopes.CreateScope();
    var db=scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
    var now=DateTimeOffset.UtcNow;
    var processes=await db.SigningProcesses.Include(x=>x.Participants)
     .Where(x=>x.ExpiresAt!=null&&x.ExpiresAt<=now&&(x.Status==ProcessStatus.InProgress||x.Status==ProcessStatus.PartiallySigned)).ToListAsync(stoppingToken);
    foreach(var process in processes)
    {
     process.Status=ProcessStatus.Expired;process.UpdatedAt=now;
     foreach(var signer in process.Participants.Where(x=>x.Status!=ParticipantStatus.Signed))signer.Status=ParticipantStatus.Expired;
     db.SigningEvents.Add(new SigningEvent{SigningProcessID=process.SigningProcessID,EventType="PROCESS_EXPIRED",CorrelationID=Guid.NewGuid().ToString()});
    }
    var sessions=await db.SigningSessions.Where(x=>x.ExpiresAt<=now&&x.Status!="COMPLETED"&&x.Status!="EXPIRED").ToListAsync(stoppingToken);
    foreach(var session in sessions)session.Status="EXPIRED";
    if(processes.Count>0||sessions.Count>0)await db.SaveChangesAsync(stoppingToken);
   }
   catch(Exception ex){logger.LogError(ex,"Signature maintenance cycle failed");}
  }
 }
}

public sealed class CallbackOutboxWorker(IServiceScopeFactory scopes,IHttpClientFactory clients,IConfiguration config,ILogger<CallbackOutboxWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
  while(await timer.WaitForNextTickAsync(stoppingToken))
  {
   try
   {
    using var scope=scopes.CreateScope();
    var db=scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
    var messages=await db.OutboxMessages.Where(x=>x.Status=="PENDING"&&(x.Type=="SIGNATURE_PROCESS_CREATED"||x.Type=="PROCESS_COMPLETED")&&(x.NextAttemptAt==null||x.NextAttemptAt<=DateTimeOffset.UtcNow)).OrderBy(x=>x.CreatedAt).Take(10).ToListAsync(stoppingToken);
    foreach(var message in messages)await DeliverAsync(db,message,stoppingToken);
    await db.SaveChangesAsync(stoppingToken);
   }
   catch(Exception ex){logger.LogError(ex,"Callback outbox cycle failed");}
  }
 }

 private async Task DeliverAsync(SignatureDbContext db,OutboxMessage message,CancellationToken ct)
 {
  try
  {
   using var json=JsonDocument.Parse(message.Payload);
   long? processId=json.RootElement.TryGetProperty("SigningProcessID",out var idElement)?idElement.GetInt64():null;
   if(processId is null&&json.RootElement.TryGetProperty("ProcessGuid",out var guidElement))
   {
    var processGuid=guidElement.GetGuid();
    processId=await db.SigningProcesses.Where(x=>x.ProcessGuid==processGuid).Select(x=>(long?)x.SigningProcessID).SingleOrDefaultAsync(ct);
   }
   if(processId is null){message.Status="FAILED";return;}
   var eventName=message.Type=="SIGNATURE_PROCESS_CREATED"?"PROCESS_CREATED":message.Type;
   var subscriptions=await db.CallbackSubscriptions.AsNoTracking().Where(x=>x.SigningProcessID==processId&&x.IsActive).ToListAsync(ct);
   var payload=JsonSerializer.Serialize(new{eventId=message.OutboxMessageID,eventType=eventName,occurredAt=message.CreatedAt,processId,payload=json.RootElement});
   var secret=config["Callbacks:HmacSecret"];
   foreach(var subscription in subscriptions.Where(x=>JsonSerializer.Deserialize<string[]>(x.EventsJson)?.Contains(eventName,StringComparer.OrdinalIgnoreCase)==true))
   {
    if(string.IsNullOrWhiteSpace(secret))throw new InvalidOperationException("Callbacks:HmacSecret no está configurado.");
    using var request=new HttpRequestMessage(HttpMethod.Post,subscription.Url){Content=new StringContent(payload,Encoding.UTF8,"application/json")};
    request.Headers.Add("X-Event-ID",message.OutboxMessageID.ToString());
    request.Headers.Add("X-UTA-Signature",Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),Encoding.UTF8.GetBytes(payload))).ToLowerInvariant());
    using var response=await clients.CreateClient("Callbacks").SendAsync(request,ct);response.EnsureSuccessStatusCode();
   }
   message.Status="SENT";message.ProcessedAt=DateTimeOffset.UtcNow;
  }
  catch(Exception ex)
  {
   message.AttemptCount++;message.Status=message.AttemptCount>=5?"FAILED":"PENDING";message.NextAttemptAt=DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,message.AttemptCount));
   logger.LogError(ex,"Callback outbox {Id} failed",message.OutboxMessageID);
  }
 }
}
