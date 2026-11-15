# 05 Portal: applications, keys, render log — Requirements (outline)

Created: 2026-09-19
Owner: medaamarnadh
Due: 2026-10-09
Status: draft

## Intent
Extend the existing React mapper into the customer portal: manage applications and keys, browse the render log,
and keep the mapper as the template editor.

## Requirements (to be expanded by product-owner)
- R1 Applications screen: list, create, delete (blocked when templates exist), test flag.
- R2 API keys screen: create with scopes and application allocation checkboxes, show secret once, rotate, revoke.
- R3 Template list scoped to the selected application; editor unchanged except version switcher and publish.
- R4 Render log screen per application: table newest first, status filter, row detail with payload and problems, download output while retained.
- R5 Admin token entry for the POC (until login exists).

## Open questions
- [ ] Navigation: application selector in the header vs application pages
