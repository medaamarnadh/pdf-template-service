# 04 Webhooks and delivery — Requirements (outline, phase 2)

Created: 2026-09-19
Owner: medaamarnadh
Due: 2026-10-09
Status: draft

## Intent
Long renders and batch jobs run asynchronously; customers are notified by signed webhooks and files are pushed to
their storage.

## Requirements (to be expanded by product-owner)
- R1 Async mode: WHEN `delivery` is present or `?mode=async` THE SYSTEM SHALL return 202 with a render id and process in a worker.
- R2 Webhook endpoints per application with events render.completed, render.failed, template.published; HMAC `X-Signature` with timestamp; retries over 24 h.
- R3 Delivery targets per application: s3, azure_blob, gcs, sftp, webhook; secrets write-only; SSRF protection.
- R4 Job status via `GET /renders/{id}` (queued, rendering, delivering, completed, failed).

## Open questions
- [ ] Queue technology for the POC (in-process channel) vs production (Redis Streams)
