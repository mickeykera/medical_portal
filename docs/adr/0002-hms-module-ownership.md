# ADR 0002: HMS Module Ownership

**Status:** Proposed for Phase 0 gate  
**Date:** 2026-11-05

## Context

The transformation specification expands the patient portal into a hospital
management system integrated with OpenMRS/O3. The repository already has an
approved FHIR R4 clinical-core boundary. Duplicating OpenMRS clinical ownership
inside native modules would create conflicting charts and unsafe reconciliation.

## Decision

Keep OpenMRS/O3 as the clinical system of record for clinical charting and FHIR
clinical resources. Implement operational HMS workflows natively in this
repository, using stable portal IDs and FHIR references at the boundary.

| Module | Owner | Reason |
|---|---|---|
| Patient identity/MPI linkage | Hybrid: portal identity + OpenMRS identifier adapter | Portal accounts and tenant identity remain local; clinical identifiers are resolved through the approved FHIR boundary. |
| Encounters and clinical observations | OpenMRS/O3 via FHIR | The existing spike and ADR 0009 select OpenMRS/O3 and prohibit a second clinical source of truth. |
| Registration, queue and ADT workflow board | Native .NET operational module | Hospital flow, bed responsibility and local routing are operational concerns not delegated to the clinical chart. |
| Orders, payment release and worklists | Hybrid | Clinical order semantics/results are exchanged through FHIR; payment gates, charge items and departmental worklists are native. |
| Billing/cashier/receivables/tariffs | Native .NET module | Financial controls, ETB tariffs, receipts and tenant policy belong to the HMS. |
| Pharmacy/warehouse/stock/controlled drugs | Native .NET module | Inventory ledger, FEFO, requisition and controlled-drug custody are operational workflows. |
| Emergency/inpatient/ICU/OR/delivery/NICU operational logs | Hybrid | Clinical facts remain in OpenMRS; beds, transfers, checklists, equipment and operational events are native. |
| Laboratory/pathology/imaging/physiology operations | Hybrid | Orders/results and clinical reports cross FHIR; specimen, analyzer, modality, accession and worklists are native behind adapters. |
| Mortuary | Native .NET module | Permissioned chain-of-custody and release workflow is an operational privacy boundary. |
| Kitchen/housekeeping/laundry/linen/stores/assets | Native .NET module | These are facility operations with no need to become clinical-core tables. |
| Reporting/KPIs/DHIS2/eCHIS/MFR | Hybrid | Native projections aggregate local operations; national exchange uses configured adapters and approved contracts. |
| Admin/IT/compliance/audit | Native .NET module | Tenant policy, authorization, audit review and system status are portal responsibilities. |

## Consequences

- New native aggregates are tenant-scoped and communicate through application
  services and transactional outbox events, never cross-module table access.
- Imported clinical data is clearly marked as an external/read-through snapshot.
- FHIR profiles, national specifications and clinical protocols remain
  configuration/interfaces until verified by stakeholders.
- The integration boundary must be tested independently from operational modules.

## Rejected alternatives

- Making the portal database a direct OpenMRS database: violates the approved
  boundary and couples two independent systems.
- Rebuilding clinical charting natively: duplicates clinical ownership and
  increases patient-safety risk.
- Making every module a separate deployable service now: contradicts the
  modular-monolith constraint and adds avoidable LAN operations burden.
