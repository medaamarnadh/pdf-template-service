# PDF Template Service (POC)

[![CI](https://github.com/medaamarnadh/pdf-template-service/actions/workflows/ci.yml/badge.svg)](https://github.com/medaamarnadh/pdf-template-service/actions/workflows/ci.yml)

A three-tier REST application for generating PDFs from templates. App teams upload a fillable PDF,
map its fields to data keys in the **Template Mapper** portal, then call one endpoint with a JSON payload
and get the finished PDF back — text, checkboxes, radios, tables and signature images — with the payload
validated against the mapping before anything is rendered.

| Tier | Technology |
|---|---|
| Front-end | React 19 + TypeScript (Vite), pdf.js for page rendering and field overlays |
| Back-end | .NET 10 (LTS) ASP.NET Core minimal API, PDFsharp (MIT) fill engine, Dapper |
| Database | SQLite — templates (PDF bytes + mapping JSON) and the POC business data |

```
├── src/PdfFill.Api/            REST API, validator, fill engine, SQLite schema/seed, sample template
├── tests/PdfFill.Api.Tests/    xUnit tests (payload validation rules)
├── portal/                     React portal (Template Mapper)
├── tools/                      build_template.py (stand-in for Acrobat), sample signatures
├── output/                     rendered samples
├── Dockerfile, docker-compose.yml     one image: API + built portal
└── .github/workflows/          ci.yml (build + test), release.yml (tag -> zips + container image)
```

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (LTS)
- Node.js 22+ (portal)
- Docker (optional)
- Python 3.10+ with `reportlab pillow pypdf` (optional — only to regenerate the sample template)

## Run locally (two terminals)

```bash
# 1) API — creates and seeds the SQLite DB on first start, loads the sample template into it
dotnet run --project src/PdfFill.Api --launch-profile http        # http://localhost:5080

# 2) Portal — Vite dev server, proxies /api to the API
cd portal && npm install && npm run dev                            # http://localhost:5173
```
First-run order: sign in to the portal (seeded user below), **create an application**, **upload a template into
it** and map its fields, **create an API key** assigned to that application, then render — either with **Test
render** in the portal (uses your portal session) or by calling `POST /api/v1/render` with the key.

Open the portal, pick **grant application** (pre-mapped, already seeded into the first application) or drop your
own fillable PDF, map keys, save, then use **Test render** to validate and render through the API. Published API
reference: `http://localhost:5080/docs` (render and validate only); full internal document at
`/openapi/internal.json` in Development.
`src/PdfFill.Api/requests.http` has ready-made requests; `sample-payload.json` is a valid render payload.

## REST API (`/api/v1`)

| Method | Path | Purpose |
|---|---|---|
| GET / POST | `/applications` | list / create a tenant application (UUID) |
| PUT / DELETE | `/applications/{id}` | rename / delete (409 while it holds templates) |
| GET / POST | `/keys` | list keys / create one (secret returned once) |
| PUT | `/keys/{id}/applications` | replace the applications a key may use |
| DELETE | `/keys/{id}` | revoke |
| GET | `/templates?applicationId=` | templates in one application |
| POST | `/templates` | upload a PDF (multipart: `file`, `applicationId`, optional `name`) |
| GET | `/templates/{id}` | metadata, discovered fields, saved mapping |
| GET | `/templates/{id}/file` | the PDF bytes |
| PUT | `/templates/{id}/mapping` | save the mapping document |
| DELETE | `/templates/{id}` | delete |
| POST | `/render` and `/validate` | public, API key - body `{application_id, template_id, options, data}` |
| GET / POST | `/grant-applications/{id}` and `/grant-applications/{id}/render` | POC business data through the same engine |

Templates and auth responses are camelCase JSON (an older convention, kept as-is); applications, keys, render and
validate are snake_case. See Authentication and Payload validation below for the render/validate error shape.

## Build and test

```bash
dotnet restore && dotnet build -c Release && dotnet test -c Release
cd portal && npm ci && npm run build          # -> portal/dist
```
For a single deployable, copy `portal/dist` to `src/PdfFill.Api/wwwroot` (the Dockerfile does this): the API
serves the portal from the same origin, so no CORS configuration is needed in production.

## Configuration

| Setting (appsettings / env var) | Default | Meaning |
|---|---|---|
| `Pdf:TemplatesDir` / `Pdf__TemplatesDir` | `Templates` | sample templates seeded into the DB when it is empty |
| `Pdf:FontFile` | `Fonts/DejaVuSans.ttf` | font for field appearances (bundled so Linux and Windows match) |
| `Database:File` | `Data/poc.db` | SQLite file |
| `Database:RecreateOnStartup` | `true` | drop and reseed on start (POC only — set `false` to keep uploads) |
| `Cors:Origins` | `http://localhost:5173` | allowed portal origins in development |
| `Auth:JwtKey` / `Auth__JwtKey` | — (required) | HMAC key for portal tokens, ≥ 32 random chars |
| `Auth:TokenHours` | `12` | token lifetime |
| `RateLimiting:AuthPerMinute` | `10` | login attempts per IP |
| `Docs:ExposeInternal` | `false` (`true` in Development) | serve the internal OpenAPI document with portal/management endpoints |
| `RateLimiting:Enabled` | `true` | turn limiting off for load tests |
| `RateLimiting:ApiPerMinute` | `300` | sliding window per API key (or client IP when no key) on every `/api` endpoint |
| `RateLimiting:RenderPerMinute` | `60` | tighter window on render and validate endpoints |
| `RateLimiting:RenderConcurrency` / `RenderQueue` | `8` / `16` | global cap on simultaneous renders; excess waits in a short queue |

Environment variables use `__` in place of `:`.

## Release

Tag `main` to release. `release.yml` runs the tests, publishes self-contained builds for `linux-x64` and
`win-x64`, pushes a container image (API + portal) to GitHub Container Registry, and creates a GitHub Release.
```bash
git tag -a v0.1.0 -m "First POC release" && git push origin v0.1.0
```

## Deploy

**Oracle Cloud Always Free (recommended free hosting)** — see `docs/runbooks/deploy-oracle.md`: Arm VM, Docker Compose
with Caddy TLS from `deploy/oracle/`, SQLite persisted on the VM disk. The release image is built for `linux/amd64` and `linux/arm64`.

**Docker** — one container serves both the API and the portal on port 8080:
```bash
docker compose up --build                     # http://localhost:8080
docker run -p 8080:8080 -v $(pwd)/data:/data ghcr.io/medaamarnadh/pdf-template-service:latest
```
**Self-contained zip** — unzip, build the portal into `wwwroot`, run `PdfFill.Api` with `ASPNETCORE_URLS=http://+:8080`.
**Azure App Service / Container Apps** — deploy the image, set `WEBSITES_PORT=8080`, mount persistent storage at `/data`.

**Production checklist** (not done in the POC): PostgreSQL instead of SQLite, object storage for PDFs, background
queue for rendering, audit log and retention policy, template versioning, key rotation with a 24 h overlap.

## Regenerating the sample template
```bash
pip install reportlab pillow pypdf && python tools/build_template.py
```

## Payload validation (`POST /api/v1/render`, `POST /api/v1/validate`)

Callers send `application_id`, `template_id` and `data` as JSON; the engine validates `data` against the
template's mapping **before** touching the PDF and returns one complete `400` body listing every problem, or the
PDF on success. `validate` never renders and is never billed.

```bash
# dry run - same rules, no PDF (warnings included)
curl -X POST "localhost:5080/api/v1/validate" -H "Authorization: Bearer $API_KEY" -H "Content-Type: application/json" \
  -d '{"application_id":"'"$APPLICATION_ID"'","template_id":"'"$TEMPLATE_ID"'","data":{"business_name":"Acme Manufacturing"}}'
# render (options.strict also rejects keys that are not in the mapping; options.read_only locks every field)
curl -X POST "localhost:5080/api/v1/render" -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"application_id":"'"$APPLICATION_ID"'","template_id":"'"$TEMPLATE_ID"'","options":{"strict":true,"read_only":false},"data":{"business_name":"Acme Manufacturing","project_type":"New"}}' \
  -o out.pdf
```

| code | when |
|---|---|
| `required` | a field or table marked `"required": true` is missing, null or empty |
| `type` | wrong JSON type for the field kind (checkbox must be bool-like, text must be string/number) |
| `max_length` | text longer than the field's `maxLength` |
| `format` | `date:*` not an ISO date, `number:*`/`currency:*` not numeric |
| `not_allowed` | radio/dropdown value not in the template's export values |
| `image_format` / `image_size` / `image_decode` | not a png/jpeg data URL, over `maxKb`, or bytes that will not decode |
| `table_shape` / `table_rows` / `table_column` | not an array of objects, more rows than the template, unknown column |
| `unknown_key` | key not in the mapping (`error` when `options.strict` is true, otherwise `warning`) |

Example rejection from `render` (`application/problem+json`, `400`):
```json
{ "title": "Payload rejected", "status": 400, "application_id": "3f9e...", "template_id": "9c1a...", "error_count": 2,
  "problems": [ { "key": "business_name", "code": "required", "message": "Value is required.", "severity": "error" },
                { "key": "project_type", "code": "not_allowed", "message": "\"Maybe\" is not one of: New, Expansion.", "severity": "error" } ] }
```
A successful `validate` call returns `200` with `{ application_id, template_id, valid, problems }` (`problems` may
still list warnings even when `valid` is `true`).

Other render/validate error responses (all RFC 9457 problem details):

| status | code | when |
|---|---|---|
| `401` | `unauthorized` | no API key or portal token presented |
| `401` | `key_invalid` | the presented API key does not match any stored key |
| `401` | `key_revoked` | the key exists but has been revoked |
| `403` | `key_not_allocated` | the key is valid but is not assigned to `application_id` |
| `403` | `application_not_owned` | a portal token was used and the caller's user does not own `application_id` |
| `404` | `application_not_found` | `application_id` does not exist |
| `404` | `template_not_found` | `template_id` does not exist in that application |
| `413` | `payload_too_large` | request body exceeds the 5 MB cap (checked from `Content-Length` before parsing) |
| `429` | `rate_limited` | too many requests for this key/IP; see Rate limiting below |

Required-ness is a mapping decision: mark any field or table `"required": true` in the mapping JSON
(the Template Mapper UI has a checkbox for it). Nothing in the engine is hard-coded.

## Authentication

Two credentials. Portal users sign in with email and password (`POST /api/v1/auth/login`) and receive a JWT; every
management endpoint requires it. Customers' developers use an API key (`pdfk_live_...`, sent as `Authorization: Bearer`
or `X-Api-Key`) which reaches only `POST /render` and `POST /validate`, and only for the applications that key is
assigned to. Keys are created in the portal, shown exactly once, and stored as SHA-256 hashes.

A portal JWT is also accepted on `/render` and `/validate` (useful for the portal's own "Test render" feature), but
it is authorized against applications the signed-in user owns, not against a key's assignments.

- First user is seeded from `Data/seed-users.sql`: **demo@example.com**, role `owner`, flagged
  `must_change_password` — the portal forces a new password (≥ 12 characters) at first sign-in. Passwords are stored as
  PBKDF2-SHA256 hashes (600k iterations); the seed file never contains plaintext.
- Add a user: `dotnet run --project src/PdfFill.Api -- hash-password "<password>"` and insert the hash into `users`.
- `Auth:JwtKey` (env `Auth__JwtKey`) must be a random string ≥ 32 characters; Development has a dev-only key,
  production refuses to start without one.
- Login attempts are rate-limited per IP (`RateLimiting:AuthPerMinute`, default 10).
- `Demo:Enabled` (env `Demo__Enabled`) runs the instance as a public demo: the seeded account keeps its
  `must_change_password` flag cleared and `auth/change-password` answers `403 demo_disabled`, so one shared
  login keeps working for every visitor instead of the first one locking out the rest. Leave it `false`
  anywhere real. It does not restrict anything else, so a demo instance should also reset its data - see
  the deploy runbook.
- Portal auth errors: `401 unauthorized` (no/invalid token), `401 invalid_credentials`, `400 weak_password`.
- API key errors on render/validate: `401 key_invalid`, `401 key_revoked`, `403 key_not_allocated` — see the
  table above.

## API documentation policy

Only the endpoints customers call to render are published: `POST …/render` and `POST …/validate` (later: job status).
They carry the `Public` tag in `Program.cs`; a transformer in `Services/ApiDocs.cs` strips everything else from
`/openapi/public.json`, which backs the `/docs` reference page. Template, mapping, key and application endpoints are
portal-only and appear only in `/openapi/internal.json`, which production does not serve. `ApiDocsTests` fails if a
non-public endpoint ever appears in the public document.

## Rate limiting

Built-in ASP.NET Core limiter, partitioned per API key (a hash of the bearer token, never the secret) or per client IP
when no key is sent. Every `/api` endpoint is under the `api` window; render and validate endpoints add the stricter
`render` window and a global concurrency cap. Rejections are `429` with a problem body
(`code: rate_limited`, `retry_after`) and a `Retry-After` header; successful responses carry
`RateLimit-Limit` and `RateLimit-Policy`. Limits are configuration, see the table above.

## The three moving parts

**1. Template** (`Templates/<name>.pdf`) - static layout with uniquely named fields and tooltips.
Signature placeholders are text fields named `Sig_*` that the filler replaces with an image.

**2. Mapping** (`Templates/<name>.mapping.json`) - the typed contract the Template Mapper exports.
Key = data key, value = PDF field(s) plus validation rules. The simple `{ "key": "FieldId" }` form still loads.
```json
{ "fields": {
    "applicant_name": { "field": ["Applicant_Name", "Applicant_Name_P2", "Applicant_Name_P3"], "type": "text", "maxLength": 100, "required": true },
    "project_type":   { "field": "Project_Type", "type": "radio", "values": ["New", "Expansion"] },
    "sig_applicant":  { "field": "Sig_Applicant", "type": "image", "imageTypes": ["png", "jpeg"], "maxKb": 500, "required": true },
    "budget":         { "type": "table", "rows": 8, "required": true,
                        "columns": { "category": "Budget{n}_Category", "requested": "Budget{n}_Requested" } } } }
```
The PDF's own field type still decides *how* a value is written; the mapping decides *whether* it is accepted.

**3. Data** (`ApplicationRepository`) - one flat dictionary whose keys equal the mapping keys:
- header columns come straight from the tables or are aliased in SQL
  (`ap.business_name AS applicant_name`, `ap.entity_type = 'LLC' AS entity_llc`,
  `CASE ap.minority_owned WHEN 1 THEN 'Yes' ELSE 'No' END AS minority_owned`)
- grid rows become `<prefix><row_no>_<column>` (`rev_fy1_q1`, `budget3_requested`, `source2_secured`)
- derived values (annual totals, growth %, match %) are computed in the repository
- signatures become `sig_<role>` (PNG bytes), `sig_<role>_name`, `sig_<role>_date`
- rows missing from the DB are emitted as `""` so grid cells render blank

## How the filler decides what to do

| PDF field type | Data value | Action |
|---|---|---|
| text | string / number / date | set `/V` (ISO dates -> `MM/dd/yyyy`) |
| text | `byte[]` | draw PNG inside the field rectangle, hide the field |
| checkbox | bool / 0-1 / "Yes" | check / uncheck |
| radio group | "Yes", "Expansion", "Approve" ... | select the kid whose export value matches |

The JSON report lists `missingInPdf` / `missingInData`, so template drift is caught on every run;
`POST /api/v1/validate` does the same check without generating.

## Production notes
- Replace the `output/` write in `PdfGenerationService` with your destination (blob, SFTP, API) and move
  the call into a Hangfire/Quartz job; the API then only enqueues.
- The template is authored in Acrobat in production; `tools/build_template.py` exists only so the POC is
  self-contained (`pip install reportlab pillow pypdf`). Keep the naming convention
  (`Section_Field`, radio export values = data codes).
- PDFsharp is MIT. If complex templates hit appearance-stream edge cases, `PdfFormFiller` is the only class
  to swap for iText / Syncfusion / Aspose.
- Section 508: tooltips are on every field; tagging and tab order are done in the template.
