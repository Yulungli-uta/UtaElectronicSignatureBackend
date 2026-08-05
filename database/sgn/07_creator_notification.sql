IF COL_LENGTH(N'SGN.tbl_SigningProcesses',N'CreatorEmail') IS NULL
BEGIN
 ALTER TABLE [SGN].[tbl_SigningProcesses] ADD [CreatorEmail] nvarchar(320) NULL;
 EXEC(N'UPDATE [SGN].[tbl_SigningProcesses] SET [CreatorEmail]=N'''' WHERE [CreatorEmail] IS NULL');
 EXEC(N'ALTER TABLE [SGN].[tbl_SigningProcesses] ALTER COLUMN [CreatorEmail] nvarchar(320) NOT NULL');
END;
GO
