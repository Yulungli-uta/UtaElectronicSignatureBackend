using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UtaElectronicSignature.Infrastructure;

namespace UtaElectronicSignature.API;

internal static class InstitutionalProvisioner
{
    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("SignatureDatabase");
            var serviceSecret = configuration["RepositoryUta:ServiceClientSecret"];
            var directoryCode = configuration["HrBackend:SignatureDirectoryCode"];
            var physicalPath = configuration["HrBackend:SignaturePhysicalPath"];
            if (string.IsNullOrWhiteSpace(connectionString)
                || string.IsNullOrWhiteSpace(serviceSecret)
                || string.IsNullOrWhiteSpace(directoryCode)
                || string.IsNullOrWhiteSpace(physicalPath))
            {
                throw new InvalidOperationException(
                    "Faltan secretos o parámetros de aprovisionamiento institucional.");
            }

            var options = new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            await using var db = new SignatureDbContext(options);
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await ExecuteAsync(
                connection,
                transaction,
                """
                IF OBJECT_ID(N'SGN.tbl_SigningProcesses',N'U') IS NULL
                    THROW 51000, 'El esquema SGN no está instalado.', 1;
                IF NOT EXISTS (
                    SELECT 1
                    FROM auth.tbl_Roles
                    WHERE Name=N'R_SIGNATURE_INTEGRATION' AND IsDeleted=0
                )
                    THROW 51000, 'Falta R_SIGNATURE_INTEGRATION en RepositoryUta.', 1;
                """);

            var secretHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(serviceSecret)));
            await ExecuteAsync(
                connection,
                transaction,
                """
                IF EXISTS (SELECT 1 FROM auth.tbl_Applications WHERE ClientId=@ClientId)
                BEGIN
                    UPDATE auth.tbl_Applications
                    SET Name=@Name,
                        ClientSecretHash=@SecretHash,
                        Description=@Description,
                        IsActive=1,
                        IsDeleted=0,
                        ModifiedAt=SYSUTCDATETIME(),
                        ModifiedBy=@Actor,
                        SecretRotatedAt=SYSUTCDATETIME(),
                        SecretRotatedBy=@Actor,
                        SuspendedAt=NULL,
                        SuspendedBy=NULL
                    WHERE ClientId=@ClientId;
                END
                ELSE
                BEGIN
                    INSERT auth.tbl_Applications
                        (Id,Name,ClientId,ClientSecretHash,Description,IsActive,
                         IsDeleted,CreatedAt,CreatedBy,ModifiedAt,ModifiedBy)
                    VALUES
                        (NEWID(),@Name,@ClientId,@SecretHash,@Description,1,
                         0,SYSUTCDATETIME(),@Actor,SYSUTCDATETIME(),@Actor);
                END;
                """,
                ("@ClientId", "uta-signature"),
                ("@Name", "UTA Electronic Signature"),
                ("@SecretHash", secretHash),
                ("@Description", "Backend institucional de firma electrónica"),
                ("@Actor", "UTA-SIGNATURE-DEPLOYMENT"));

            await ExecuteAsync(
                connection,
                transaction,
                """
                IF EXISTS (SELECT 1 FROM HR.TBL_DirectoryParameters WHERE Code=@Code)
                BEGIN
                    UPDATE HR.TBL_DirectoryParameters
                    SET PhysicalPath=@PhysicalPath,
                        RelativePath=@PhysicalPath,
                        Description=@Description,
                        Extension='.pdf',
                        MaxSizeMB=15,
                        Status=1,
                        UpdatedAt=SYSUTCDATETIME()
                    WHERE Code=@Code;
                END
                ELSE
                BEGIN
                    INSERT HR.TBL_DirectoryParameters
                        (Code,PhysicalPath,RelativePath,Description,Extension,MaxSizeMB,Status)
                    VALUES
                        (@Code,@PhysicalPath,@PhysicalPath,@Description,'.pdf',15,1);
                END;
                """,
                ("@Code", directoryCode),
                ("@PhysicalPath", physicalPath),
                ("@Description", "Documentos y versiones de firma electrónica institucional"));

            await transaction.CommitAsync();
            Console.WriteLine(
                "Aprovisionamiento correcto: cliente uta-signature, directorio documental y esquema SGN verificados.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Aprovisionamiento fallido: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync();
    }
}
