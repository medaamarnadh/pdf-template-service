# Security rules

These are requirements, not guidance. A PR that breaks one is blocked.

## Authentication and keys
1. API keys: 32 random bytes, prefix `pdfk_live_` / `pdfk_test_`, stored as SHA-256; compared in constant time; shown once.
2. Rotation keeps the old key for 24 h; revocation is immediate; both write an audit event.
3. Portal actions (create/allocate/revoke keys, delete applications) require a portal session, never an API key.
4. Scopes are enforced per endpoint (`templates:read`, `templates:write`, `render`); default deny.

## Portal authentication
4a. Portal users authenticate with email + password; passwords are stored as PBKDF2-SHA256 (≥ 600k iterations) and
    never in plaintext anywhere, including seed files. Tokens are signed JWTs (HS256, key ≥ 32 random chars from
    configuration, never in the repo); login is rate-limited per IP; first-login accounts must change their password.

## Tenancy
5. Every application-scoped endpoint verifies key→application allocation **before** loading any data.
6. Template, version and render ids are always resolved together with the application id; a mismatch is `404`, never `403`
   with a hint that the resource exists elsewhere.
7. Repository methods take the tenant id as a parameter; no method returns rows across accounts.
8. Integration tests must include a cross-tenant read attempt for every new endpoint.

## Payloads and PDFs
9. Payloads are written only to `render_log`; never to application logs, exceptions, traces or test output. Log key names and problem codes only.
10. Payload size ≤ 5 MB, template upload ≤ 20 MB, image ≤ `max_kb`; enforced before parsing.
11. PDFs are parsed with a time limit (10 s) and memory cap; parsing failures are `422`, never a stack trace.
12. Images are decoded server-side before rendering; only PNG and JPEG data URLs are accepted; no URLs fetched.
13. Production: `render_log.payload` encrypted at rest with the account key; outputs in object storage encrypted per account.

## Rate limiting
13a. Every `/api` endpoint is rate-limited per API key (hash of the token) or per client IP; render/validate have a
    stricter window and a global concurrency cap. Limits are configuration (`RateLimiting:*`), rejections are `429`
    problem details with `Retry-After`. Implemented in `Services/RateLimiting.cs`; never disable in production.

## Input and output
14. All JSON is deserialized with a schema or explicit type checks; unknown keys are rejected in strict mode and warned otherwise.
15. Error responses use RFC 9457 problem details; no internal paths, SQL, or library messages in `detail`.
16. Customer-supplied URLs (webhooks, delivery targets) are validated: https only, no private or loopback ranges, DNS re-checked at send time (SSRF).
17. Webhook deliveries are HMAC-signed with a timestamp; receivers are told to reject > 5 minutes skew.

## API documentation
17a. Only render-surface endpoints (tagged `Public`) are published in `/openapi/public.json` and `/docs`. Management
    endpoints (templates, mappings, keys, applications, logs) are never in the public reference; the internal document
    is served only with `Docs:ExposeInternal=true` and never in production. A test guards the public document's contents.

## Secrets and configuration
18. No secrets in the repo, workflows, compose files or the portal bundle; use environment variables and the platform secret store.
19. Delivery-target and webhook secrets are write-only through the API.
20. Admin token for the POC lives in configuration and is replaced by OIDC before any external user.

## Dependencies and pipeline
21. New dependencies need an ADR; PDF libraries are limited to PDFsharp unless an ADR adds a commercial SDK.
22. CI runs `dotnet list package --vulnerable` and `npm audit --audit-level=high`; high or critical findings block the release.
23. Containers run as non-root, with a read-only filesystem except `/data` and `/tmp`.

## Data protection
24. Per-application retention for payloads and outputs; purge is automatic and logged.
25. Account deletion removes applications, templates, keys, logs and stored files within 30 days and is audited.
