# UtaElectronicSignature

.NET 9 API for institutional electronic-signature processes using RepositoryUta identity, existing HrBackend institutional APIs, SQL Server schema SGN and the local FirmaEC application coordinated by an authorized FirmaEC service.

## Configuration

Set environment variables `ConnectionStrings__SignatureDatabase`, `RepositoryUta__BaseUrl`, `RepositoryUta__Issuer`, `RepositoryUta__Audience`, `RepositoryUta__ServiceClientSecret`, `HrBackend__BaseUrl`, `HrBackend__SignatureDirectoryCode`, `FirmaEc__Enabled`, `FirmaEc__ServiceBaseUrl`, `FirmaEc__PublicApiBaseUrl`, `FirmaEc__SystemCode`, `FirmaEc__ApiKey`, `FirmaEc__CallbackApiKey` and `Cors__AllowedOrigins__0`. Never commit secrets.

The decentralized adapter sends the current PDF to `POST /servicio/documentos`, launches the desktop application through `firmaec://`, and accepts the resulting document only through `POST /api/v1/signature/callbacks/firmaec`. `FirmaEc__Enabled` must stay `false` until the UTA system record, both API keys, HrBackend storage directory and end-to-end test are verified.

Callback destinations require an HTTPS host listed in `Callbacks__AllowedHosts` and a secret supplied through `Callbacks__HmacSecret`. Original PDFs are uploaded through the existing HrBackend document API; signed versions are never manually uploaded by signers.

Implemented API areas include process creation/list/detail/progress, signer inbox, participant maintenance and rejection, cancellation, reminders, audit, document-version metadata/download, integration-reference lookup, validation entry point, health checks and the FirmaEC start operation boundary.

## Run

`dotnet restore`, `dotnet build`, then `dotnet run --project src/UtaElectronicSignature.API`.
