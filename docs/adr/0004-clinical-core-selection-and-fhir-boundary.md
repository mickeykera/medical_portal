# ADR 0004: Clinical Core Selection & FHIR R4 Integration Boundary

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Architecture (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `docker-compose.clinical-core.yml`)

---

## 1. Context & Business Drivers

The Springfield Health prototype historically coupled patient portal features (billing, messaging, appointments) with duplicated clinical charting tables (`Observations`, `Prescriptions`, `Allergies`, `Conditions`, `Medications`) and demo clinical decision support rules in a single monolithic database.

To achieve the project goal—transforming the software into a low-cost, offline-first hospital platform deployable in Ethiopian hospitals—the clinical core must be decoupled into an open-source, proven electronic medical record (EMR) platform acting as the authoritative clinical system of record.

Two primary candidates were evaluated in the Phase 3 spike ([`docs/clinical-core-spike.md`](../clinical-core-spike.md)):
1. **OpenMRS Platform 2.8+ with the O3 Frontend on PostgreSQL**
2. **Bahmni EMR (Full Distribution)**

---

## 2. Decision

### 2.1 Technology Selection: OpenMRS 2.8+ / O3 on PostgreSQL
We formally select **OpenMRS Platform 2.8+ with the O3 microfrontend on PostgreSQL 17** as the clinical core system of record, rejecting Bahmni for the following reasons:
1. **Database Uniformity (PostgreSQL 17):** OpenMRS 2.8+ runs natively on PostgreSQL. Both the portal tier and the clinical core tier share the same database technology, simplifying facility administration, replication, and disaster recovery to a single PostgreSQL toolchain (`pg_dump`, `pg_basebackup`, WAL archiving). Bahmni requires managing a polyglot MySQL 8 + PostgreSQL stack.
2. **Hardware & Memory Footprint:** OpenMRS 2.8+ / O3 requires **~2.2 GB - 2.5 GB RAM total** (idle: ~1.6 GB), allowing it to run smoothly on low-cost $300-$500 mini-PCs (4-core, 8 GB RAM) in Ethiopian district hospitals. Bahmni requires 8 GB - 14 GB RAM minimum across 7+ containers, which is cost-prohibitive and power-inefficient for facilities operating on solar/battery backup.
3. **Startup Resilience:** OpenMRS cold boots in **~70-90 seconds** on PostgreSQL, whereas Bahmni takes 8-12 minutes with frequent startup race conditions between cascaded containers.
4. **Modern Tablet-Ready UX (O3):** OpenMRS 3.x provides a responsive React/Single-SPA microfrontend architecture optimized for bedside clinical workflows and offline-capable tablet operation.
5. **Licensing:** OpenMRS is licensed under MPL 2.0 with a clear architectural boundary permitting separate proprietary or tenant-specific modules.

### 2.2 Protocol Boundary: HL7 FHIR R4 JSON REST Exclusively
- The portal communicates with the clinical core exclusively via standard **HL7 FHIR R4 JSON REST APIs** (`application/fhir+json`) using [`IClinicalCoreFhirClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/IClinicalCoreFhirClient.cs).
- **Zero Database Access:** The portal application **never** connects directly to the OpenMRS PostgreSQL database. The clinical core database (`openmrs`) is isolated on an internal network (`portal-internal`) and inaccessible to external callers.
- **Authentication:** Service-to-service authentication using dedicated HTTP Basic or OAuth2 Bearer service credentials over internal TLS.

### 2.3 Responsibility Boundaries

| System | Owned Responsibilities |
|---|---|
| **Medical Portal** | Patient and proxy identity, MFA, session management, proxy delegations, record-sharing scopes, secure messaging, patient billing, payment plans and gateways, tenant administration, and audit event logging. |
| **Clinical Core (OpenMRS)** | Patient master index, clinical charting, vital signs, diagnoses, clinical encounters, allergy documentation, medication orders/prescriptions, clinical decision-support rules, lab orders/results, and appointment slots. |

---

## 3. Consequences & Verification

- **Spike Proof:** All 6 required FHIR R4 resources (`Patient`, `Encounter`, `Observation`, `AllergyIntolerance`, `MedicationRequest`, `Appointment`) were implemented and verified with automated tests in [`ClinicalCoreFhirIntegrationTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/ClinicalCoreFhirIntegrationTests.cs).
- **Test Baseline:** 288 tests passing (259 baseline + 16 critical containment + 13 FHIR core integration tests).
- **Migration Roadmap (Phase 8):** In Phase 8, the portal's background sync workers will transition from the legacy HMS adapter to `IClinicalCoreFhirClient`, and duplicated clinical tables in the portal schema will be formally deprecated.
