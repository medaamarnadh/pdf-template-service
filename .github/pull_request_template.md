## What
<!-- one or two sentences -->

## Spec
- Feature: `specs/<nn>-<feature>`
- Story: R<n> · Task: T<n.m>

## Checklist (author)
- [ ] Linked task and story above; title `<feature>: <task> (T<n.m>)`
- [ ] `dotnet build -c Release` and `dotnet test` pass; `npm run build` passes if portal changed
- [ ] Test added/updated that fails without this change
- [ ] No payload values, secrets or customer identifiers in code, tests, fixtures or logs
- [ ] Schema change → migration file + `schema.sql`
- [ ] API change → `docs/design-summary.md`, `README.md`, `requests.http`
- [ ] Task box ticked in `tasks.md`

## Checklist (reviewer)
- [ ] Tenancy filters and allocation/ownership checks present
- [ ] Problem shape and codes match `design.md`
- [ ] No new dependency without an ADR
- [ ] Security rules respected; `security-reviewer` run where required

## Notes for reviewer
<!-- risk, rollout, anything non-obvious -->
