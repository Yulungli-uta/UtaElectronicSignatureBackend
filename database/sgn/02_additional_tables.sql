IF OBJECT_ID(N'[SGN].[tbl_SigningProcessDocuments]',N'U') IS NULL
BEGIN
 CREATE TABLE [SGN].[tbl_SigningProcessDocuments](
  [SigningProcessDocumentID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tbl_SigningProcessDocuments] PRIMARY KEY,
  [SigningProcessID] bigint NOT NULL,[DocumentID] bigint NOT NULL,[DocumentRole] varchar(30) NOT NULL CONSTRAINT [DF_tbl_SigningProcessDocuments_DocumentRole] DEFAULT 'PRIMARY',
  [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_tbl_SigningProcessDocuments_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT [FK_tbl_SigningProcessDocuments_tbl_SigningProcesses_SigningProcessID] FOREIGN KEY([SigningProcessID]) REFERENCES [SGN].[tbl_SigningProcesses]([SigningProcessID]),
  CONSTRAINT [FK_tbl_SigningProcessDocuments_tbl_Documents_DocumentID] FOREIGN KEY([DocumentID]) REFERENCES [SGN].[tbl_Documents]([DocumentID])
 );
 CREATE UNIQUE INDEX [UX_tbl_SigningProcessDocuments_Process_Document] ON [SGN].[tbl_SigningProcessDocuments]([SigningProcessID],[DocumentID]);
END;
GO
