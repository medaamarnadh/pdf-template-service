# Design summary (agent-readable)

## Domain model
| Entity | Id | Belongs to | Definition |
|---|---|---|---|
| Account | uuid | — | The customer; billing owner; region; members with roles owner/admin/developer/viewer |
| Application | uuid | Account | Container created by the customer for one of their systems; any number per account; `is_test` flag; `retention_days` |
| Template | uuid | Application | A fillable PDF; exactly one application |
| Template version | uuid | Template | Uploaded PDF + discovered fields + mapping + JSON Schema; draft → published (immutable) → archived |
| API key | uuid | Account | Secret shown once, SHA-256 hash stored; scopes `templates:read`, `templates:write`, `render`; rotate (old valid 24 h), revoke |
| Key allocation | (key, application) | — | Many-to-many; a key acts only on applications it is allocated to |
| Render log | uuid | Application | One row per `POST /renders` call, written before rendering: ids, version, key, request id, full payload, options, status, problems, page count, output ref, duration |
| Webhook / Delivery target | uuid | Application | Phase 2 |
| Audit event, Usage record | — | Account | Change history; hourly roll-up of the render log |

## Invariants
1. Every application-scoped call carries the application id (path or body) and is refused with `403 key_not_allocated` unless the key is allocated to it.
2. A template id must belong to the application named, else `404 template_not_in_application`.
3. `POST /renders` requires `application_id` and `template_id` in the body.
4. Every render call is logged with its payload, including rejected calls; payloads expire per application retention (default 90 days).
5. Published versions never change.
6. Validation collects all problems and returns `{ key, code, message, severity }` with codes: required, type, max_length, format, not_allowed, image_format, image_size, image_decode, table_shape, table_rows, table_column, unknown_key.

## REST surface (`/api/v1`)
| POST | /auth/login · GET /auth/me · POST /auth/change-password | portal JWT (implemented in the POC) |
| Method | Path | Notes |
|---|---|---|
| GET, POST | /applications | account scope |
| GET, PATCH, DELETE | /applications/{app} | 409 application_not_empty |
| GET, POST | /applications/{app}/templates | upload = multipart `file` |
| GET, PATCH, DELETE | /applications/{app}/templates/{tpl} | |
| POST | …/{tpl}/versions | new draft |
| GET | …/versions/{ver} · /file · /schema | |
| PUT | …/versions/{ver}/mapping | draft only, 409 version_published |
| POST | …/versions/{ver}/publish | generates schema |
| POST | …/versions/{ver}/validate | dry run, never billed |
| POST | /renders | body: application_id, template_id, version?, data, options?, metadata? → pdf or 400 problems |
| GET | /renders/{id} · /file | row incl. payload; allocation re-checked |
| GET | /applications/{app}/renders | paged log, newest first |
| GET, POST, PATCH, DELETE | /applications/{app}/webhooks, /delivery-targets | phase 2 |
| GET, POST, DELETE | /keys, /keys/{key}; PUT /keys/{key}/applications; POST /keys/{key}/rotate | portal session only |
| GET | /usage, /events | account scope |

## Mapping contract (per version)
```json
{ "fields": {
  "applicant_name": { "field": ["Applicant_Name","Applicant_Name_P2"], "type": "text", "max_length": 100, "required": true },
  "project_type":   { "field": "Project_Type", "type": "radio", "values": ["New","Expansion"] },
  "sig_applicant":  { "field": "Sig_Applicant", "type": "image", "image_types": ["png","jpeg"], "max_kb": 500, "required": true },
  "budget":         { "type": "table", "rows": 8, "required": true, "columns": { "category": "Budget{n}_Category" } },
  "extra_note":     { "type": "text", "box": { "page": 3, "rect": [36,300,400,340] }, "max_length": 200 },
  "line_items":     { "type": "table", "box": { "page": 3, "rect": [36,120,500,280] }, "columns": ["item","qty","amount"], "row_height": 18, "header": true } } }
```

## Documentation policy
Published reference (`/openapi/public.json`, `/docs`): `POST /renders`, `…/validate`, `GET /renders/{id}` only. Everything else is internal.

## Conventions
UUID v4 ids · snake_case JSON · ISO 8601 UTC · RFC 9457 problem details · `Idempotency-Key` on POST /renders ·
rate limits per key (api 300/min, render 60/min, global render concurrency) with `RateLimit-*`/`Retry-After` headers and `429 rate_limited` · `X-Request-Id` echoed · additive changes stay in /v1, breaking changes go to /v2 with 12 months overlap.
