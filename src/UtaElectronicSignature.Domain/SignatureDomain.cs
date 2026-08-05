namespace UtaElectronicSignature.Domain;

public enum WorkflowType { Unordered, Sequential }
public enum ProcessStatus { Draft, ReadyToSend, InProgress, PartiallySigned, Completed, Rejected, Cancelled, Expired, Observed, ValidationFailed }
public enum ParticipantStatus { Pending, Notified, Viewed, AvailableToSign, Signing, Signed, Rejected, Expired, RetryRequired, ValidationFailed }
public enum ValidationStatus { Valid, Invalid, Warning, Unknown, NotSigned, IntegrityFailed, CertificateRevoked, CertificateExpired, CertificateUntrusted, ValidationError }

public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SigningProcess : AuditableEntity
{
    public long SigningProcessID { get; set; }
    public Guid ProcessGuid { get; set; } = Guid.NewGuid();
    public string ProcessNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string CreatorEmail { get; set; } = "";
    public WorkflowType WorkflowType { get; set; }
    public ProcessStatus Status { get; set; } = ProcessStatus.Draft;
    public int MinimumRequiredSignatures { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public long? CurrentDocumentVersionID { get; set; }
    public ICollection<SigningParticipant> Participants { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];

    public decimal Progress => MinimumRequiredSignatures == 0 ? 0 :
        Math.Round(Participants.Count(x => x.Required && x.Status == ParticipantStatus.Signed) * 100m / MinimumRequiredSignatures, 2);
}

public sealed class SigningParticipant : AuditableEntity
{
    public long SigningParticipantID { get; set; }
    public long SigningProcessID { get; set; }
    public SigningProcess Process { get; set; } = null!;
    public Guid? UserID { get; set; }
    public long? PersonID { get; set; }
    public long? EmployeeID { get; set; }
    public string Identification { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? JobName { get; set; }
    public string? DepartmentName { get; set; }
    public string RoleCode { get; set; } = "SIGNER";
    public bool Required { get; set; } = true;
    public int? SigningOrder { get; set; }
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Pending;
    public DateTimeOffset? SignedAt { get; set; }
    // Firmante externo (sin usuario/empleado interno): accede via link publico de un
    // solo uso en vez de login normal. Hash SHA-256 del token (nunca el token en claro),
    // igual patron que SigningSession.OneTimeTokenHash.
    public bool IsExternal { get; set; }
    public string? ExternalAccessTokenHash { get; set; }
    public DateTimeOffset? ExternalAccessTokenExpiresAt { get; set; }
    public DateTimeOffset? ExternalAccessTokenUsedAt { get; set; }
}

public sealed class Document : AuditableEntity
{
    public long DocumentID { get; set; }
    public long SigningProcessID { get; set; }
    public SigningProcess Process { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public ICollection<DocumentVersion> Versions { get; set; } = [];
}

public sealed class DocumentVersion : AuditableEntity
{
    public long DocumentVersionID { get; set; }
    public long DocumentID { get; set; }
    public Document Document { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public long? PreviousVersionID { get; set; }
    public byte[]? PreviousSha256 { get; set; }
    public byte[] Sha256 { get; set; } = [];
    public Guid FileGuid { get; set; }
    public long SizeBytes { get; set; }
    public int? PageCount { get; set; }
    public long? SignatureID { get; set; }
}

public sealed class SigningSession : AuditableEntity
{
    public Guid SigningSessionID { get; set; } = Guid.NewGuid();
    public long SigningProcessID { get; set; }
    public long SigningParticipantID { get; set; }
    public long BaseDocumentVersionID { get; set; }
    public string? FirmaEcTransactionID { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset ExpiresAt { get; set; }
    public string OneTimeTokenHash { get; set; } = "";
}

public sealed class OutboxMessage
{
    public Guid OutboxMessageID { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class SigningEvent
{
    public long SigningEventID { get; set; }
    public long SigningProcessID { get; set; }
    public string EventType { get; set; } = "";
    public Guid? ActorUserID { get; set; }
    public string? DataJson { get; set; }
    public string CorrelationID { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IdempotencyRequest
{
    public long IdempotencyRequestID { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string SourceSystem { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long? SigningProcessID { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class IntegrationReference
{
    public long IntegrationReferenceID { get; set; }
    public long SigningProcessID { get; set; }
    public string SourceSystem { get; set; } = "";
    public string Module { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityID { get; set; } = "";
    public string? ExternalReference { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CallbackSubscription
{
    public long CallbackSubscriptionID { get; set; }
    public long SigningProcessID { get; set; }
    public string Url { get; set; } = "";
    public string EventsJson { get; set; } = "[]";
    public string SecretReference { get; set; } = "Callbacks:HmacSecret";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Configuracion permanente por sistema consumidor: a que URL avisar cuando ese
/// ClientId (autenticado via RepositoryUta) cree procesos de firma. Distinto de
/// <see cref="CallbackSubscription"/>, que es un registro por proceso individual.
/// </summary>
public sealed class CallbackEndpoint
{
    public long CallbackEndpointID { get; set; }
    public string ClientId { get; set; } = "";
    public string Url { get; set; } = "";
    public string EventsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
