# ADR-002: Final document notification

When all required participants have signed, the transaction marks the process completed and writes one `SIGNATURE_FINAL_DOCUMENT_EMAIL` Outbox message addressed to the creator snapshot email. Email delivery is asynchronous and cannot roll back process completion. The institutional email adapter attaches the immutable final PDF when it is within the configured attachment limit; otherwise it sends a short-lived authenticated download link. Delivery is idempotent by process GUID and is audited.
