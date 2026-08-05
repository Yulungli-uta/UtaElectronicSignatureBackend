CREATE VIEW [SGN].[vw_SigningProcessProgress] AS
SELECT p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Title,p.Status,p.WorkflowType,
 COUNT(CASE WHEN sp.Required=1 THEN 1 END) TotalRequiredSigners,
 COUNT(CASE WHEN sp.Required=1 AND sp.Status='SIGNED' THEN 1 END) SignedRequiredSigners,
 CAST(CASE WHEN COUNT(CASE WHEN sp.Required=1 THEN 1 END)=0 THEN 0
 ELSE COUNT(CASE WHEN sp.Required=1 AND sp.Status='SIGNED' THEN 1 END)*100.0/COUNT(CASE WHEN sp.Required=1 THEN 1 END) END AS decimal(5,2)) ProgressPercentage
FROM [SGN].[tbl_SigningProcesses] p LEFT JOIN [SGN].[tbl_SigningParticipants] sp ON sp.SigningProcessID=p.SigningProcessID
GROUP BY p.SigningProcessID,p.ProcessGuid,p.ProcessNumber,p.Title,p.Status,p.WorkflowType;
GO
CREATE VIEW [SGN].[vw_SigningInbox] AS
SELECT sp.UserID,sp.Identification,sp.SigningParticipantID,sp.Status ParticipantStatus,p.SigningProcessID,p.ProcessNumber,p.Title,p.Status ProcessStatus,p.ExpiresAt
FROM [SGN].[tbl_SigningParticipants] sp INNER JOIN [SGN].[tbl_SigningProcesses] p ON p.SigningProcessID=sp.SigningProcessID;
GO
