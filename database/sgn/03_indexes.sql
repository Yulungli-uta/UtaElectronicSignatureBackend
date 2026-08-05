CREATE UNIQUE INDEX [UX_tbl_SigningProcesses_ProcessGuid] ON [SGN].[tbl_SigningProcesses]([ProcessGuid]);
CREATE UNIQUE INDEX [UX_tbl_SigningProcesses_ProcessNumber] ON [SGN].[tbl_SigningProcesses]([ProcessNumber]);
CREATE INDEX [IX_tbl_SigningProcesses_Status_CreatedAt] ON [SGN].[tbl_SigningProcesses]([Status],[CreatedAt] DESC);
CREATE INDEX [IX_tbl_SigningParticipants_UserID_Status] ON [SGN].[tbl_SigningParticipants]([UserID],[Status]);
CREATE INDEX [IX_tbl_SigningParticipants_Identification_Status] ON [SGN].[tbl_SigningParticipants]([Identification],[Status]);
CREATE UNIQUE INDEX [UX_tbl_SigningParticipants_Process_Identification] ON [SGN].[tbl_SigningParticipants]([SigningProcessID],[Identification]);
CREATE UNIQUE INDEX [UX_tbl_DocumentVersions_DocumentID_SequenceNumber] ON [SGN].[tbl_DocumentVersions]([DocumentID],[SequenceNumber]);
CREATE UNIQUE INDEX [UX_tbl_DocumentVersions_FileGuid] ON [SGN].[tbl_DocumentVersions]([FileGuid]);
CREATE INDEX [IX_tbl_SigningSessions_ExpiresAt_Status] ON [SGN].[tbl_SigningSessions]([ExpiresAt],[Status]);
CREATE UNIQUE INDEX [UX_tbl_Notifications_IdempotencyKey] ON [SGN].[tbl_Notifications]([IdempotencyKey]);
CREATE UNIQUE INDEX [UX_tbl_IntegrationReferences_Source_Entity] ON [SGN].[tbl_IntegrationReferences]([SourceSystem],[EntityType],[EntityID]);
CREATE UNIQUE INDEX [UX_tbl_IdempotencyRequests_Source_Key] ON [SGN].[tbl_IdempotencyRequests]([SourceSystem],[IdempotencyKey]);
CREATE INDEX [IX_tbl_OutboxMessages_Status_NextAttemptAt] ON [SGN].[tbl_OutboxMessages]([Status],[NextAttemptAt]);
CREATE UNIQUE INDEX [UX_tbl_CallbackDeliveries_Subscription_Event] ON [SGN].[tbl_CallbackDeliveries]([CallbackSubscriptionID],[EventID]);
CREATE UNIQUE INDEX [UX_tbl_CallbackEndpoints_ClientId_Active] ON [SGN].[tbl_CallbackEndpoints]([ClientId]) WHERE [IsActive]=1;
GO
