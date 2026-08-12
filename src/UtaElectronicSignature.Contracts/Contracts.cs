namespace UtaElectronicSignature.Contracts;

public sealed record SourceRequest(string System, string Module, string EntityType, string EntityId, string? ExternalReference);
public sealed record DocumentRequest(Guid FileGuid, string FileName, string? Sha256);
public sealed record ProcessRequest(string Title, string? Description, string WorkflowType, DateTimeOffset? ExpiresAt, int MinimumRequiredSignatures, bool NotifyOnCreate = true);
public sealed record SignerRequest(long? PersonId, long? EmployeeId, string Identification, string FullName, string Email, string Role, bool Required, int? Order, bool IsExternal = false);
public sealed record CallbackRequest(Uri Url, string[] Events);
public sealed record CreateIntegrationRequest(SourceRequest Source, DocumentRequest Document, ProcessRequest Process, IReadOnlyList<SignerRequest> Signers, CallbackRequest? Callback, Dictionary<string, string>? Metadata);
public sealed record CreateProcessResponse(long ProcessId, Guid ProcessGuid, string ProcessNumber, string Status, int DocumentVersion, string SignatureUrl, DateTimeOffset CreatedAt);
public sealed record SignerProgress(long ParticipantId, string Identification, string FullName, string Status, DateTimeOffset? SignedAt);
public sealed record ProcessProgress(long ProcessId, string ProcessNumber, string Status, int TotalRequiredSigners, int SignedRequiredSigners, decimal ProgressPercentage, int CurrentDocumentVersion, IReadOnlyList<SignerProgress> Signers, long? MyParticipantId);
public sealed record StartSigningResponse(Guid SigningSessionId, string LaunchUrl, DateTimeOffset ExpiresAt);
// Posicion del sello elegida por el usuario en el visor interactivo del frontend (opcional:
// si se omite, se usa la posicion estatica por defecto de FirmaEc:Stamp* en configuracion).
// El tamaño del cuadro queda estandarizado (ver FirmaEcOptions.StampWidth/Height); solo la
// esquina inferior izquierda (Llx,Lly) y la pagina son elegibles.
public sealed record StartSigningRequest(int? Page, int? Llx, int? Lly, int? Width = null, int? Height = null);
public sealed record CompleteSigningRequest(Guid SigningSessionId, long BaseDocumentVersionId, Guid SignedFileGuid, string Sha256);
public sealed record FirmaEcCreateRequest(Guid SessionId, string Identification, string FileName, byte[] Document, string? Reason, int? Page = null, int? Llx = null, int? Lly = null, int? Width = null, int? Height = null);
public sealed record FirmaEcCreateResult(string TransactionId, string LaunchUrl, DateTimeOffset ExpiresAt);
public sealed record FirmaEcSignedDocumentCallback(
    string Cedula,
    string NombreDocumento,
    string Archivo,
    bool FirmasValidas,
    bool IntegridadDocumento,
    string? Error,
    IReadOnlyList<FirmaEcCertificateCallback>? Certificado);
public sealed record FirmaEcCertificateCallback(
    string? Cedula,
    string? Nombre,
    string? Apellido,
    string? EmitidoPara,
    string? EmitidoPor,
    string? FechaFirma,
    bool CertificadoVigente,
    bool IntegridadFirma,
    bool CertificadoDigitalValido,
    string? Serial);
public sealed record DocumentValidationResult(string Status, bool IsSigned, bool IsIntegrityValid, string Sha256, int SignatureCount, IReadOnlyList<ValidatedSigner> Signers, IReadOnlyList<string> Warnings);
public sealed record ValidatedSigner(string FullName, string Identification, DateTimeOffset? SignedAt, string SignatureStatus, string? Issuer, string? SerialNumber, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, string RevocationStatus);
// Validacion de un archivo .p12/.pfx (certificado personal + llave privada protegida por
// contraseña): solo se leen los datos PUBLICOS del certificado (titular, vigencia, emisor).
// La contraseña se usa unicamente en memoria para abrir el archivo y nunca se persiste ni
// se registra en logs.
public sealed record CertificateValidationResult(string Subject, string? Identification, string Issuer, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, bool IsCurrentlyValid, string SerialNumber);
public sealed record ProcessListItem(long ProcessId, Guid ProcessGuid, string ProcessNumber, string Title, string Status, string WorkflowType, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, decimal ProgressPercentage, string? MyParticipantStatus, long? MyParticipantId, string CreatorEmail);
public sealed record ProcessDetail(long ProcessId, Guid ProcessGuid, string ProcessNumber, string Title, string? Description, string Status, string WorkflowType, string CreatorEmail, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, int CurrentDocumentVersion, IReadOnlyList<SignerProgress> Signers);
public sealed record ParticipantCreateRequest(long? PersonId, long? EmployeeId, string Identification, string FullName, string Email, string Role, bool Required, int? Order, bool IsExternal = false);
public sealed record PublicParticipantInfoResponse(long ParticipantId, string FullName, string ProcessNumber, string Title, string? Description, string CreatorEmail, string Status, bool AlreadyUsed);
public sealed record RejectSigningRequest(string Reason);
public sealed record AuditEventResponse(long EventId, string EventType, Guid? ActorUserId, string? DataJson, string CorrelationId, DateTimeOffset OccurredAt);
public sealed record DocumentVersionResponse(long VersionId, int SequenceNumber, long? PreviousVersionId, string Sha256, Guid FileGuid, long SizeBytes, int? PageCount, DateTimeOffset CreatedAt);
public sealed record DocumentResponse(long DocumentId, long ProcessId, string FileName, string ContentType, IReadOnlyList<DocumentVersionResponse> Versions);
public sealed record CallbackEndpointResponse(long CallbackEndpointId, string ClientId, string Url, string[] Events, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public sealed record CallbackEndpointCreateRequest(string ClientId, Uri Url, string[] Events);
public sealed record CallbackEndpointUpdateRequest(Uri Url, string[] Events, bool IsActive);
