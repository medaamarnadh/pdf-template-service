# Delivery policy

## The 20-day rule
A user story is **done within 20 calendar days of its creation** or it is split, descoped or explicitly re-planned.
Done means: acceptance criteria met, tests green, merged to `main`, task boxes ticked, docs updated.

## How it is tracked
- Every feature spec (`specs/<nn>-<feature>/requirements.md`) starts with a header:
  ```
  Created: 2026-09-19
  Owner: <name>
  Due: 2026-10-09        (Created + 20 days)
  Status: draft | in-progress | blocked | done
  ```
- Every story inside it is one `### R<n>` block with acceptance criteria; when a feature has more than ~5 stories
  or would exceed 20 days, split it into another spec folder.
- `.github/workflows/story-sla.yml` runs every Monday, reads every `requirements.md`, and opens or updates a
  GitHub issue titled **"Story SLA report"** listing specs past `Due` or due within 5 days that are not `done`.

## Sizing
| Size | Guideline |
|---|---|
| Story | 1–5 days of work, one feature folder section, ≤ 5 tasks |
| Task | ≤ 1 day, one PR, names its files and its test |
| Feature | ≤ 20 days end to end; otherwise split before starting |

## When a story will miss the date
1. Owner marks `Status: blocked` and writes one line under `## Open questions` saying why.
2. Options, in order: descope (move stories to a new spec), split, or re-plan with a new `Due` and a note in the
   PR or issue. Silent slips are not allowed; the SLA report makes them visible.

## Definition of ready (before Construction starts)
- [ ] Requirements approved by a human (status `in-progress`)
- [ ] `design.md` and `tasks.md` exist and are approved
- [ ] Open questions that block the first task are answered
