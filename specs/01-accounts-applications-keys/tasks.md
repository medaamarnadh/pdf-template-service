# 01 Accounts, applications and API keys — Tasks

- [x] **T1.1** Schema: applications, api_keys, api_key_applications; `templates.application_id` — files: Data/schema.sql, Data/migrations/0001_tenancy.sql — test: MigrationTests
- [x] **T1.2** `ApiKeyService`: generate/hash — files: Services/ApiKeyService.cs — test: ApiKeyServiceTests. Rotation deferred.
- [x] **T1.3** Auth scheme producing a key principal (key id, applications) — files: Services/ApiKeyAuth.cs, Program.cs — test: PublicApiTests. No 60 s cache: every call reads the key row.
- [x] **T1.4** `/applications` endpoints + `ApplicationsRepository` — files: Program.cs, Data/ApplicationsRepository.cs — test: ApplicationsTests
- [x] **T1.5** `/keys` endpoints incl. assignment PUT — files: Program.cs, Data/ApiKeyRepository.cs — test: ApiKeyTests. Rotate deferred.
- [x] **T1.6** Access check used by render and validate — files: Services/ApiKeyAuth.cs (`AccessCheck`) — test: PublicApiTests
- [x] **T1.7** Public endpoints take the ids in the body: `POST /render`, `POST /validate` — files: Program.cs, Models/PublicApiModels.cs — test: PublicApiTests
- [x] **T1.8** Portal: Applications and API keys screens with assignment checkboxes — files: portal/src/components/{Applications,Keys}.tsx, api.ts, types.ts — verified manually; no Playwright suite in this repo
- [x] **T1.9** Docs: README API table, requests.http — files: README.md, src/PdfFill.Api/requests.http
- [ ] **T1.10** Deferred: key scopes and `insufficient_scope`
- [ ] **T1.11** Deferred: key rotation keeping the previous secret valid 24 h
- [ ] **T1.12** Deferred: an `accounts` entity above the user, with invitations and roles
