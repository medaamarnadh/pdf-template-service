# Coding conventions

## .NET (src/PdfFill.Api, tests/)
- Minimal API endpoints in `Program.cs` route groups; one file per service in `Services/`; SQL in `Data/`.
- Async all the way; no `.Result` or `.Wait()`.
- Validation collects all problems; never throw for a user error.
- Nullable enabled; warnings are errors in Release (planned).
- Tests: xUnit, one test class per service; names `Method_Condition_Expectation`; no shared mutable state; temp SQLite file per test.
- Logging: structured, with request id; never payload values.

## React (portal/)
- TypeScript strict; components in `src/components`, one per file; hooks for shared logic.
- All HTTP through `src/api.ts`; types in `src/types.ts` mirror the API in snake_case.
- State lives in the owning screen (Editor, TemplateList); pass props down; no global store until needed.
- Accessibility: labelled inputs, visible focus, keyboard-operable canvas actions have button equivalents.
- Styling via tokens in `index.css`; no inline colours.

## Git
- Branch `feat/<nn>-<task>`; squash-merge; commit message = PR title.
- Never commit `poc.db`, `bin/`, `obj/`, `node_modules/`, rendered PDFs other than the samples in `output/`.
