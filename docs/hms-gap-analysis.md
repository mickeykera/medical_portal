# HMS/EMR Transformation Phase 0 Gap Analysis

**Date:** 2026-11-05  
**Scope:** Inspection and planning only. No application code or schema was changed.

## 1. Verification of the specification snapshot

| Specification claim | Repository evidence | Finding |
|---|---|---|
| .NET 8 / C# / EF Core / PostgreSQL | `backend/MedicalPortal.sln`, EF Core migrations, Npgsql configuration | Confirmed. |
| PostgreSQL is production-only and SQLite is being removed | Production startup guards require PostgreSQL, but README and test infrastructure still document/use SQLite fallback | Partially true. Keep SQLite for tests until an explicit migration decision; remove only from production paths. |
| OpenMRS/O3 is the clinical core | `docs/clinical-core-spike.md`, FHIR client and sync services, `docs/adr/0009-*` | Confirmed as the selected clinical-core boundary. |
| Existing security controls | `docs/security-audit-2026-10-24.md`, `docs/progress.md`, security test files | Confirmed as implemented claims, but the new HMS modules still need the same controls and proof. |
| Ethiopia foundations | Calendar dimension, localization, ETB formatting and offline assets exist | Confirmed. New HMS strings and clinical safety fields still need `en`, `am`, and `om` coverage. |
| Frontend is a vanilla JS SPA | `frontend/index.html`, `frontend/js/app.js`, `frontend/js/admin-dashboard.js` | Confirmed. There is no React/Vite application to reuse. |
| Existing role set | `Enums/Enums.cs`, authorization tests, seeded accounts | Confirmed for PATIENT/PROXY, CLINICIAN, NURSE, SCHEDULER, BILLING, LAB, PHARMACIST, COMPLIANCE_AUDITOR, DEPT_ADMIN, ADMIN, PLATFORM_ADMIN and INTEGRATION_ADMIN. `REGISTRAR` and the specialized clinical/support roles in section 9 are not yet modeled as distinct roles. |
| Clinical portal tables are obsolete/read-through cache | `[Obsolete]` annotations and `ClinicalCoreSyncService` | Confirmed for current clinical entities. This transformation must not create a second clinical source of truth. |
| No Find Care/nearby functionality remains | `frontend/js/app.js`, care/facility controllers and tests | **False.** Navigation and loaders still reference `care` and `nearby`; `/api/care/*`, `/api/facilities/nearby` and `/api/geo/*` consumers remain. |
| No browser dialogs | XSS/dialog tests exist | No production dialog call was found in the initial targeted search; retain a build-blocking source test for future changes. |
| Role-driven server-authoritative navigation | Existing frontend navigation is assembled locally in `app.js`; admin module gating exists | **Not complete.** A single backend `NavigationPolicy` response and frontend renderer are not yet present. |
| PatientPicker replaces staff patient selectors | Staff-facing `<select>` and a patient selector remain in `app.js` | **Not complete.** This must be addressed with the shared component and API authorization. |
| `.NET ONLINE` and MFA-adoption cards are restricted to IT/Admin | Existing dashboard code and tests need a role-by-role audit | **Unverified in this inspection;** treat as an open Phase 1 verification item. |

## 2. Current architecture and transformation boundary

The repository is a modular monolith with the portal database as its own system of
record. OpenMRS/O3 remains the clinical-core owner for clinical charting and FHIR
resources. The new native HMS work should own operational workflows: ADT/bed
boards, order/payment release, stock and assets, theatre/acute-care operational
records, support services, mortuary workflow, and hospital KPI projections.

All new endpoints must use the existing authentication/session validation, tenant
filters, department scope, permission matrix, audit/hash-chain logging, PHI-read
audit, CSRF/CSP, rate limits, module flags, and step-up controls where required.
No new module may access another module's tables directly; use application
services and transactional outbox events.

## 3. Data model proposal and migration plan

### 3.1 Proposed bounded-context aggregates

1. **Registration/ADT:** `Encounter`, `PatientClass`, `LocationNode`, `Bed`,
   `BedAssignment`, `AdtEvent`, `QueueTicket`, `PatientFlag`.
2. **Orders/results:** `Order`, `OrderStatusTransition`, `OrderResultRoute`,
   `CriticalValueCallback`, `Task`.
3. **Billing gate:** `ServiceCatalogItem`, `Tariff`, `ChargeItem`, `Receipt`,
   `PaymentAllocation`, `Waiver`, `Receivable`, `RefundRequest`, `CashShift`.
4. **Inventory/assets:** `Item`, `Store`, `StockBatch`, `StockLedgerEntry`,
   `Requisition`, `Asset`, `AssetUsageSession`, `MaintenanceRecord`.
5. **Acute/diagnostic operations:** module-owned workflow records referencing
   portal patient/encounter/order IDs; clinical observations and reports remain
   FHIR/OpenMRS-owned or explicitly imported snapshots.
6. **Compliance/reporting:** `KpiDefinition`, `KpiObservation`,
   `BreakGlassReview`, `InvestigationCase`, `AccessRecertification`,
   `AnomalyReviewTask`, `IncidentCase`.

Every aggregate is tenant-scoped. Department- and unit-scoped records carry the
scope required by the owning workflow. Monetary amounts use integer santim and
timestamps use UTC `timestamptz` equivalents. All state changes are append-only
events or audited transitions where the domain requires current-state tables.

### 3.2 Safe migration sequence

1. Add enums/value objects and module-flag identifiers without changing existing
   tables.
2. Add tenant-scoped reference tables (facility/department/unit/location/bed)
   with nullable foreign keys where existing data cannot be backfilled safely.
3. Add ADT and order tables; backfill only synthetic/dev records and record
   production backfill counts before making constraints mandatory.
4. Add catalog/tariff/charge/payment-gate tables; preserve existing billing and
   payment-intent data, mapping each legacy record explicitly.
5. Add stock ledger and asset tables; derive balances from ledger entries and
   prohibit direct balance writes.
6. Add module workflow tables and transactional-outbox event contracts.
7. Add KPI definitions/projections and compliance review tables.
8. Add non-null constraints, unique tenant-scoped indexes, row-level security
   policies, and authorization tests only after the preceding backfills pass.

Each migration must be forward-only in normal deployment, have a tested rollback
or restore procedure, and include tenant-isolation and audit assertions. No table
is dropped or existing clinical data deleted by this plan.

## 4. Gap classification

### Complete or reusable

- Authentication, server-side sessions, MFA/step-up, tenant isolation, audit
  chain, break-glass, CSRF/CSP and PHI-read auditing.
- FHIR R4/OpenMRS adapter and clinical-core sync boundary.
- Ethiopian calendar, ETB formatting, localization catalogs and offline assets.
- Outbox/offline facility primitives, national interoperability abstractions and
  production hardening.

### Missing for the transformation

- Global cleanup CL-1 through CL-9.
- Canonical server-authoritative navigation policy and PatientPicker.
- Patient class, ADT, hierarchical locations and bed board (FND-1..5).
- Generic orders/results and payment-gate state machine (FND-6..12).
- Stock ledger, FEFO, requisitions and controlled-drug controls (FND-13..17).
- Asset registry, tasks/notifications/rosters and permission catalog (FND-18..26).
- Native operational dashboards and workflows from section 6.
- Requirement-specific tests and progress traceability for every new ID.
- The PDF, PACS/DICOMweb and analyzer/instrument adapter decisions recorded in
  ADRs (see the companion ADRs).

### Contradictions or risks to resolve

- The repository progress says Phase 13 is complete, while this specification
  introduces a larger HMS transformation. The progress record must retain the
  historical release-candidate status and add a new transformation track rather
  than rewriting historical claims.
- The specification says SQLite development fallback is being removed, while
  current tests and README still rely on it. Removing it immediately would be a
  broad infrastructure change unrelated to Phase 0.
- The existing `PublicIpService` and geolocation history are security-sensitive;
  CL-1 removal must delete only functionality used exclusively by Find Care and
  nearby facilities, with tests proving no other security behavior regresses.
- The selected OpenMRS/O3 boundary and the proposed native operational modules
  must remain explicit; otherwise duplicate clinical/order data will be created.

## 5. Sequenced backlog and estimates

Estimates are engineering days for one experienced engineer, excluding external
clinical/regulatory approval and hardware procurement.

| Sequence | Work | Estimate | Dependencies |
|---:|---|---:|---|
| 0 | Phase 0 artifacts, ownership ADRs, open questions | 2 | None |
| 1 | CL-1..CL-9 cleanup and navigation policy | 5 | 0 |
| 2 | Foundations: ADT, locations, beds, orders, billing gate, stock/asset/task primitives | 20 | 1 |
| 3 | Dashboard rebuilds, role seeds, scoped worklists | 15 | 1–2 |
| 4 | Pharmacy, warehouse, controlled drugs, APTS-style analytics | 12 | 2–3 |
| 5 | Emergency, ICU, theatre, delivery suite, NICU transfers | 22 | 2–4 |
| 6 | Laboratory, imaging/physiology/PACS boundary, pathology | 18 | 2–5 |
| 7 | Mortuary and support services | 10 | 2, 5 |
| 8 | KPI engine, hospital dashboards, DHIS2/FHIR mappings | 12 | 2–7 |
| 9 | Authorization/isolation/accessibility/load/security/E2E proof and docs | 15 | 1–8 |

These are planning estimates, not commitments. Each phase ends at the specified
STOP gate and requires fresh test evidence.

## 6. Traceability policy

Each requirement ID will have:

1. an implementation reference,
2. a focused unit/integration/authorization test,
3. a row in `docs/progress.md`, and
4. a demonstrated command result at the phase gate.

Phase 0 has no implementation requirement IDs to close. It closes the planning
deliverables only; CL/FND/role/module IDs remain open until their phases.

## 7. Questions requiring approval

1. Approve the ownership split in `docs/adr/0002-hms-module-ownership.md`?
2. Should the existing SQLite fallback remain test-only while production remains
   PostgreSQL-only, or should it be removed from development and tests too?
3. Which hospital modules are enabled for the first tenant rollout?
4. Are the proposed native `Order` and `Encounter` operational projections
   acceptable alongside OpenMRS clinical resources?
5. Which PDF generation policy is acceptable for a LAN deployment?
6. Which PACS/DICOMweb and analyzer protocols are available at the first facility?
7. What are the approved tariff, formulary, APTS, KPI and safety sources?
8. Which specialized roles should be distinct permissions versus role-plus-unit
   capabilities?
