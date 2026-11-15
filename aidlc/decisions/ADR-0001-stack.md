# ADR-0001: React + .NET 10 + SQLite (POC) / PostgreSQL (production)

- Status: accepted
- Date: 2026-09-19

## Context
The POC must run anywhere with zero setup and match the company's existing skills (React, .NET). Production needs
multi-tenant isolation, concurrent writes and object storage.

## Decision
Portal in React 19 + TypeScript (Vite) with pdf.js. API in .NET 10 minimal API with PDFsharp (MIT) behind an
`IPdfEngine`-style boundary so a commercial SDK can be swapped in for PDF/A and digital signatures. SQLite for the
POC; PostgreSQL + object storage for production, with the same repository interfaces.

## Consequences
Dapper repositories must avoid SQLite-only SQL where practical; PDF bytes move from a BLOB column to object storage
in production; the mapping contract and problem codes are frozen as the API surface.
