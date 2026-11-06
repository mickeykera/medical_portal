# ADR 0017: Laboratory Instrument Interfaces

**Status:** Proposed  
**Date:** 2026-11-05

## Decision

Define an `IInstrumentAdapter` boundary with typed messages for order download,
specimen/accession identifiers, result import, QC messages, acknowledgements,
timeouts and quarantined parse failures. Implement a deterministic fake adapter
first. Support protocol-specific adapters for ASTM, HL7 v2 and CSV only when a
facility device inventory and vendor conformance sample are available. Do not
pretend that a generic parser is validated for clinical use.

Imported results remain pending until reference-range checks, delta checks,
critical-value handling and authorized technologist/supervisor verification
complete. Every import, correction, rejection and release is audited. Adapter
credentials and network endpoints are deployment secrets/configuration, never
seed data.

## Consequences

The laboratory module is testable without instruments and can operate on a LAN.
Each vendor adapter needs contract fixtures, replay/idempotency tests, malformed
message tests, and an operational rollback/runbook before enablement.
