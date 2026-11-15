# 02 Templates, versions and mapping — Requirements

Created: 2026-09-19
Owner: medaamarnadh
Due: 2026-10-09
Status: draft

## Intent
A developer uploads a fillable PDF into an application, the service discovers its fields, the customer maps fields to
data keys (with required, limits, formats, tables and drawn boxes) and publishes an immutable version that renders
can rely on.

## Actors
- Customer admin (portal) · Developer (API key with templates:write / templates:read)

## Requirements (EARS)
### R1 Upload and discovery
- WHEN a PDF is uploaded to `/applications/{app}/templates` THE SYSTEM SHALL create a template with a UUID id, belonging to that application, and draft version 1.
- WHEN a version is created THE SYSTEM SHALL discover every AcroForm field with id, type, page, rectangle, tooltip, export values and max length.
- IF the file is not a readable PDF or exceeds 20 MB THEN THE SYSTEM SHALL refuse with 400 / 413.
**Acceptance criteria**
- [ ] Given the sample grant PDF, when uploaded, then 121 fields are returned including radio options `New`, `Expansion` for `Project_Type`.

### R2 Mapping
- WHEN a mapping is saved on a draft THE SYSTEM SHALL validate it (referenced fields exist, table patterns resolve for every row, types agree with the PDF) and store it.
- IF a mapping is saved on a published version THEN THE SYSTEM SHALL refuse with 409 `version_published`.
- THE SYSTEM SHALL support entries of type text, checkbox, radio, choice, image, table (template rows) and drawn boxes (text, checkbox, image, table) with `required`, `max_length`, `format`, `values`, `image_types`, `max_kb`, `rows`, `columns`, `box`, `row_height`, `header`.
**Acceptance criteria**
- [ ] Given a mapping referencing a field that does not exist, when saved, then 400 with a problem `mapping_field_missing` naming it.

### R3 Versions and publish
- WHEN a draft is published THE SYSTEM SHALL freeze the PDF and mapping, generate a JSON Schema for the payload, and mark the version published.
- WHEN a new version is created from a new PDF THE SYSTEM SHALL copy mapping entries whose field ids still exist and report the rest as unmapped.
- THE SYSTEM SHALL never modify a published version.
**Acceptance criteria**
- [ ] Given a published version, when PUT mapping is called, then 409 and the mapping is unchanged.
- [ ] Given version 2 uploaded with one field renamed, then the response lists that field's old key under `unmapped`.

## Out of scope
- Template design tools; conditional logic; PDF/A output

## Open questions
- [ ] Do draft versions render in live applications, or only in applications flagged `is_test`?
