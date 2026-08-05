using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UtaElectronicSignature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSignatureSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "SGN");

            migrationBuilder.CreateTable(
                name: "tbl_OutboxMessages",
                schema: "SGN",
                columns: table => new
                {
                    OutboxMessageID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_OutboxMessages", x => x.OutboxMessageID);
                });

            migrationBuilder.CreateTable(
                name: "tbl_SigningEvents",
                schema: "SGN",
                columns: table => new
                {
                    SigningEventID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SigningProcessID = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorUserID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_SigningEvents", x => x.SigningEventID);
                });

            migrationBuilder.CreateTable(
                name: "tbl_SigningProcesses",
                schema: "SGN",
                columns: table => new
                {
                    SigningProcessID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumRequiredSignatures = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CurrentDocumentVersionID = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_SigningProcesses", x => x.SigningProcessID);
                });

            migrationBuilder.CreateTable(
                name: "tbl_SigningSessions",
                schema: "SGN",
                columns: table => new
                {
                    SigningSessionID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SigningProcessID = table.Column<long>(type: "bigint", nullable: false),
                    SigningParticipantID = table.Column<long>(type: "bigint", nullable: false),
                    BaseDocumentVersionID = table.Column<long>(type: "bigint", nullable: false),
                    FirmaEcTransactionID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OneTimeTokenHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_SigningSessions", x => x.SigningSessionID);
                });

            migrationBuilder.CreateTable(
                name: "tbl_Documents",
                schema: "SGN",
                columns: table => new
                {
                    DocumentID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SigningProcessID = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_Documents", x => x.DocumentID);
                    table.ForeignKey(
                        name: "FK_tbl_Documents_tbl_SigningProcesses_SigningProcessID",
                        column: x => x.SigningProcessID,
                        principalSchema: "SGN",
                        principalTable: "tbl_SigningProcesses",
                        principalColumn: "SigningProcessID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tbl_SigningParticipants",
                schema: "SGN",
                columns: table => new
                {
                    SigningParticipantID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SigningProcessID = table.Column<long>(type: "bigint", nullable: false),
                    UserID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PersonID = table.Column<long>(type: "bigint", nullable: true),
                    EmployeeID = table.Column<long>(type: "bigint", nullable: true),
                    Identification = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DepartmentName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RoleCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    SigningOrder = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_SigningParticipants", x => x.SigningParticipantID);
                    table.ForeignKey(
                        name: "FK_tbl_SigningParticipants_tbl_SigningProcesses_SigningProcessID",
                        column: x => x.SigningProcessID,
                        principalSchema: "SGN",
                        principalTable: "tbl_SigningProcesses",
                        principalColumn: "SigningProcessID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tbl_DocumentVersions",
                schema: "SGN",
                columns: table => new
                {
                    DocumentVersionID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentID = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    PreviousVersionID = table.Column<long>(type: "bigint", nullable: true),
                    PreviousSha256 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    FileGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    SignatureID = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_DocumentVersions", x => x.DocumentVersionID);
                    table.ForeignKey(
                        name: "FK_tbl_DocumentVersions_tbl_Documents_DocumentID",
                        column: x => x.DocumentID,
                        principalSchema: "SGN",
                        principalTable: "tbl_Documents",
                        principalColumn: "DocumentID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tbl_Documents_SigningProcessID",
                schema: "SGN",
                table: "tbl_Documents",
                column: "SigningProcessID");

            migrationBuilder.CreateIndex(
                name: "IX_tbl_DocumentVersions_DocumentID",
                schema: "SGN",
                table: "tbl_DocumentVersions",
                column: "DocumentID");

            migrationBuilder.CreateIndex(
                name: "IX_tbl_SigningParticipants_SigningProcessID",
                schema: "SGN",
                table: "tbl_SigningParticipants",
                column: "SigningProcessID");

            migrationBuilder.CreateIndex(
                name: "UX_tbl_SigningProcesses_ProcessNumber",
                schema: "SGN",
                table: "tbl_SigningProcesses",
                column: "ProcessNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_DocumentVersions",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_OutboxMessages",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_SigningEvents",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_SigningParticipants",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_SigningSessions",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_Documents",
                schema: "SGN");

            migrationBuilder.DropTable(
                name: "tbl_SigningProcesses",
                schema: "SGN");
        }
    }
}
