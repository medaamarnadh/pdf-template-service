# ADR-0003: Tenancy by owned application, and a body-addressed public API

- Status: accepted
- Date: 2026-09-20
- Supersedes parts of ADR-0002 (tenancy)

## Context
The POC had one flat set of templates, short `tpl_` ids, and render/validate addressed by URL path behind the
portal login. Customers need to group templates and hand their developers a credential that reaches only some of
them, without that credential being able to manage anything.

## Decision
1. A **portal user owns applications**; applications own templates. No `accounts` table exists — ownership is the
   `owner_user_id` column on `applications` and `api_keys`.
2. **API keys** are assigned to applications through `api_key_applications`. A key authenticates only
   `POST /render` and `POST /validate`; management endpoints (`/applications`, `/keys`, `/templates`, ...) reject
   it — `Key_cannot_reach_management_endpoints` in `PublicApiTests`.
3. **Both credentials are authorised**, by different rules: a key must be assigned to the application
   (`key_not_allocated` if not), a portal token must own it (`application_not_owned` if not). Neither is trusted
   to name an application it has no relationship to. `AccessCheck.EvaluateAsync` in `Services/ApiKeyAuth.cs` is
   the single place this is decided.
4. **Ids are UUID v4** for applications, templates and keys, and are shown in the portal because customers need
   them to call the API.
5. The public endpoints are **`POST /api/v1/render` and `POST /api/v1/validate`**, taking `application_id` and
   `template_id` in the body rather than the path, so one URL serves every template.
6. Their JSON is **snake_case**, per CLAUDE.md. Pre-existing template and auth endpoints keep camelCase
   (`pageCount`, `fileName`, `mappedKeys`) until someone converts them; no single response mixes the two.
7. **Both an API key and a portal JWT are accepted** on `/render` and `/validate` (`RequireAuthorization("PublicApi")`
   composes the `ApiKey` and JWT bearer schemes). This exists because the portal's own Test render panel has no
   key to send — it renders with the signed-in user's session. Keys still cannot reach any management endpoint
   (point 2); a JWT reaching render/validate is scoped to applications the user owns, exactly as it already was
   before keys existed.
8. **The key secret comparison is constant-time by construction, not by habit.** `ApiKeyRepository.FindBySecretAsync`
   resolves candidates by the non-secret 12-character `prefix` column (which is deliberately not unique — 4 random
   base62 characters after the fixed `pdfk_live_` prefix), then decides the match with
   `CryptographicOperations.FixedTimeEquals` against every candidate, never short-circuiting on the first hit. An
   earlier version looked candidates up with `WHERE hash = @hash`; because `hash` is `UNIQUE`, SQLite's own
   (non-constant-time, short-circuiting) equality check had already decided the match before any code-level
   comparison ran, making the fixed-time compare dead code protecting nothing. This must not be "simplified" back
   to a hash-equality `WHERE` clause.
9. **Rate limiting runs ahead of authentication**, partitioned on a hash of the presented credential
   (`RateLimiting.PartitionKey` reads `X-Api-Key` or the `Authorization` bearer value directly off the request).
   An earlier version authenticated first so it could partition on the key id; because `AuthorizationMiddleware`
   short-circuits on 401/403 without calling `next()`, that ordering meant a flood of requests with a bogus or
   missing key never reached the limiter, and a valid-but-over-limit key completed a full key lookup and
   last-used write before being rejected. Authenticating first also let an oversized/lying body reach JSON
   parsing before the payload cap fired; the cap, like the limiter, now runs off endpoint metadata ahead of both.

## Consequences
- `POST /templates/{id}/render` and `/templates/{id}/validate` are removed. Nothing external depended on them.
- The demo grant-application data moves to `grant_applications` and `/api/v1/grant-applications/...`.
- Existing templates are migrated into a `Default` application and re-keyed as UUIDs; old `tpl_` ids stop working.
- The API speaks two JSON conventions until the template and auth endpoints are converted. This is recorded as
  accepted debt, not an oversight, and is called out explicitly in CLAUDE.md's Conventions section.
- Error bodies are RFC 9457 problem details (`type`/`title`/`status`/`detail`, `application/problem+json`) on
  authentication and authorisation failures (`Problems.Of` in `Services/ApiKeyAuth.cs`) and on `/keys`
  (`KeyProblem` in `Program.cs`), with one documented exception: `POST /render`'s 400 payload-rejection body keeps
  its existing `{ problems, error_count }` shape because the portal and the validation contract depend on that
  exact shape. `/applications`' validation responses (`name_required`, `application_required`, an invalid mapping
  document) are plain `Results.BadRequest(new { code, error })` — not RFC 9457 shaped, and not served as
  `application/problem+json`. This was not in the brief and is not fixed here (no code changes in this task); it
  is a real, if minor, gap against `standards/security.md` rule 15 that whoever next touches `/applications` should close.
- Key scopes, key rotation with a grace period, and an `accounts` entity grouping users remain unbuilt; per-user
  isolation is enforced, multi-user customers are not yet modelled. Each is a deliberate deviation from
  `standards/security.md`, recorded and reasoned about below rather than silently dropped.
- Render logging (CLAUDE.md's "every render call is logged with its payload" line and working rule 6, which both
  refer to a `render_log` table) was not built in this slice. It is `specs/03-render-and-log`, and the service now
  has `api_key_id` and `application_id` on every render call to log against once that spec lands.

### Accepted deviations from `standards/security.md`
- **Rule 4 (scopes enforced per endpoint, default deny):** not implemented. A key that is assigned to an
  application can render and validate for it; there is no `templates:read` / `templates:write` / `render`
  distinction, and no `insufficient_scope` response exists. Closing this needs a `scopes` column on `api_keys` (or
  a join table, if a key can hold more than one), a claim carrying the granted scopes alongside
  `api_key_application`, and a check next to `AccessCheck` before each handler runs. Deferred because the only
  scope-gated surface today (render, validate) has one scope in practice; the distinction matters once
  scope-worthy management endpoints (e.g. template upload) are opened to keys, which they are not.
- **Rule 2 (rotation keeps the old key valid 24 h; rotation and revocation both write an audit event):** neither
  exists. `DELETE /keys/{id}` revokes immediately (no grace period, nothing to rotate into) and writes nothing
  auditable — `ApiKeyRepository.RevokeAsync` is a plain `UPDATE`. Closing this needs a `previous_hash` /
  `previous_expires_at` pair on `api_keys` so `FindBySecretAsync` can accept either the current or the aging
  hash, a rotate endpoint, and an audit table (or reuse of the deferred `render_log`-style logging work) for both
  rotate and revoke. Deferred because no key has yet needed rotating in practice and revoke-and-reissue is an
  acceptable stand-in for a POC.

## Alternatives considered
- **Application in the URL** (`/applications/{app}/templates/{tpl}/render`): more self-documenting, but every
  caller must build a two-segment path, and the id pair is equally explicit in the body.
- **Key-only public API**: would have broken the portal's own Test render panel, which has no key to send.
- **Keys valid on every endpoint**: rejected — a leaked key would then own the account rather than render access.
