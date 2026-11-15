# ADR-0002: Applications as the tenancy unit; keys allocated to applications; render log stores payloads

- Status: accepted
- Date: 2026-09-19

## Context
Customers run several systems; one backend often serves more than one. Support and audit need to reproduce any
generated document.

## Decision
- **Application** (UUID) is the container for templates, webhooks, delivery targets and logs. A customer creates any number.
- **Template** (UUID) belongs to exactly one application.
- **API key** is created by the customer and allocated to one or more applications (many-to-many). A call must name an
  application the key is allocated to.
- `POST /renders` requires `application_id` and `template_id`.
- Every render call writes a `render_log` row with the full payload, options, outcome and version, before any PDF work.

## Consequences
Payloads are stored: they must be encrypted at rest and expire per application retention. Cross-tenant checks are
key→application allocation plus template→application ownership on every application-scoped call.
