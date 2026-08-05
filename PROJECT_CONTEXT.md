# Project context

Independent .NET 9 backend for UTA electronic-signature workflows. Identity, roles, permissions and menu are owned by RepositoryUta. People, employees, institutional email and storage are integrations with HrBackend. Signing uses local FirmaEC; the private key and PIN remain on the user's computer.

The database is SQL Server 2022, database `dbUtaSystem`, schema `SGN`. The approved environment is a test database.
