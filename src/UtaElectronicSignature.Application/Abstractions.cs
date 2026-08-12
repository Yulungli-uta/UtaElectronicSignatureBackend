using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.Application;

public static class SignaturePermissions
{
    public const string ProcessCreate="SIGNATURE.PROCESS.CREATE", ProcessReadOwn="SIGNATURE.PROCESS.READ_OWN", ProcessReadAll="SIGNATURE.PROCESS.READ_ALL";
    public const string ProcessSend="SIGNATURE.PROCESS.SEND", ProcessCancel="SIGNATURE.PROCESS.CANCEL", ProcessRemind="SIGNATURE.PROCESS.REMIND";
    public const string DocumentSign="SIGNATURE.DOCUMENT.SIGN", DocumentReject="SIGNATURE.DOCUMENT.REJECT", DocumentDownload="SIGNATURE.DOCUMENT.DOWNLOAD";
    public const string DocumentValidate="SIGNATURE.DOCUMENT.VALIDATE", ReportDownload="SIGNATURE.REPORT.DOWNLOAD", ConfigManage="SIGNATURE.CONFIG.MANAGE";
    public const string AuditRead="SIGNATURE.AUDIT.READ", IntegrationCreate="SIGNATURE.INTEGRATION.CREATE", IntegrationRead="SIGNATURE.INTEGRATION.READ";
    public static readonly string[] All=[ProcessCreate,ProcessReadOwn,ProcessReadAll,ProcessSend,ProcessCancel,ProcessRemind,DocumentSign,DocumentReject,DocumentDownload,DocumentValidate,ReportDownload,ConfigManage,AuditRead,IntegrationCreate,IntegrationRead];
}

public static class InstitutionalClaimTypes
{
    public const string UserId="sub", EmployeeId="employeeId", Email="email", SessionId="sid", Permission="permission";
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    long? EmployeeId { get; }
    string? Email { get; }
    string? SessionId { get; }
    /// <summary>ClientId del token de aplicacion (RepositoryUta client-credentials), null para tokens de usuario humano.</summary>
    string? ClientId { get; }
    bool IsAuthenticated { get; }
}

public interface ISigningProcessService
{
    Task<CreateProcessResponse> CreateAsync(CreateIntegrationRequest request, Guid idempotencyKey, string sourceSystem, CancellationToken ct);
    Task<IReadOnlyList<ProcessListItem>> ListAsync(bool inbox, CancellationToken ct);
    Task<IReadOnlyList<ProcessListItem>> ListAllAsync(CancellationToken ct);
    Task<ProcessDetail?> GetAsync(long id, CancellationToken ct);
    Task<ProcessProgress?> GetProgressAsync(long id, CancellationToken ct);
    Task CancelAsync(long id, CancellationToken ct);
    Task RemindAsync(long id, CancellationToken ct);
    Task<SignerProgress> AddParticipantAsync(long id, ParticipantCreateRequest request, CancellationToken ct);
    Task RemoveParticipantAsync(long id, long participantId, CancellationToken ct);
    Task RejectAsync(long id, long participantId, RejectSigningRequest request, CancellationToken ct);
    Task<IReadOnlyList<AuditEventResponse>> GetAuditAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<DocumentResponse>> GetDocumentsAsync(long processId, CancellationToken ct);
    Task<ProcessDetail?> GetByIntegrationAsync(string sourceSystem, string entityType, string entityId, CancellationToken ct);
    Task<StartSigningResponse> StartSigningAsync(long id, StartSigningRequest? position, CancellationToken ct);
    Task CompleteSigningAsync(long id, CompleteSigningRequest request, CancellationToken ct);
    Task CompleteFirmaEcCallbackAsync(FirmaEcSignedDocumentCallback request, CancellationToken ct);
    Task<PublicParticipantInfoResponse> GetPublicParticipantAsync(long participantId, string token, CancellationToken ct);
    Task<StartSigningResponse> StartExternalSigningAsync(long participantId, string token, StartSigningRequest? position, CancellationToken ct);
    Task<(byte[] Content, string FileName)> GetPublicDocumentAsync(long participantId, string token, CancellationToken ct);
    // Alternativa a CompleteFirmaEcCallbackAsync para cuando el cliente movil de FirmaEC no
    // notifica solo (confirmado: su pantalla final solo ofrece Visualizar/Verificar/Compartir/
    // Regresar, sin ningun paso que avise al sistema de origen). El firmante sube el PDF que
    // la app le entrego y este metodo lo valida criptograficamente antes de aceptarlo.
    Task CompleteSigningByUploadAsync(long processId, Stream signedDocument, CancellationToken ct);
    Task CompleteExternalSigningByUploadAsync(long participantId, string token, Stream signedDocument, CancellationToken ct);
}

public interface IFirmaEcClient
{
    Task<FirmaEcCreateResult> CreateSigningRequestAsync(FirmaEcCreateRequest request, CancellationToken ct);
}

// Validacion local de PDFs firmados (lectura de la firma PKCS#7/CMS embebida): deliberadamente
// separada de IFirmaEcClient porque NO llama a FirmaEC ni depende de su contrato — funciona
// para cualquier PDF firmado, y asi puede reutilizarse desde cualquier otro flujo de este
// backend sin arrastrar la configuracion/HttpClient de FirmaEc.
public interface IPdfSignatureValidator
{
    Task<DocumentValidationResult> ValidateDocumentAsync(Stream document, CancellationToken ct);
}

// Lectura de datos publicos de un certificado personal .p12/.pfx (titular, vigencia,
// emisor). La contraseña solo se usa en memoria para abrir el archivo, nunca se persiste.
public interface ICertificateValidator
{
    CertificateValidationResult Validate(byte[] pkcs12, string password);
}

public interface IDocumentStorage
{
    Task<Stream> OpenReadAsync(Guid fileGuid, CancellationToken ct);
    Task<Guid> StoreImmutableAsync(Stream content, string fileName, string contentType, CancellationToken ct);
}

public interface IPersonDirectoryClient
{
    Task<SignerRequest?> ResolveAsync(long? personId, long? employeeId, CancellationToken ct);
}

public interface ICallbackEndpointService
{
    Task<IReadOnlyList<CallbackEndpointResponse>> ListAsync(CancellationToken ct);
    Task<CallbackEndpointResponse> CreateAsync(CallbackEndpointCreateRequest request, CancellationToken ct);
    Task<CallbackEndpointResponse> UpdateAsync(long id, CallbackEndpointUpdateRequest request, CancellationToken ct);
    Task DeactivateAsync(long id, CancellationToken ct);
}
