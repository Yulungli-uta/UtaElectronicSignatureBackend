# Final signed document email

Outbox type: `SIGNATURE_FINAL_DOCUMENT_EMAIL`.

HrBackend layout slug: `firma-electronica-final`; configuration key: `EmailTemplates:Layouts:SignatureFinalDocument`.

Subject: `Documento firmado completado - {ProcessNumber}`.

The body is generated from controlled fields (`ProcessNumber`, `Title`, completion date and portal URL), never arbitrary HTML supplied by a caller. The immutable final PDF is attached when it is within the configured institutional limit. Otherwise the message contains a short-lived authenticated link. The idempotency key is `final-document:{ProcessGuid}`.
