using Microsoft.EntityFrameworkCore;
using UtaElectronicSignature.Domain;

namespace UtaElectronicSignature.Infrastructure;

public sealed class SignatureDbContext(DbContextOptions<SignatureDbContext> options) : DbContext(options)
{
    public DbSet<SigningProcess> SigningProcesses=>Set<SigningProcess>();
    public DbSet<SigningParticipant> SigningParticipants=>Set<SigningParticipant>();
    public DbSet<Document> Documents=>Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions=>Set<DocumentVersion>();
    public DbSet<SigningSession> SigningSessions=>Set<SigningSession>();
    public DbSet<SigningEvent> SigningEvents=>Set<SigningEvent>();
    public DbSet<OutboxMessage> OutboxMessages=>Set<OutboxMessage>();
    public DbSet<IdempotencyRequest> IdempotencyRequests=>Set<IdempotencyRequest>();
    public DbSet<IntegrationReference> IntegrationReferences=>Set<IntegrationReference>();
    public DbSet<CallbackSubscription> CallbackSubscriptions=>Set<CallbackSubscription>();
    public DbSet<CallbackEndpoint> CallbackEndpoints=>Set<CallbackEndpoint>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("SGN");
        b.Entity<SigningProcess>(e=>{e.ToTable("tbl_SigningProcesses");e.HasKey(x=>x.SigningProcessID).HasName("PK_tbl_SigningProcesses");e.Property(x=>x.CreatorEmail).HasMaxLength(320);e.Property(x=>x.WorkflowType).HasConversion<string>().HasMaxLength(20);e.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);e.Property(x=>x.RowVersion).IsRowVersion();e.HasIndex(x=>x.ProcessNumber).IsUnique().HasDatabaseName("UX_tbl_SigningProcesses_ProcessNumber");e.Ignore(x=>x.Progress);});
        b.Entity<SigningParticipant>(e=>{e.ToTable("tbl_SigningParticipants");e.HasKey(x=>x.SigningParticipantID);e.Property(x=>x.Status).HasConversion<string>();e.Property(x=>x.RowVersion).IsRowVersion();e.Property(x=>x.ExternalAccessTokenHash).HasMaxLength(64).IsUnicode(false);e.HasOne(x=>x.Process).WithMany(x=>x.Participants).HasForeignKey(x=>x.SigningProcessID);});
        b.Entity<Document>(e=>{e.ToTable("tbl_Documents");e.HasKey(x=>x.DocumentID);e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne(x=>x.Process).WithMany(x=>x.Documents).HasForeignKey(x=>x.SigningProcessID);});
        b.Entity<DocumentVersion>(e=>{e.ToTable("tbl_DocumentVersions");e.HasKey(x=>x.DocumentVersionID);e.Property(x=>x.Sha256).HasColumnType("binary(32)");e.Property(x=>x.PreviousSha256).HasColumnType("binary(32)");e.Property(x=>x.RowVersion).IsRowVersion();e.HasOne(x=>x.Document).WithMany(x=>x.Versions).HasForeignKey(x=>x.DocumentID);});
        b.Entity<SigningSession>(e=>{e.ToTable("tbl_SigningSessions");e.HasKey(x=>x.SigningSessionID);e.Property(x=>x.RowVersion).IsRowVersion();});
        b.Entity<SigningEvent>(e=>{e.ToTable("tbl_SigningEvents");e.HasKey(x=>x.SigningEventID);});
        b.Entity<OutboxMessage>(e=>{e.ToTable("tbl_OutboxMessages");e.HasKey(x=>x.OutboxMessageID);});
        b.Entity<IdempotencyRequest>(e=>{e.ToTable("tbl_IdempotencyRequests");e.HasKey(x=>x.IdempotencyRequestID);e.Property(x=>x.RequestHash).HasMaxLength(64).IsUnicode(false);e.HasIndex(x=>new{x.SourceSystem,x.IdempotencyKey}).IsUnique().HasDatabaseName("UX_tbl_IdempotencyRequests_Source_Key");});
        b.Entity<IntegrationReference>(e=>{e.ToTable("tbl_IntegrationReferences");e.HasKey(x=>x.IntegrationReferenceID);e.HasIndex(x=>new{x.SourceSystem,x.EntityType,x.EntityID}).HasDatabaseName("IX_tbl_IntegrationReferences_Source_Entity");});
        b.Entity<CallbackSubscription>(e=>{e.ToTable("tbl_CallbackSubscriptions");e.HasKey(x=>x.CallbackSubscriptionID);});
        b.Entity<CallbackEndpoint>(e=>{e.ToTable("tbl_CallbackEndpoints");e.HasKey(x=>x.CallbackEndpointID);e.Property(x=>x.ClientId).HasMaxLength(100);e.HasIndex(x=>x.ClientId).HasDatabaseName("UX_tbl_CallbackEndpoints_ClientId_Active").IsUnique().HasFilter("[IsActive]=1");});
    }
}
