# PDF Template Service — project context for AI agents

## What this is
A multi-tenant REST service that fills PDF templates from JSON payloads. Customers create **applications**
(UUID), upload **templates** into an application (UUID), map fields to data keys in the React portal, and call
`POST /render` with `application_id` + `template_id` + `data`. **API keys** are allocated to one or more
applications. **Every render call is logged with its payload.**

Design docs (source of truth for scope): `docs/design-summary.md` (condensed) and the linked Claude Docs in `docs/README.md`.
Feature specs (source of truth for work): `specs/<nn>-<feature>/{requirements,design,tasks}.md`.

## Stack (do not change without an ADR)
- Front-end: React 19 + TypeScript (Vite), pdf.js — `portal/`
- Back-end: .NET 10 LTS, ASP.NET Core minimal API, PDFsharp (MIT), Dapper — `src/PdfFill.Api/`
- Database: SQLite for the POC (`src/PdfFill.Api/Data/schema.sql`); PostgreSQL is the production target
- Tests: xUnit — `tests/PdfFill.Api.Tests/`

## Commands
- API: `dotnet run --project src/PdfFill.Api --launch-profile http` (http://localhost:5080)
- Portal: `cd portal && npm run dev` (http://localhost:5173, proxies /api)
- Test: `dotnet test` · Build all: `dotnet build -c Release && (cd portal && npm run build)`

## Standards (binding)
`standards/delivery-policy.md` (20-day story rule) · `standards/pr-checklist.md` · `standards/database.md` ·
`standards/security.md` · `standards/coding.md`. Agents read the ones relevant to their role before acting.

## Working rules (AIDLC)
1. No code without a spec: every change traces to a task in `specs/*/tasks.md`.
2. Requirements use EARS phrasing (WHEN/IF … THE SYSTEM SHALL …) and get acceptance criteria.
3. Design before tasks: data model, endpoints and error codes are decided in `design.md`, not in code.
4. Tests first for validators and repositories; every endpoint has at least one request-level test.
5. Validation problems keep the shape `{ key, code, message, severity }` and the existing codes.
6. Payloads are sensitive: never log values outside the render_log table; never print them in test output.
7. Small PRs, one task each; update `tasks.md` checkboxes in the same PR.
8. Public API docs contain only render/validate (+ job status later): tag an endpoint `Public` only if customers call it to render; everything else stays internal.
9. Decisions that change stack, tenancy or API shape → `aidlc/decisions/ADR-nnnn.md`.

## Conventions
- Ids: UUID v4 strings. JSON: snake_case. Dates: ISO 8601 UTC.
- Endpoints live in `Program.cs` route groups; logic in `Services/`; data access in `Data/`.
- Portal: components in `portal/src/components`, API client in `portal/src/api.ts`, types in `portal/src/types.ts`.
- JSON: snake_case on `/applications`, `/keys`, `/render`, `/validate`. The older template and auth endpoints still
  emit camelCase (`pageCount`, `fileName`, `mappedKeys`); converting them is tracked as debt in ADR-0003.
