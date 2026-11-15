# AIDLC — AI-Driven Development Lifecycle for this project

Three phases, each with a human checkpoint. AI agents draft; a person approves before the next phase starts.

```
Inception ──▶ Construction ──▶ Operations
 intent         spec-driven      release, observe,
 → features     build per        feed back into
 → specs        feature          new specs
```

## Phase 1 — Inception (what and why)
| Step | Owner agent | Output | Checkpoint |
|---|---|---|---|
| Intent | human | one-paragraph goal per feature | — |
| Feature breakdown | `product-owner` | `specs/<nn>-<feature>/requirements.md` (EARS + acceptance criteria) | human approves requirements |
| Domain model | `architect` | entities, invariants, API shape in `design.md` | human approves design |

## Phase 2 — Construction (how)
| Step | Owner agent | Output | Checkpoint |
|---|---|---|---|
| Task plan | `architect` | `tasks.md`: ordered, small, testable tasks with file paths | human approves plan |
| Implement | `backend-engineer`, `frontend-engineer`, `data-engineer` | code + tests, one task per PR | CI green |
| Verify | `qa-engineer` | test report, gaps as new tasks | human merges |
| Review | `security-reviewer` | findings on auth, tenancy, payload handling | blocking findings fixed |

## Phase 3 — Operations (run and learn)
| Step | Owner agent | Output |
|---|---|---|
| Release | `devops-engineer` | tag, container image, release notes, runbook updates |
| Observe | `devops-engineer` | SLO dashboard, incident notes |
| Document | `tech-writer` | API reference and portal guide updated from the code |
| Feed back | `product-owner` | new or amended specs from incidents and customer asks |

## Standards
Binding rules live in `standards/`: delivery policy (20-day story rule, checked weekly by `story-sla.yml`),
PR checklist (embedded in the PR template), database, security and coding conventions.

## Folder layout
```
CLAUDE.md                 project context every agent loads
.claude/agents/*.md       sub-agent definitions (role, scope, rules, tools)
aidlc/README.md           this process
aidlc/templates/          requirements / design / tasks / ADR templates
aidlc/decisions/          ADRs
standards/                delivery policy, PR checklist, database, security, coding rules
.github/pull_request_template.md, workflows/story-sla.yml
specs/<nn>-<feature>/     requirements.md · design.md · tasks.md
docs/                     SaaS design, API design (exported from the design docs)
```

## How to run a feature end to end (Claude Code)
```
> use the product-owner agent to write requirements for specs/03-render-and-log
> use the architect agent to write design.md and tasks.md for specs/03-render-and-log
> use the backend-engineer agent to implement task 3.2 in specs/03-render-and-log/tasks.md
> use the qa-engineer agent to verify specs/03-render-and-log against its acceptance criteria
> use the security-reviewer agent to review the PR
```
Each agent reads `CLAUDE.md`, the feature's spec folder, and only the code it needs.

## Definition of done (per task)
- [ ] Acceptance criteria in `requirements.md` demonstrably met
- [ ] Tests added or updated; `dotnet test` and `npm run build` pass
- [ ] No new payload logging outside `render_log`; no tenancy filter bypassed
- [ ] `tasks.md` checkbox ticked; docs updated if the API changed
