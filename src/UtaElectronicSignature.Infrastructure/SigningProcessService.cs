using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;
using UtaElectronicSignature.Domain;

namespace UtaElectronicSignature.Infrastructure;

public sealed class SigningProcessService(
    SignatureDbContext db,
    ICurrentUserService current,
    IFirmaEcClient firmaEc,
    HrBackendClient hrBackend,
    IPdfSignatureValidator pdfValidator,
    Microsoft.Extensions.Configuration.IConfiguration config) : ISigningProcessService
{
    public async Task<CreateProcessResponse> CreateAsync(CreateIntegrationRequest request, Guid key, string source, CancellationToken ct)
    {
        if(request.Signers.Count==0) throw new ArgumentException("Debe existir al menos un firmante.");
        if(request.Process.MinimumRequiredSignatures<=0||request.Process.MinimumRequiredSignatures>request.Signers.Count(x=>x.Required))
            throw new ArgumentException("La cantidad mínima de firmas no es válida.");
        var requestJson=JsonSerializer.Serialize(request);
        var requestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var existing=await db.IdempotencyRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.SourceSystem==source&&x.IdempotencyKey==key,ct);
        if(existing is not null)
        {
            if(!string.Equals(existing.RequestHash,requestHash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            if(existing.SigningProcessID is not long existingId)throw new InvalidOperationException("IDEMPOTENCY_REQUEST_IN_PROGRESS");
            var found=await db.SigningProcesses.AsNoTracking().SingleAsync(x=>x.SigningProcessID==existingId,ct);
            return Map(found,1);
        }
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var year=DateTimeOffset.UtcNow.Year;
        var next=await db.SigningProcesses.CountAsync(x=>x.CreatedAt.Year==year,ct)+1;
        var creatorEmail=request.Metadata?.GetValueOrDefault("creatorEmail")??current.Email??throw new InvalidOperationException("El creador no tiene correo institucional.");
        var process=new SigningProcess{ProcessNumber=$"FIR-{year}-{next:000000}",Title=request.Process.Title,Description=request.Process.Description,CreatorEmail=creatorEmail,
            WorkflowType=Enum.Parse<WorkflowType>(request.Process.WorkflowType,true),Status=ProcessStatus.InProgress,
            MinimumRequiredSignatures=request.Process.MinimumRequiredSignatures,ExpiresAt=request.Process.ExpiresAt,CreatedBy=current.UserId};
        var externalTokens=new List<(SigningParticipant Participant,string RawToken)>();
        foreach(var s in request.Signers)
        {
            var participant=new SigningParticipant{UserID=null,PersonID=s.PersonId,EmployeeID=s.EmployeeId,Identification=s.Identification,
                FullName=s.FullName,Email=s.Email,RoleCode=s.Role,Required=s.Required,SigningOrder=s.Order,Status=ParticipantStatus.Notified,CreatedBy=current.UserId,IsExternal=s.IsExternal};
            if(s.IsExternal)
            {
                var(rawToken,hash)=GenerateExternalToken();
                participant.ExternalAccessTokenHash=hash;participant.ExternalAccessTokenExpiresAt=DateTimeOffset.UtcNow.AddHours(72);
                externalTokens.Add((participant,rawToken));
            }
            process.Participants.Add(participant);
        }
        process.Documents.Add(new Document{FileName=request.Document.FileName,ContentType="application/pdf",CreatedBy=current.UserId,
            Versions=[new DocumentVersion{SequenceNumber=1,FileGuid=request.Document.FileGuid,Sha256=ParseHash(request.Document.Sha256),SizeBytes=0,CreatedBy=current.UserId}]});
        db.SigningProcesses.Add(process);
        db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_PROCESS_CREATED",Payload=JsonSerializer.Serialize(new{process.ProcessGuid,request.Source})});
        db.IdempotencyRequests.Add(new IdempotencyRequest{IdempotencyKey=key,SourceSystem=source,RequestHash=requestHash,ResponseStatusCode=201,ExpiresAt=DateTimeOffset.UtcNow.AddDays(1)});
        await db.SaveChangesAsync(ct);
        var idempotency=await db.IdempotencyRequests.SingleAsync(x=>x.SourceSystem==source&&x.IdempotencyKey==key,ct);
        idempotency.SigningProcessID=process.SigningProcessID;
        db.IntegrationReferences.Add(new IntegrationReference{SigningProcessID=process.SigningProcessID,SourceSystem=request.Source.System,Module=request.Source.Module,
            EntityType=request.Source.EntityType,EntityID=request.Source.EntityId,ExternalReference=request.Source.ExternalReference,
            MetadataJson=request.Metadata is null?null:JsonSerializer.Serialize(request.Metadata)});
        // La URL de callback ya NO se acepta del body (request.Callback se ignora a
        // proposito): se resuelve contra tbl_CallbackEndpoints, para que un llamador no
        // pueda decidir en caliente a donde se le avisa. Se busca primero por el ClientId
        // autenticado (integraciones API con token de aplicacion); si no hay (ej. un humano
        // creando el proceso desde la UI via "UTA-PORTAL"), se usa el "source" como llave
        // alterna para que tambien puedan suscribirse procesos creados asi.
        var callbackClientId=current.ClientId??source;
        var endpoint=await db.CallbackEndpoints.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.ClientId==callbackClientId&&x.IsActive,ct);
        if(endpoint is not null)
            db.CallbackSubscriptions.Add(new CallbackSubscription{SigningProcessID=process.SigningProcessID,Url=endpoint.Url,
                EventsJson=endpoint.EventsJson,SecretReference="Callbacks:HmacSecret"});
        db.SigningEvents.Add(new SigningEvent{SigningProcessID=process.SigningProcessID,EventType="PROCESS_CREATED",ActorUserID=current.UserId,CorrelationID=key.ToString()});
        process.CurrentDocumentVersionID=process.Documents.Single().Versions.Single().DocumentVersionID;
        // El firmante externo SIEMPRE recibe su invitacion (es su unica via de acceso,
        // a diferencia de un interno que puede revisar su bandeja); NotifyOnCreate solo
        // controla el correo generico de los firmantes internos.
        foreach(var(participant,rawToken) in externalTokens)
            db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_EXTERNAL_INVITATION_EMAIL",Payload=JsonSerializer.Serialize(new{
                participant.Email,participant.FullName,process.ProcessNumber,process.Title,
                Link=BuildExternalSignLink(participant.SigningParticipantID,rawToken)})});
        if(request.Process.NotifyOnCreate)
            foreach(var participant in process.Participants.Where(x=>!x.IsExternal))
                db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_REMINDER_EMAIL",Payload=JsonSerializer.Serialize(new{
                    process.ProcessNumber,process.Title,process.Description,Email=participant.Email,participant.FullName})});
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(process,1);
    }

    public async Task<IReadOnlyList<ProcessListItem>> ListAsync(bool inbox,CancellationToken ct)
    {
        var uid=current.UserId??throw new UnauthorizedAccessException();
        var employee=current.EmployeeId;
        var query=db.SigningProcesses.AsNoTracking().Include(x=>x.Participants).AsQueryable();
        query=inbox
            ?query.Where(x=>x.Participants.Any(p=>p.UserID==uid||(employee!=null&&p.EmployeeID==employee)))
            :query.Where(x=>x.CreatedBy==uid);
        var rows=await query.OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(p=>ToListItem(p,uid,employee)).ToList();
    }

    public async Task<ProcessDetail?> GetAsync(long id,CancellationToken ct)
    {
        var process=await LoadProcessAsync(id,ct);
        if(process is null)return null;
        EnsureCanRead(process);
        return ToDetail(process);
    }

    public async Task<ProcessProgress?> GetProgressAsync(long id,CancellationToken ct)
    {
        var p=await db.SigningProcesses.AsNoTracking().Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct);
        if(p is null)return null;
        EnsureCanRead(p);
        var required=p.Participants.Where(x=>x.Required).ToList(); var version=p.Documents.SelectMany(x=>x.Versions).Max(x=>(int?)x.SequenceNumber)??0;
        var mine=p.Participants.FirstOrDefault(x=>x.UserID==current.UserId||(current.EmployeeId!=null&&x.EmployeeID==current.EmployeeId));
        return new(p.SigningProcessID,p.ProcessNumber,p.Status.ToString().ToUpperInvariant(),required.Count,required.Count(x=>x.Status==ParticipantStatus.Signed),
            required.Count==0?0:Math.Round(required.Count(x=>x.Status==ParticipantStatus.Signed)*100m/required.Count,2),version,
            p.Participants.Select(x=>new SignerProgress(x.SigningParticipantID,x.Identification,x.FullName,x.Status.ToString().ToUpperInvariant(),x.SignedAt)).ToList(),
            mine?.SigningParticipantID);
    }

    public async Task CancelAsync(long id,CancellationToken ct)
    {
        var p=await db.SigningProcesses
            .Include(x=>x.Participants)
            .Include(x=>x.Documents)
            .ThenInclude(x=>x.Versions)
            .SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)
            ??throw new KeyNotFoundException();
        EnsureOwner(p);
        if(p.Status is ProcessStatus.Completed or ProcessStatus.Cancelled or ProcessStatus.Rejected)throw new InvalidOperationException("SIGNING_PROCESS_CANNOT_BE_CANCELLED");
        p.Status=ProcessStatus.Cancelled;p.UpdatedAt=DateTimeOffset.UtcNow;p.UpdatedBy=current.UserId;
        foreach(var signer in p.Participants.Where(x=>x.Status!=ParticipantStatus.Signed))signer.Status=ParticipantStatus.Expired;
        AddEvent(p.SigningProcessID,"PROCESS_CANCELLED",null);await db.SaveChangesAsync(ct);
    }

    public async Task RemindAsync(long id,CancellationToken ct)
    {
        var p=await db.SigningProcesses
            .Include(x=>x.Participants)
            .Include(x=>x.Documents)
            .ThenInclude(x=>x.Versions)
            .SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)
            ??throw new KeyNotFoundException();
        EnsureOwner(p);
        if(p.Status is not (ProcessStatus.InProgress or ProcessStatus.PartiallySigned))throw new InvalidOperationException("SIGNING_PROCESS_NOT_ACTIVE");
        foreach(var signer in p.Participants.Where(x=>x.Status is ParticipantStatus.Pending or ParticipantStatus.Notified or ParticipantStatus.AvailableToSign))
        {
            if(signer.IsExternal)
            {
                // El token original nunca se puede reenviar (solo se guarda su hash);
                // "recordar" a un externo significa emitirle un link nuevo e invalidar
                // el anterior, igual que un reenvio de "olvide mi contrasena".
                var(rawToken,hash)=GenerateExternalToken();
                signer.ExternalAccessTokenHash=hash;signer.ExternalAccessTokenExpiresAt=DateTimeOffset.UtcNow.AddHours(72);signer.ExternalAccessTokenUsedAt=null;
                db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_EXTERNAL_INVITATION_EMAIL",Payload=JsonSerializer.Serialize(new{
                    signer.Email,signer.FullName,p.ProcessNumber,p.Title,Link=BuildExternalSignLink(signer.SigningParticipantID,rawToken)})});
            }
            else
                db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_REMINDER_EMAIL",Payload=JsonSerializer.Serialize(new{p.SigningProcessID,p.ProcessNumber,p.Title,p.Description,signer.Email,signer.FullName})});
        }
        AddEvent(id,"REMINDER_REQUESTED",null);await db.SaveChangesAsync(ct);
    }

    public async Task<SignerProgress> AddParticipantAsync(long id,ParticipantCreateRequest request,CancellationToken ct)
    {
        var p=await db.SigningProcesses.Include(x=>x.Participants).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)??throw new KeyNotFoundException();
        EnsureOwner(p);
        if(p.Status is not (ProcessStatus.Draft or ProcessStatus.ReadyToSend))throw new InvalidOperationException("PARTICIPANTS_LOCKED");
        if(p.Participants.Any(x=>x.Identification==request.Identification))throw new InvalidOperationException("PARTICIPANT_ALREADY_EXISTS");
        string? rawToken=null;
        var participant=new SigningParticipant{PersonID=request.PersonId,EmployeeID=request.EmployeeId,Identification=request.Identification,FullName=request.FullName,
            Email=request.Email,RoleCode=request.Role,Required=request.Required,SigningOrder=request.Order,CreatedBy=current.UserId,IsExternal=request.IsExternal};
        if(request.IsExternal)
        {
            string hash;(rawToken,hash)=GenerateExternalToken();
            participant.ExternalAccessTokenHash=hash;participant.ExternalAccessTokenExpiresAt=DateTimeOffset.UtcNow.AddHours(72);
        }
        p.Participants.Add(participant);AddEvent(id,"PARTICIPANT_ADDED",JsonSerializer.Serialize(new{request.Identification}));await db.SaveChangesAsync(ct);
        if(rawToken is not null)
        {
            db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_EXTERNAL_INVITATION_EMAIL",Payload=JsonSerializer.Serialize(new{
                participant.Email,participant.FullName,p.ProcessNumber,p.Title,Link=BuildExternalSignLink(participant.SigningParticipantID,rawToken)})});
            await db.SaveChangesAsync(ct);
        }
        return ToSigner(participant);
    }

    public async Task RemoveParticipantAsync(long id,long participantId,CancellationToken ct)
    {
        var p=await db.SigningProcesses.Include(x=>x.Participants).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)??throw new KeyNotFoundException();
        EnsureOwner(p);
        if(p.Status is not (ProcessStatus.Draft or ProcessStatus.ReadyToSend))throw new InvalidOperationException("PARTICIPANTS_LOCKED");
        var participant=p.Participants.SingleOrDefault(x=>x.SigningParticipantID==participantId)??throw new KeyNotFoundException();
        db.SigningParticipants.Remove(participant);AddEvent(id,"PARTICIPANT_REMOVED",JsonSerializer.Serialize(new{participant.Identification}));await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(long id,long participantId,RejectSigningRequest request,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.Reason))throw new ArgumentException("Debe indicar el motivo del rechazo.");
        var p=await db.SigningProcesses.Include(x=>x.Participants).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)??throw new KeyNotFoundException();
        var participant=p.Participants.SingleOrDefault(x=>x.SigningParticipantID==participantId)??throw new KeyNotFoundException();
        EnsureParticipant(participant);
        if(participant.Status==ParticipantStatus.Signed)throw new InvalidOperationException("SIGNED_PARTICIPANT_CANNOT_REJECT");
        participant.Status=ParticipantStatus.Rejected;p.Status=ProcessStatus.Rejected;
        AddEvent(id,"SIGNATURE_REJECTED",JsonSerializer.Serialize(new{participantId,request.Reason}));await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEventResponse>> GetAuditAsync(long id,CancellationToken ct)
    {
        var p=await LoadProcessAsync(id,ct)??throw new KeyNotFoundException();EnsureCanRead(p);
        return await db.SigningEvents.AsNoTracking().Where(x=>x.SigningProcessID==id).OrderBy(x=>x.OccurredAt)
            .Select(x=>new AuditEventResponse(x.SigningEventID,x.EventType,x.ActorUserID,x.DataJson,x.CorrelationID,x.OccurredAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentResponse>> GetDocumentsAsync(long processId,CancellationToken ct)
    {
        var p=await LoadProcessAsync(processId,ct)??throw new KeyNotFoundException();EnsureCanRead(p);
        return p.Documents.Select(d=>new DocumentResponse(d.DocumentID,d.SigningProcessID,d.FileName,d.ContentType,d.Versions.OrderBy(v=>v.SequenceNumber).Select(ToVersion).ToList())).ToList();
    }

    public async Task<ProcessDetail?> GetByIntegrationAsync(string sourceSystem,string entityType,string entityId,CancellationToken ct)
    {
        var reference=await db.IntegrationReferences.AsNoTracking().OrderByDescending(x=>x.CreatedAt)
            .FirstOrDefaultAsync(x=>x.SourceSystem==sourceSystem&&x.EntityType==entityType&&x.EntityID==entityId,ct);
        return reference is null?null:await GetAsync(reference.SigningProcessID,ct);
    }

    public async Task<StartSigningResponse> StartSigningAsync(long id,StartSigningRequest? position,CancellationToken ct)
    {
        var uid=current.UserId??throw new UnauthorizedAccessException();
        var p=await db.SigningProcesses.Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct)??throw new KeyNotFoundException();
        var signer=p.Participants.SingleOrDefault(x=>x.UserID==uid || (current.EmployeeId!=null&&x.EmployeeID==current.EmployeeId))??throw new UnauthorizedAccessException("SIGNER_NOT_ALLOWED");
        if(signer.Status==ParticipantStatus.Signed)throw new InvalidOperationException("PARTICIPANT_ALREADY_SIGNED");
        if(p.Status is not (ProcessStatus.InProgress or ProcessStatus.PartiallySigned))throw new InvalidOperationException("SIGNING_PROCESS_NOT_ACTIVE");
        if(p.WorkflowType==WorkflowType.Sequential&&p.Participants.Any(x=>x.Required&&x.SigningOrder<signer.SigningOrder&&x.Status!=ParticipantStatus.Signed))
            throw new InvalidOperationException("SIGNING_ORDER_NOT_AVAILABLE");
        if(p.CurrentDocumentVersionID is null)throw new InvalidOperationException("El proceso no tiene versión vigente.");
        var document=p.Documents.Single();
        var currentVersion=document.Versions.Single(x=>x.DocumentVersionID==p.CurrentDocumentVersionID);
        var session=new SigningSession{SigningProcessID=id,SigningParticipantID=signer.SigningParticipantID,BaseDocumentVersionID=p.CurrentDocumentVersionID.Value,
            ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5),OneTimeTokenHash=Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),CreatedBy=uid};
        db.SigningSessions.Add(session); signer.Status=ParticipantStatus.Signing; await db.SaveChangesAsync(ct);
        try
        {
            var bytes=await hrBackend.DownloadDocumentAsync(currentVersion.FileGuid,ct);
            var firmaEcFileName=$"firmaec-{session.SigningSessionID:N}.pdf";
            var result=await firmaEc.CreateSigningRequestAsync(
                new(
                    session.SigningSessionID,
                    signer.Identification,
                    firmaEcFileName,
                    bytes,
                    $"Firma electrónica del proceso {p.ProcessNumber}",
                    position?.Page,position?.Llx,position?.Lly,position?.Width,position?.Height),
                ct);
            session.FirmaEcTransactionID=result.TransactionId;
            session.Status="LAUNCHED";
            session.ExpiresAt=result.ExpiresAt;
            await db.SaveChangesAsync(ct);
            return new(session.SigningSessionID,result.LaunchUrl,result.ExpiresAt);
        }
        catch
        {
            session.Status="FAILED";
            signer.Status=ParticipantStatus.Notified;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CompleteFirmaEcCallbackAsync(
        FirmaEcSignedDocumentCallback request,
        CancellationToken ct)
    {
        if(!request.FirmasValidas||!request.IntegridadDocumento
            ||(!string.IsNullOrWhiteSpace(request.Error)
                &&!string.Equals(request.Error,"null",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("FIRMAEC_DOCUMENT_VALIDATION_FAILED");

        var sessionId=ParseFirmaEcSessionId(request.NombreDocumento);
        var session=await db.SigningSessions
            .SingleOrDefaultAsync(x=>x.SigningSessionID==sessionId,ct)
            ??throw new KeyNotFoundException();
        var participant=await db.SigningParticipants
            .SingleAsync(x=>x.SigningParticipantID==session.SigningParticipantID,ct);
        if(session.Status=="COMPLETED")return;
        if(session.Status=="EXPIRED"||session.ExpiresAt<DateTimeOffset.UtcNow)
            throw new InvalidOperationException("SIGNING_SESSION_EXPIRED");
        if(!string.Equals(
            participant.Identification.Trim(),
            request.Cedula.Trim(),
            StringComparison.Ordinal))
            throw new UnauthorizedAccessException("FIRMAEC_SIGNER_MISMATCH");
        if(request.Certificado is null
            ||!request.Certificado.Any(c=>
                string.Equals(c.Cedula?.Trim(),request.Cedula.Trim(),StringComparison.Ordinal)
                &&c.CertificadoVigente
                &&c.IntegridadFirma
                &&c.CertificadoDigitalValido))
            throw new InvalidOperationException("FIRMAEC_CERTIFICATE_VALIDATION_FAILED");

        byte[] signedDocument;
        try
        {
            signedDocument=Convert.FromBase64String(request.Archivo);
        }
        catch(FormatException exception)
        {
            throw new ArgumentException("FirmaEC devolvió un documento Base64 inválido.",exception);
        }
        var maximum=int.TryParse(
            config["FirmaEc:MaxDocumentBytes"],
            out var configuredMaximum)
            ?configuredMaximum
            :15_728_640;
        if(signedDocument.Length==0||signedDocument.Length>maximum)
            throw new InvalidOperationException("DOCUMENT_SIZE_LIMIT_EXCEEDED");
        if(signedDocument.Length<5||!signedDocument.AsSpan(0,5).SequenceEqual("%PDF-"u8))
            throw new ArgumentException("FirmaEC devolvió un archivo que no es PDF.");

        var fileGuid=await hrBackend.UploadSignedDocumentAsync(
            signedDocument,
            request.NombreDocumento,
            session.SigningProcessID,
            ct);
        var sha256=Convert.ToHexString(SHA256.HashData(signedDocument));
        await CompleteSigningAsync(
            session.SigningProcessID,
            new(sessionId,session.BaseDocumentVersionID,fileGuid,sha256),
            ct);
    }

    public async Task CompleteSigningAsync(long id,CompleteSigningRequest request,CancellationToken ct)
    {
        var p=await db.SigningProcesses.Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleAsync(x=>x.SigningProcessID==id,ct);
        if(p.CurrentDocumentVersionID!=request.BaseDocumentVersionId)throw new DbUpdateConcurrencyException("DOCUMENT_VERSION_CHANGED");
        var session=await db.SigningSessions.SingleAsync(x=>x.SigningSessionID==request.SigningSessionId&&x.SigningProcessID==id,ct);
        if(session.Status=="COMPLETED")return;
        if(session.ExpiresAt<DateTimeOffset.UtcNow)throw new InvalidOperationException("SIGNING_SESSION_EXPIRED");
        var doc=p.Documents.Single();var previous=doc.Versions.Single(x=>x.DocumentVersionID==request.BaseDocumentVersionId);
        var signer=p.Participants.Single(x=>x.SigningParticipantID==session.SigningParticipantID);
        session.Status="COMPLETED";
        await CompleteWithNewVersionAsync(p,doc,previous,signer,request.SignedFileGuid,Convert.FromHexString(request.Sha256),request.SigningSessionId.ToString(),ct);
    }

    public async Task CompleteSigningByUploadAsync(long processId,Stream signedDocument,CancellationToken ct)
    {
        var p=await db.SigningProcesses.Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleOrDefaultAsync(x=>x.SigningProcessID==processId,ct)??throw new KeyNotFoundException();
        var signer=p.Participants.FirstOrDefault(x=>!x.IsExternal&&(x.UserID==current.UserId||(current.EmployeeId!=null&&x.EmployeeID==current.EmployeeId)))??throw new UnauthorizedAccessException("SIGNER_NOT_ALLOWED");
        await CompleteByUploadAsync(p,signer,signedDocument,ct);
    }

    public async Task CompleteExternalSigningByUploadAsync(long participantId,string token,Stream signedDocument,CancellationToken ct)
    {
        var signer=await ValidateExternalTokenAsync(participantId,token,ct,requireUnused:true);
        var p=await db.SigningProcesses.Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleAsync(x=>x.SigningProcessID==signer.SigningProcessID,ct);
        await CompleteByUploadAsync(p,signer,signedDocument,ct);
    }

    // Alternativa manual a CompleteFirmaEcCallbackAsync: el cliente movil de FirmaEC no
    // notifica solo al completar la firma (su pantalla final solo ofrece Visualizar/
    // Verificar/Compartir/Regresar — confirmado, ningun paso avisa al sistema de origen),
    // asi que el firmante sube el PDF que la app le entrego. En vez de confiar en lo que
    // FirmaEC reporta sobre si mismo (como hace el callback automatico), aqui se valida
    // criptograficamente con el mismo verificador de /validation/documents — mas riguroso
    // que el camino automatico, no menos.
    private async Task CompleteByUploadAsync(SigningProcess p,SigningParticipant signer,Stream signedDocument,CancellationToken ct)
    {
        if(signer.Status==ParticipantStatus.Signed)return;
        if(p.Status is not (ProcessStatus.InProgress or ProcessStatus.PartiallySigned))throw new InvalidOperationException("SIGNING_PROCESS_NOT_ACTIVE");
        if(p.WorkflowType==WorkflowType.Sequential&&p.Participants.Any(x=>x.Required&&x.SigningOrder<signer.SigningOrder&&x.Status!=ParticipantStatus.Signed))
            throw new InvalidOperationException("SIGNING_ORDER_NOT_AVAILABLE");

        using var ms=new MemoryStream();
        await signedDocument.CopyToAsync(ms,ct);
        var bytes=ms.ToArray();
        ms.Position=0;
        var result=await pdfValidator.ValidateDocumentAsync(ms,ct);
        if(!result.IsSigned||!result.IsIntegrityValid)throw new InvalidOperationException("UPLOADED_DOCUMENT_NOT_VALIDLY_SIGNED");
        var matches=result.Signers.Any(s=>string.Equals(s.Identification?.Trim(),signer.Identification.Trim(),StringComparison.Ordinal)&&s.SignatureStatus=="FIRMA_VALIDA");
        if(!matches)throw new UnauthorizedAccessException("UPLOADED_DOCUMENT_SIGNER_MISMATCH");

        var doc=p.Documents.Single();
        var previous=doc.Versions.Single(x=>x.DocumentVersionID==p.CurrentDocumentVersionID);
        var fileGuid=await hrBackend.UploadSignedDocumentAsync(bytes,$"firmado-{p.ProcessNumber}.pdf",p.SigningProcessID,ct);
        await CompleteWithNewVersionAsync(p,doc,previous,signer,fileGuid,SHA256.HashData(bytes),$"upload:{signer.SigningParticipantID}",ct);
    }

    private async Task CompleteWithNewVersionAsync(SigningProcess p,Document doc,DocumentVersion previous,SigningParticipant signer,Guid fileGuid,byte[] sha256,string correlationId,CancellationToken ct)
    {
        var version=new DocumentVersion{DocumentID=doc.DocumentID,SequenceNumber=previous.SequenceNumber+1,PreviousVersionID=previous.DocumentVersionID,PreviousSha256=previous.Sha256,
            Sha256=sha256,FileGuid=fileGuid,CreatedBy=current.UserId};
        doc.Versions.Add(version);signer.Status=ParticipantStatus.Signed;signer.SignedAt=DateTimeOffset.UtcNow;
        if(signer.IsExternal)signer.ExternalAccessTokenUsedAt=DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);p.CurrentDocumentVersionID=version.DocumentVersionID;
        var completed=p.Participants.Where(x=>x.Required).All(x=>x.Status==ParticipantStatus.Signed);
        p.Status=completed?ProcessStatus.Completed:ProcessStatus.PartiallySigned;
        db.SigningEvents.Add(new SigningEvent{SigningProcessID=p.SigningProcessID,EventType="SIGNATURE_COMPLETED",ActorUserID=current.UserId,CorrelationID=correlationId});await db.SaveChangesAsync(ct);
        if(completed)
        {
            db.SigningEvents.Add(new SigningEvent{SigningProcessID=p.SigningProcessID,EventType="PROCESS_COMPLETED",ActorUserID=current.UserId,CorrelationID=correlationId});
            db.OutboxMessages.Add(new OutboxMessage{Type="SIGNATURE_FINAL_DOCUMENT_EMAIL",
                Payload=JsonSerializer.Serialize(new{p.SigningProcessID,p.ProcessNumber,p.Title,RecipientEmail=p.CreatorEmail,
                    FinalDocumentVersionID=version.DocumentVersionID,version.FileGuid,IdempotencyKey=$"final-document:{p.ProcessGuid}"})});
            db.OutboxMessages.Add(new OutboxMessage{Type="PROCESS_COMPLETED",Payload=JsonSerializer.Serialize(new{p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Status})});
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task<PublicParticipantInfoResponse> GetPublicParticipantAsync(long participantId,string token,CancellationToken ct)
    {
        var participant=await ValidateExternalTokenAsync(participantId,token,ct,requireUnused:false);
        var expired=participant.ExternalAccessTokenExpiresAt is null||participant.ExternalAccessTokenExpiresAt<DateTimeOffset.UtcNow;
        var alreadyUsed=participant.ExternalAccessTokenUsedAt is not null||participant.Status==ParticipantStatus.Signed||expired;
        return new(participant.SigningParticipantID,participant.FullName,participant.Process.ProcessNumber,participant.Process.Title,participant.Process.Description,
            participant.Process.CreatorEmail,participant.Status.ToString().ToUpperInvariant(),alreadyUsed);
    }

    public async Task<(byte[] Content,string FileName)> GetPublicDocumentAsync(long participantId,string token,CancellationToken ct)
    {
        var participant=await ValidateExternalTokenAsync(participantId,token,ct,requireUnused:false);
        if(participant.ExternalAccessTokenExpiresAt is null||participant.ExternalAccessTokenExpiresAt<DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("EXTERNAL_LINK_EXPIRED");
        var p=await db.SigningProcesses.Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleAsync(x=>x.SigningProcessID==participant.SigningProcessID,ct);
        var document=p.Documents.Single();
        var version=document.Versions.SingleOrDefault(x=>x.DocumentVersionID==p.CurrentDocumentVersionID)??throw new InvalidOperationException("El proceso no tiene versión vigente.");
        var bytes=await hrBackend.DownloadDocumentAsync(version.FileGuid,ct);
        return(bytes,document.FileName);
    }

    public async Task<StartSigningResponse> StartExternalSigningAsync(long participantId,string token,StartSigningRequest? position,CancellationToken ct)
    {
        var signer=await ValidateExternalTokenAsync(participantId,token,ct,requireUnused:true);
        var p=await db.SigningProcesses.Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleAsync(x=>x.SigningProcessID==signer.SigningProcessID,ct);
        if(p.Status is not (ProcessStatus.InProgress or ProcessStatus.PartiallySigned))throw new InvalidOperationException("SIGNING_PROCESS_NOT_ACTIVE");
        if(p.WorkflowType==WorkflowType.Sequential&&p.Participants.Any(x=>x.Required&&x.SigningOrder<signer.SigningOrder&&x.Status!=ParticipantStatus.Signed))
            throw new InvalidOperationException("SIGNING_ORDER_NOT_AVAILABLE");
        if(p.CurrentDocumentVersionID is null)throw new InvalidOperationException("El proceso no tiene versión vigente.");
        var document=p.Documents.Single();
        var currentVersion=document.Versions.Single(x=>x.DocumentVersionID==p.CurrentDocumentVersionID);
        var session=new SigningSession{SigningProcessID=p.SigningProcessID,SigningParticipantID=signer.SigningParticipantID,BaseDocumentVersionID=p.CurrentDocumentVersionID.Value,
            ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5),OneTimeTokenHash=Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32)))};
        db.SigningSessions.Add(session);signer.Status=ParticipantStatus.Signing;await db.SaveChangesAsync(ct);
        try
        {
            var bytes=await hrBackend.DownloadDocumentAsync(currentVersion.FileGuid,ct);
            var firmaEcFileName=$"firmaec-{session.SigningSessionID:N}.pdf";
            var result=await firmaEc.CreateSigningRequestAsync(
                new(session.SigningSessionID,signer.Identification,firmaEcFileName,bytes,$"Firma electrónica del proceso {p.ProcessNumber}",
                    position?.Page,position?.Llx,position?.Lly,position?.Width,position?.Height),
                ct);
            session.FirmaEcTransactionID=result.TransactionId;session.Status="LAUNCHED";session.ExpiresAt=result.ExpiresAt;
            await db.SaveChangesAsync(ct);
            return new(session.SigningSessionID,result.LaunchUrl,result.ExpiresAt);
        }
        catch
        {
            session.Status="FAILED";signer.Status=ParticipantStatus.Notified;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    // Compara el hash del token recibido contra el guardado en tiempo constante para no
    // filtrar por timing si el participantId es valido. El mismo mensaje generico de error
    // se usa para "no existe" y "token no coincide" para no permitir enumerar participantId.
    private async Task<SigningParticipant> ValidateExternalTokenAsync(long participantId,string token,CancellationToken ct,bool requireUnused)
    {
        var participant=await db.SigningParticipants.Include(x=>x.Process).SingleOrDefaultAsync(x=>x.SigningParticipantID==participantId&&x.IsExternal,ct);
        var providedHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token??"")));
        var storedHash=participant?.ExternalAccessTokenHash;
        var matches=storedHash is not null&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash),Encoding.UTF8.GetBytes(providedHash));
        if(participant is null||!matches)throw new UnauthorizedAccessException("EXTERNAL_LINK_INVALID");
        if(requireUnused)
        {
            if(participant.ExternalAccessTokenExpiresAt is null||participant.ExternalAccessTokenExpiresAt<DateTimeOffset.UtcNow)
                throw new UnauthorizedAccessException("EXTERNAL_LINK_EXPIRED");
            if(participant.ExternalAccessTokenUsedAt is not null||participant.Status==ParticipantStatus.Signed)
                throw new UnauthorizedAccessException("EXTERNAL_LINK_ALREADY_USED");
        }
        return participant;
    }

    private static (string RawToken,string Hash) GenerateExternalToken()
    {
        var raw=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return(raw,hash);
    }

    private string BuildExternalSignLink(long participantId,string rawToken)=>
        $"{config["Frontend:PublicBaseUrl"]?.TrimEnd('/')}/firma-externa/{participantId}?token={rawToken}";

    private static byte[] ParseHash(string? value)=>string.IsNullOrWhiteSpace(value)?SHA256.HashData([]):Convert.FromHexString(value);
    private static Guid ParseFirmaEcSessionId(string fileName)
    {
        const string prefix="firmaec-";
        const string suffix=".pdf";
        if(!fileName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)
            ||!fileName.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El nombre del documento FirmaEC no contiene una sesión válida.");
        var value=fileName[prefix.Length..^suffix.Length];
        return Guid.TryParseExact(value,"N",out var sessionId)
            ?sessionId
            :throw new ArgumentException("El identificador de sesión FirmaEC no es válido.");
    }
    private static CreateProcessResponse Map(SigningProcess p,int version)=>new(p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Status.ToString().ToUpperInvariant(),version,$"/signatures/processes/{p.SigningProcessID}",p.CreatedAt);
    private async Task<SigningProcess?> LoadProcessAsync(long id,CancellationToken ct)=>await db.SigningProcesses.AsNoTracking().Include(x=>x.Participants).Include(x=>x.Documents).ThenInclude(x=>x.Versions).SingleOrDefaultAsync(x=>x.SigningProcessID==id,ct);
    private void EnsureCanRead(SigningProcess p){var uid=current.UserId??throw new UnauthorizedAccessException();if(p.CreatedBy==uid)return;if(p.Participants.Any(x=>x.UserID==uid||(current.EmployeeId!=null&&x.EmployeeID==current.EmployeeId)))return;throw new UnauthorizedAccessException("SIGNING_PROCESS_ACCESS_DENIED");}
    private void EnsureOwner(SigningProcess p){if(p.CreatedBy!=current.UserId)throw new UnauthorizedAccessException("SIGNING_PROCESS_ACCESS_DENIED");}
    private void EnsureParticipant(SigningParticipant p){var uid=current.UserId??throw new UnauthorizedAccessException();if(p.UserID!=uid&&(current.EmployeeId==null||p.EmployeeID!=current.EmployeeId))throw new UnauthorizedAccessException("SIGNER_NOT_ALLOWED");}
    private void AddEvent(long id,string type,string? data)=>db.SigningEvents.Add(new SigningEvent{SigningProcessID=id,EventType=type,ActorUserID=current.UserId,DataJson=data,CorrelationID=Guid.NewGuid().ToString()});
    private static SignerProgress ToSigner(SigningParticipant x)=>new(x.SigningParticipantID,x.Identification,x.FullName,x.Status.ToString().ToUpperInvariant(),x.SignedAt);
    private static DocumentVersionResponse ToVersion(DocumentVersion v)=>new(v.DocumentVersionID,v.SequenceNumber,v.PreviousVersionID,Convert.ToHexString(v.Sha256),v.FileGuid,v.SizeBytes,v.PageCount,v.CreatedAt);
    private static ProcessListItem ToListItem(SigningProcess p,Guid uid,long? employee)
    {
        var mine=p.Participants.FirstOrDefault(x=>x.UserID==uid||(employee!=null&&x.EmployeeID==employee));
        return new(p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Title,p.Status.ToString().ToUpperInvariant(),p.WorkflowType.ToString().ToUpperInvariant(),p.CreatedAt,p.ExpiresAt,p.Progress,mine?.Status.ToString().ToUpperInvariant(),mine?.SigningParticipantID);
    }
    private static ProcessDetail ToDetail(SigningProcess p)=>new(p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Title,p.Description,p.Status.ToString().ToUpperInvariant(),p.WorkflowType.ToString().ToUpperInvariant(),p.CreatorEmail,p.CreatedAt,p.ExpiresAt,p.Documents.SelectMany(x=>x.Versions).Max(x=>(int?)x.SequenceNumber)??0,p.Participants.Select(ToSigner).ToList());
}
