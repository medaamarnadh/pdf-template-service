# 03 Render and render log — Tasks

- [ ] **T3.1** Schema: render_log + applications.retention_days — files: Data/schema.sql, Data/migrations/0003_render_log.sql — test: insert/update/list
- [ ] **T3.2** `RenderLogRepository` (insert, complete, get, list paged, purge) — files: Data/RenderLogRepository.cs — test: newest first, cursor paging
- [ ] **T3.3** `RenderService` orchestration: log → validate → fill → complete — files: Services/RenderService.cs — test: row exists when validation fails
- [ ] **T3.4** `POST /renders` with body ids, headers, Accept negotiation; remove old route — files: Program.cs — test: 200/400/403/404/409
- [ ] **T3.5** `GET /renders/{id}`, `/file`, `/applications/{app}/renders` — files: Program.cs — test: allocation re-check, filters
- [ ] **T3.6** Retention hosted service — files: Services/RetentionJob.cs — test: purge respects per-application days
- [ ] **T3.7** Move validate endpoint under versions; keep dry-run semantics — files: Program.cs — test: warnings returned
- [ ] **T3.8** Portal: Render log screen (table, status filter, row detail with payload viewer) and test panel switched to `/renders` — files: portal/src/components/RenderLog.tsx, TestPanel.tsx, api.ts — test: Playwright render → appears in log
- [ ] **T3.9** Docs and sample: README, requests.http, sample-payload.json with ids — files: README.md, requests.http, sample-payload.json
