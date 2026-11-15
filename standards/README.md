# Standards

Rules every contributor and every agent follows. Each file is short and enforceable; where a rule can be
checked by CI or a template, it is.

| File | Covers | Enforced by |
|---|---|---|
| `delivery-policy.md` | Story lifecycle, 20-day completion rule, sizing, escalation | `story-sla.yml` weekly check, specs status table |
| `pr-checklist.md` | What a PR must contain before review and before merge | `.github/pull_request_template.md`, CI |
| `database.md` | Naming, keys, tenancy columns, migrations, indexes, JSON, retention | `data-engineer` agent, code review |
| `security.md` | Auth, tenancy, secrets, payload handling, input limits, dependencies | `security-reviewer` agent, CI checks |
| `coding.md` | .NET and React conventions, testing, logging | agents, review |
