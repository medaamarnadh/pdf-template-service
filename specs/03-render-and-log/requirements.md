# 03 Render and render log — Requirements

Created: 2026-09-19
Owner: medaamarnadh
Due: 2026-10-09
Status: draft

## Intent
A developer calls one endpoint with `application_id`, `template_id` and a payload and receives the finished PDF; every
call is logged with its payload so any document can be reproduced and every rejection explained.

## Actors
- Developer (API key with `render`) · Service · Customer admin (reads the log in the portal)

## Requirements (EARS)
### R1 Render call
- WHEN `POST /renders` is called THE SYSTEM SHALL require `application_id` and `template_id` (UUIDs) in the body.
- IF the key is not allocated to `application_id` THEN THE SYSTEM SHALL return 403 `key_not_allocated`.
- IF `template_id` does not belong to `application_id` THEN THE SYSTEM SHALL return 404 `template_not_in_application`.
- WHEN `version` is omitted THE SYSTEM SHALL use the latest published version (or the current draft if the application is `is_test`).
- WHEN the payload is valid THE SYSTEM SHALL fill the template and return `application/pdf` with `X-Render-Id` and `X-Page-Count`.
**Acceptance criteria**
- [ ] Given the sample template and `sample-payload.json`, when rendered, then 200 and 3 pages.
- [ ] Given a key allocated to another application, then 403 `key_not_allocated` and a log row with status `rejected`.

### R2 Validation
- WHEN a payload is received THE SYSTEM SHALL validate it against the version's mapping before any PDF work and return every problem in one 400 response using `{ key, code, message, severity }` and the existing codes (required, type, max_length, format, not_allowed, image_format, image_size, image_decode, table_shape, table_rows, table_column, unknown_key).
- WHEN `strict` is true THE SYSTEM SHALL treat `unknown_key` as an error, otherwise as a warning.
**Acceptance criteria**
- [ ] Given a payload missing 7 required keys, then 400 lists all 7.

### R3 Render log
- WHEN `POST /renders` is called THE SYSTEM SHALL write one render_log row before rendering with application_id, template_id, version, api_key_id, request_id, the full payload, options and created_at.
- WHEN the call completes THE SYSTEM SHALL update the row with status (completed / rejected / failed), problems, page_count, output reference and duration.
- WHEN `GET /renders/{id}` is called by a key allocated to the row's application THE SYSTEM SHALL return the row including the payload.
- WHEN `GET /applications/{app}/renders` is called THE SYSTEM SHALL page the log newest first with filters status, template_id, from, to.
- WHEN a row is older than the application's retention THE SYSTEM SHALL delete its payload and output reference.
**Acceptance criteria**
- [ ] Given three renders (two ok, one rejected), when the log is listed, then three rows newest first with correct statuses.
- [ ] Given retention of 1 day and a row 2 days old, when the retention job runs, then payload is null and the row remains.

### R4 Options
- WHEN `options.read_only` is true THE SYSTEM SHALL lock all fields; WHEN `options.flatten` is true THE SYSTEM SHALL flatten them.
**Acceptance criteria**
- [ ] Given read_only, then every field has the ReadOnly flag set.

## Out of scope
- Async rendering, delivery targets and webhooks (spec 04); PDF/A; digital signatures

## Open questions
- [ ] Retention default: 90 days (docs) or 30?
- [ ] Should rejected calls count toward usage? (docs say no)
