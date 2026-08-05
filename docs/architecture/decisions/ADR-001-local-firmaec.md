# ADR-001: FirmaEC local coordinated by the official service

The signer uses the official FirmaEC desktop application, while document exchange is coordinated by the authorized centralized or decentralized FirmaEC service. The browser invokes the official `firmaec://` URI returned by that service. The signed document must return automatically through an authenticated callback; signers do not download or upload signed copies.

The deployed Java sources established the decentralized contract used by this integration:

- the backend sends the current PDF to `POST /servicio/documentos` using the system API key;
- the frontend launches `firmaec://{system}/firmar` with the short-lived token and public API URL;
- FirmaEC returns the signed PDF to the registered HTTPS callback using a separate callback API key;
- the backend validates the session, signer, certificate flags, PDF signature and size before storing the immutable version through HrBackend.

The adapter remains disabled until the `UTA-SIGNATURE` system record, both API keys, the `UTA_SIGNATURES` HrBackend directory and an end-to-end desktop signing test are verified. The only manual upload is the original PDF when the request is created.
