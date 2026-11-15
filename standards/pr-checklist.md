# Pull request checklist

One task per PR. The template in `.github/pull_request_template.md` embeds this list; a PR is not reviewed until
the author has ticked every applicable item.

## Before opening
- [ ] Linked to a task: `specs/<feature>/tasks.md` **T<n.m>** (and the story R<n>)
- [ ] Title: `<feature>: <task> (T<n.m>)`
- [ ] Branch from `main`, rebased, no merge commits
- [ ] `dotnet build -c Release` and `dotnet test` pass locally
- [ ] `npm run build` passes if `portal/` changed
- [ ] New or changed behaviour has a test that fails without the change
- [ ] No payload values, secrets or customer identifiers in code, tests, fixtures or logs
- [ ] Migrations added under `Data/migrations/` for any schema change; `schema.sql` updated
- [ ] API change → `docs/design-summary.md`, `README.md`, `requests.http` updated
- [ ] Task box ticked in `tasks.md` in this PR

## Reviewer
- [ ] Tenancy: every new query filters by account/application; every endpoint checks key allocation and template ownership
- [ ] Errors use the problem shape `{ key, code, message, severity }`; codes are in `design.md`
- [ ] No new dependency without an ADR; no new PDF library
- [ ] Security rules (`standards/security.md`) not violated; `security-reviewer` agent run for endpoints, repositories, keys, render pipeline
- [ ] Small enough to review in 20 minutes; otherwise ask to split

## Merge
- [ ] CI green (build, tests, portal build, docker build)
- [ ] At least one approval; security findings marked blocking are fixed
- [ ] Squash-merge with the PR title as the commit message
