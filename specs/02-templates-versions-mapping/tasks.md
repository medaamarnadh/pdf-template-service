# 02 Templates, versions and mapping — Tasks

- [ ] **T2.1** Schema: template_versions; migrate POC templates into version 1 — files: Data/schema.sql, Data/migrations/0002_versions.sql — test: seed loads, sample template = version 1 published
- [ ] **T2.2** `TemplateVersionRepository` (create draft, get, list, update mapping, publish) — files: Data/TemplateVersionRepository.cs — test: unique (template, number)
- [ ] **T2.3** `MappingValidator`: field exists, type agrees, table patterns resolve — files: Services/MappingValidator.cs — test: each code
- [ ] **T2.4** `JsonSchemaGenerator` from FieldMapping — files: Services/JsonSchemaGenerator.cs — test: required, maxLength, enum, maxItems, additionalProperties
- [ ] **T2.5** Endpoints: versions, mapping PUT with 409, publish, schema — files: Program.cs — test: request-level flow
- [ ] **T2.6** New version from new PDF with mapping carry-over and `unmapped[]` — files: Services/TemplateVersionService.cs — test: renamed field reported
- [ ] **T2.7** Portal: version switcher, publish button, read-only editor on published versions, unmapped banner — files: portal/src/components/Editor.tsx, api.ts, types.ts — test: Playwright publish → editor locked
- [ ] **T2.8** Docs: api-design examples from real responses — files: docs/api-design.md
