# 01 Accounts, applications and API keys — Requirements

Created: 2026-09-19
Owner: medaamarnadh
Due: 2026-10-09
Status: in-progress

## Intent
A customer signs up, creates one or more applications (each a UUID), creates API keys and allocates each key to one or
more of their applications. Developers then call the API with a key for the application they target. This is the
tenancy foundation every other feature depends on.

## Actors
- Customer admin (portal session) · Developer (API key) · Service

## Requirements (EARS)
### R1 Applications
- WHEN a customer admin creates an application with a name THE SYSTEM SHALL create it with a UUID v4 id and return the id.
- THE SYSTEM SHALL allow a customer to own any number of applications.
- WHEN an application is deleted THE SYSTEM SHALL refuse if it still has templates, and otherwise remove it and its key allocations.
**Acceptance criteria**
- [x] Given a logged-in admin, when they POST `/applications` with `{name}`, then a 201 with a UUID id is returned and it appears in GET `/applications`. — `ApplicationsTests.Create_returns_a_uuid_and_appears_in_the_list`
- [x] Given an application with one template, when DELETE is called, then 409 `application_not_empty` is returned. — `TemplateScopingTests.Application_holding_templates_cannot_be_deleted`

### R2 API keys
- WHEN a customer admin creates a key THE SYSTEM SHALL return the secret exactly once and store only its hash.
- WHEN a key is created or updated THE SYSTEM SHALL accept a list of application ids owned by the same customer as its assignment.
- IF an assignment names an application the customer does not own THEN THE SYSTEM SHALL refuse the whole request with 400 `application_not_owned`.
- WHEN a key is revoked THE SYSTEM SHALL reject calls with it immediately, with 401 `key_revoked`.
- WHEN a key secret is not recognised THE SYSTEM SHALL return 401 `key_invalid`.
- Key rotation keeping the previous secret valid for 24 hours is **deferred** (see ADR-0003); revoking and
  issuing a new key is the current substitute.
**Acceptance criteria**
- [x] Given two applications A and B, when a key is created assigned to [A, B], then GET `/keys/{id}` lists both and the secret is absent. — `ApiKeyTests.Created_key_lists_its_applications`, `ApiKeyTests.Secret_is_returned_once_and_never_again`
- [x] Given a revoked key, when it is used on any endpoint, then 401 `key_revoked` is returned. — `PublicApiTests.Revoked_key_is_unauthorized`

### R3 Authorisation of API calls
- WHEN a developer calls render or validate THE SYSTEM SHALL check that the key is assigned to the named
  application before doing any work.
- IF the key is not assigned to the application THEN THE SYSTEM SHALL return 403 `key_not_allocated`.
- Key scopes and a 403 `insufficient_scope` response are **deferred** (see ADR-0003): a key that is assigned to an
  application may currently use it for both render and validate, with no finer-grained permission.
**Acceptance criteria**
- [x] Given a key assigned to A only, when it renders a template of B, then 403 `key_not_allocated`. — `PublicApiTests.Key_not_assigned_to_the_application_is_forbidden`
- [ ] Given a key with scope `render` only, when it uploads a template, then 403 `insufficient_scope`. — deferred; no scopes exist (T1.10)

## Out of scope
- SSO, member invitations and roles beyond a single admin (phase 3)
- Per-application members

## Resolved
- **Is there an Account above the user?** No. Applications and keys carry `owner_user_id` and hang off a portal
  user. An Account grouping several users is deferred (see ADR-0003).
- **Should a key with no assignments be usable for account-level reads?** No. A key reaches only render and
  validate, and only for applications it is assigned to; with no assignments it can do nothing.
