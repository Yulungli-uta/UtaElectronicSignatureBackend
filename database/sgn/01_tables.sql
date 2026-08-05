SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE [SGN].[tbl_SigningProcesses](
 [SigningProcessID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SigningProcesses] PRIMARY KEY,
 [ProcessGuid] uniqueidentifier NOT NULL CONSTRAINT [DF_tbl_SigningProcesses_ProcessGuid] DEFAULT NEWSEQUENTIALID(),
 [ProcessNumber] nvarchar(30) NOT NULL,[Title] nvarchar(250) NOT NULL,[Description] nvarchar(1000) NULL,
 [WorkflowType] varchar(20) NOT NULL,[Status] varchar(30) NOT NULL,[MinimumRequiredSignatures] int NOT NULL,
 [ExpiresAt] datetimeoffset NULL,[CurrentDocumentVersionID] bigint NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningProcesses_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [CreatedBy] uniqueidentifier NULL,[UpdatedAt] datetimeoffset NULL,[UpdatedBy] uniqueidentifier NULL,[RowVersion] rowversion NOT NULL,
 CONSTRAINT [CK_tbl_SigningProcesses_WorkflowType] CHECK ([WorkflowType] IN ('UNORDERED','SEQUENTIAL')),
 CONSTRAINT [CK_tbl_SigningProcesses_MinimumRequiredSignatures] CHECK ([MinimumRequiredSignatures]>0)
);

CREATE TABLE [SGN].[tbl_Documents](
 [DocumentID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_Documents] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[FileName] nvarchar(260) NOT NULL,[ContentType] varchar(100) NOT NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_Documents_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [CreatedBy] uniqueidentifier NULL,[UpdatedAt] datetimeoffset NULL,[UpdatedBy] uniqueidentifier NULL,[RowVersion] rowversion NOT NULL,
 CONSTRAINT [FK_tbl_Documents_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID])
);

CREATE TABLE [SGN].[tbl_SigningParticipants](
 [SigningParticipantID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SigningParticipants] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[UserID] uniqueidentifier NULL,[PersonID] bigint NULL,[EmployeeID] bigint NULL,
 [Identification] nvarchar(20) NOT NULL,[FullName] nvarchar(250) NOT NULL,[Email] nvarchar(320) NOT NULL,
 [JobName] nvarchar(250) NULL,[DepartmentName] nvarchar(250) NULL,[RoleCode] varchar(50) NOT NULL,
 [Required] bit NOT NULL,[SigningOrder] int NULL,[Status] varchar(30) NOT NULL,[SignedAt] datetimeoffset NULL,
 [IsExternal] bit NOT NULL CONSTRAINT [DF_tbl_SigningParticipants_IsExternal] DEFAULT (0),
 [ExternalAccessTokenHash] char(64) NULL,[ExternalAccessTokenExpiresAt] datetimeoffset NULL,[ExternalAccessTokenUsedAt] datetimeoffset NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningParticipants_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [CreatedBy] uniqueidentifier NULL,[UpdatedAt] datetimeoffset NULL,[UpdatedBy] uniqueidentifier NULL,[RowVersion] rowversion NOT NULL,
 CONSTRAINT [FK_tbl_SigningParticipants_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID])
);

CREATE TABLE [SGN].[tbl_DocumentVersions](
 [DocumentVersionID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_DocumentVersions] PRIMARY KEY,
 [DocumentID] bigint NOT NULL,[SequenceNumber] int NOT NULL,[PreviousVersionID] bigint NULL,
 [PreviousSha256] binary(32) NULL,[Sha256] binary(32) NOT NULL,[FileGuid] uniqueidentifier NOT NULL,
 [SizeBytes] bigint NOT NULL,[PageCount] int NULL,[SignatureID] bigint NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_DocumentVersions_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [CreatedBy] uniqueidentifier NULL,[UpdatedAt] datetimeoffset NULL,[UpdatedBy] uniqueidentifier NULL,[RowVersion] rowversion NOT NULL,
 CONSTRAINT [FK_tbl_DocumentVersions_tbl_Documents_DocumentID] FOREIGN KEY([DocumentID]) REFERENCES [SGN].[tbl_Documents]([DocumentID]),
 CONSTRAINT [FK_tbl_DocumentVersions_tbl_DocumentVersions_PreviousVersionID] FOREIGN KEY([PreviousVersionID]) REFERENCES [SGN].[tbl_DocumentVersions]([DocumentVersionID]),
 CONSTRAINT [CK_tbl_DocumentVersions_SequenceNumber] CHECK ([SequenceNumber]>0)
);

CREATE TABLE [SGN].[tbl_Signatures](
 [SignatureID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_Signatures] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[SigningParticipantID] bigint NOT NULL,[BaseDocumentVersionID] bigint NOT NULL,[ResultDocumentVersionID] bigint NULL,
 [Identification] nvarchar(20) NOT NULL,[CertificateSerialNumber] nvarchar(200) NULL,[CertificateIssuer] nvarchar(500) NULL,
 [SignedAt] datetimeoffset NOT NULL,[ValidationStatus] varchar(30) NOT NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_Signatures_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),[CreatedBy] uniqueidentifier NULL,
 CONSTRAINT [FK_tbl_Signatures_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID]),
 CONSTRAINT [FK_tbl_Signatures_tbl_SigningParticipants_SigningParticipantID] FOREIGN KEY([SigningParticipantID]) REFERENCES [SGN].[tbl_SigningParticipants]([SigningParticipantID])
);

CREATE TABLE [SGN].[tbl_SigningSessions](
 [SigningSessionID] uniqueidentifier NOT NULL CONSTRAINT [PK_tbl_SigningSessions] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[SigningParticipantID] bigint NOT NULL,[BaseDocumentVersionID] bigint NOT NULL,
 [FirmaEcTransactionID] nvarchar(200) NULL,[Status] varchar(30) NOT NULL,[ExpiresAt] datetimeoffset NOT NULL,[OneTimeTokenHash] char(64) NOT NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningSessions_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [CreatedBy] uniqueidentifier NULL,[UpdatedAt] datetimeoffset NULL,[UpdatedBy] uniqueidentifier NULL,[RowVersion] rowversion NOT NULL,
 CONSTRAINT [FK_tbl_SigningSessions_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID])
);

CREATE TABLE [SGN].[tbl_SigningReservations](
 [SigningReservationID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SigningReservations] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[SigningSessionID] uniqueidentifier NOT NULL,[BaseDocumentVersionID] bigint NOT NULL,
 [Status] varchar(20) NOT NULL,[ExpiresAt] datetimeoffset NOT NULL,[CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningReservations_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 CONSTRAINT [FK_tbl_SigningReservations_tbl_SigningSessions_SigningSessionID] FOREIGN KEY([SigningSessionID]) REFERENCES [SGN].[tbl_SigningSessions]([SigningSessionID])
);

CREATE TABLE [SGN].[tbl_SigningEvents](
 [SigningEventID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SigningEvents] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[EventType] varchar(50) NOT NULL,[ActorUserID] uniqueidentifier NULL,
 [DataJson] nvarchar(max) NULL,[CorrelationID] nvarchar(100) NOT NULL,[OccurredAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningEvents_OccurredAt] DEFAULT SYSDATETIMEOFFSET(),
 CONSTRAINT [FK_tbl_SigningEvents_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID])
);

CREATE TABLE [SGN].[tbl_DocumentValidations](
 [DocumentValidationID] uniqueidentifier NOT NULL CONSTRAINT [PK_tbl_DocumentValidations] PRIMARY KEY,
 [DocumentVersionID] bigint NULL,[FileGuid] uniqueidentifier NOT NULL,[Sha256] binary(32) NOT NULL,[Status] varchar(30) NOT NULL,
 [IsSigned] bit NOT NULL,[IsIntegrityValid] bit NOT NULL,[SignatureCount] int NOT NULL,[ResultJson] nvarchar(max) NOT NULL,
 [ValidatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_DocumentValidations_ValidatedAt] DEFAULT SYSDATETIMEOFFSET(),[ValidatedBy] uniqueidentifier NULL
);

CREATE TABLE [SGN].[tbl_SignatureValidations](
 [SignatureValidationID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SignatureValidations] PRIMARY KEY,
 [DocumentValidationID] uniqueidentifier NOT NULL,[SignatureID] bigint NULL,[Status] varchar(30) NOT NULL,[ResultJson] nvarchar(max) NOT NULL,[ValidatedAt] datetimeoffset NOT NULL,
 CONSTRAINT [FK_tbl_SignatureValidations_tbl_DocumentValidations_DocumentValidationID] FOREIGN KEY([DocumentValidationID]) REFERENCES [SGN].[tbl_DocumentValidations]([DocumentValidationID])
);

CREATE TABLE [SGN].[tbl_CertificateValidations](
 [CertificateValidationID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_CertificateValidations] PRIMARY KEY,
 [SignatureValidationID] bigint NOT NULL,[SerialNumber] nvarchar(200) NOT NULL,[Issuer] nvarchar(500) NOT NULL,
 [ValidFrom] datetimeoffset NULL,[ValidUntil] datetimeoffset NULL,[StatusAtSigning] varchar(30) NOT NULL,[CurrentStatus] varchar(30) NOT NULL,
 [OcspStatus] varchar(30) NULL,[CrlStatus] varchar(30) NULL,[ValidatedAt] datetimeoffset NOT NULL
);

CREATE TABLE [SGN].[tbl_Notifications](
 [NotificationID] uniqueidentifier NOT NULL CONSTRAINT [PK_tbl_Notifications] PRIMARY KEY,[SigningProcessID] bigint NOT NULL,
 [SigningParticipantID] bigint NULL,[TemplateCode] varchar(100) NOT NULL,[RecipientEmail] nvarchar(320) NOT NULL,[Status] varchar(20) NOT NULL,
 [IdempotencyKey] uniqueidentifier NOT NULL,[AttemptCount] int NOT NULL,[LastError] nvarchar(2000) NULL,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_Notifications_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),[UpdatedAt] datetimeoffset NULL
);

CREATE TABLE [SGN].[tbl_IntegrationReferences](
 [IntegrationReferenceID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_IntegrationReferences] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[SourceSystem] varchar(100) NOT NULL,[Module] varchar(100) NOT NULL,[EntityType] varchar(100) NOT NULL,
 [EntityID] nvarchar(200) NOT NULL,[ExternalReference] nvarchar(200) NULL,[MetadataJson] nvarchar(max) NULL,[CreatedAt] datetimeoffset NOT NULL
);

CREATE TABLE [SGN].[tbl_IdempotencyRequests](
 [IdempotencyRequestID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_IdempotencyRequests] PRIMARY KEY,
 [IdempotencyKey] uniqueidentifier NOT NULL,[SourceSystem] varchar(100) NOT NULL,[RequestHash] char(64) NOT NULL,
 [ResponseStatusCode] int NULL,[ResponseBody] nvarchar(max) NULL,[SigningProcessID] bigint NULL,[CreatedAt] datetimeoffset NOT NULL,[ExpiresAt] datetimeoffset NOT NULL
);

CREATE TABLE [SGN].[tbl_OutboxMessages](
 [OutboxMessageID] uniqueidentifier NOT NULL CONSTRAINT [PK_tbl_OutboxMessages] PRIMARY KEY,[Type] varchar(200) NOT NULL,
 [Payload] nvarchar(max) NOT NULL,[Status] varchar(20) NOT NULL,[AttemptCount] int NOT NULL,[CreatedAt] datetimeoffset NOT NULL,
 [NextAttemptAt] datetimeoffset NULL,[ProcessedAt] datetimeoffset NULL,[LastError] nvarchar(2000) NULL
);

CREATE TABLE [SGN].[tbl_CallbackSubscriptions](
 [CallbackSubscriptionID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_CallbackSubscriptions] PRIMARY KEY,
 [SigningProcessID] bigint NOT NULL,[Url] nvarchar(2048) NOT NULL,[EventsJson] nvarchar(max) NOT NULL,[SecretReference] nvarchar(200) NOT NULL,
 [IsActive] bit NOT NULL,[CreatedAt] datetimeoffset NOT NULL
);

CREATE TABLE [SGN].[tbl_CallbackDeliveries](
 [CallbackDeliveryID] uniqueidentifier NOT NULL CONSTRAINT [PK_tbl_CallbackDeliveries] PRIMARY KEY,[CallbackSubscriptionID] bigint NOT NULL,
 [EventID] uniqueidentifier NOT NULL,[Payload] nvarchar(max) NOT NULL,[Status] varchar(20) NOT NULL,[AttemptCount] int NOT NULL,
 [NextAttemptAt] datetimeoffset NULL,[DeliveredAt] datetimeoffset NULL,[LastError] nvarchar(2000) NULL,[CreatedAt] datetimeoffset NOT NULL
);

CREATE TABLE [SGN].[tbl_CallbackEndpoints](
 [CallbackEndpointID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_CallbackEndpoints] PRIMARY KEY,
 [ClientId] nvarchar(100) NOT NULL,[Url] nvarchar(2048) NOT NULL,[EventsJson] nvarchar(max) NOT NULL,
 [IsActive] bit NOT NULL CONSTRAINT [DF_tbl_CallbackEndpoints_IsActive] DEFAULT 1,
 [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_CallbackEndpoints_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
 [UpdatedAt] datetimeoffset NULL
);

ALTER TABLE [SGN].[tbl_SigningProcesses] ADD CONSTRAINT [FK_tbl_SigningProcesses_tbl_DocumentVersions_CurrentDocumentVersionID]
 FOREIGN KEY([CurrentDocumentVersionID]) REFERENCES [SGN].[tbl_DocumentVersions]([DocumentVersionID]);
COMMIT;
GO
