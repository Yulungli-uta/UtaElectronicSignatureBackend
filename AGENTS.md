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

## Estándar de fecha y hora (UTA) — vigente desde 2026-10-26
- **Zona única:** Ecuador (`America/Guayaquil`, UTC-5, sin horario de verano). Se guarda hora de Ecuador; no se guarda UTC en tablas propias.
- **Solo el servidor decide la hora**, nunca el cliente. El cliente envía únicamente fechas elegidas por el usuario (`YYYY-MM-DD`).
- **"Hoy" es el día de Ecuador:** después de las 19:00 el día UTC ya es el siguiente, así que nunca se calcula con UTC.
- **Tipos:** fecha de calendario = `date` / `DateOnly` / `"YYYY-MM-DD"`; instante = `datetime2` con nombre terminado en `...At`.
- **Excepciones (UTC):** solo en el borde con sistemas externos que lo exigen (JWT, tokens de Microsoft, logs técnicos).
- **SQL:** `SYSDATETIME()`. Prohibido `SYSUTCDATETIME()` y `GETUTCDATE()` (también en valores por defecto).
- **Firma electrónica:** las tablas `SGN.*` usan `datetimeoffset` (instantes sin ambigüedad), así que `DateTimeOffset.UtcNow` en código y `SYSDATETIMEOFFSET()` en defaults son válidos. Para el día/año calendario de Ecuador (ej. numeración `FIR-{año}-n`) usar `IAppClock` (`Time:ZoneId`). Lo que el aprovisionador escribe en tablas de otros sistemas (`auth.*`, `HR.*`, datetime2) debe usar `SYSDATETIME()`.
