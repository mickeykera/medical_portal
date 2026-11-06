# ADR 0015: LAN-Safe PDF Generation

**Status:** Proposed  
**Date:** 2026-11-05

## Decision

Use a server-side, vendored or package-managed .NET PDF library behind an
`IPdfRenderer` abstraction. The renderer must accept structured, escaped data,
produce deterministic documents for tests, run without internet/CDN access, and
be replaceable per deployment. Do not render user-provided HTML as trusted
markup and do not call a hosted PDF service.

The first implementation should evaluate QuestPDF (Community license and
deployment terms must be confirmed before commercial distribution) against a
permissively licensed alternative during Phase 3. A final package choice requires
legal review and a proof test for Amharic fonts, Ethiopian/Gregorian dates, ETB,
long tables, accessibility metadata and receipt reprints.

## Consequences

PDF generation stays inside the API, is audited for PHI exports, and remains
usable on an isolated LAN. The dependency and license must be pinned and scanned.
