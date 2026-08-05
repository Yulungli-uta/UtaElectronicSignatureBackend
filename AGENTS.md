# UTA Electronic Signature

- Preserve Clean Architecture dependencies: Domain has no infrastructure references.
- Database objects belong to schema `SGN`; tables use `tbl_`, views `vw_`, indexes `IX_`/`UX_`, and named constraints.
- Never store certificate passwords, PINs, private keys, personal JWTs, or FirmaEC API keys in source or logs.
- RepositoryUta is the identity authority. Do not create users, passwords, roles, menus, or institutional tokens here.
- HrBackend is consumed through authenticated APIs; never query its internal tables.
- Every signed document version is immutable and chained by previous version and SHA-256.
- A stale signing result must return `409 DOCUMENT_VERSION_CHANGED`; never merge independently signed PDFs.
- Frontend files follow the existing HrFrontend layout under pages, components, hooks, types, and lib/api/services.
- Update `IMPLEMENTATION_STATUS.md` and relevant ADRs after material changes.
