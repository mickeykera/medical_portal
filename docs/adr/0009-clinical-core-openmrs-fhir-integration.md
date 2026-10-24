# ADR 0009: Clinical Core OpenMRS FHIR R4 Integration & Clinical Cache Demolition

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Architecture (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`)

---

## 1. Context & Problem Statement

In the legacy prototype, clinical entities (`Observation`, `Condition`, `Allergy`, `Prescription`) were modeled and stored directly inside the portal's relational database. Clinicians charted encounters inside the portal UI, and simplified hardcoded rule engines evaluated drug-drug interactions and allergy conflicts locally.

Under **Target Architecture Principles B & C**:
1. **Clinical Core System of Record:** OpenMRS Platform 2.8+ with the O3 microfrontend on PostgreSQL 17 is the authoritative clinical system of record.
2. **Strict Protocol Boundary:** The portal communicates with the clinical core strictly via HL7 FHIR R4 JSON REST APIs over a private internal network (`portal-internal`). It never connects directly to the OpenMRS database.
3. **Portal Domain Ownership:** The portal owns patient/proxy authentication, proxy delegation, access consent, record sharing, patient billing, appointment requests, secure messaging, and tenant configuration.
4. **Offline Resilience:** Ethiopian district hospitals experience frequent wide-area network drops and intermittent power. The portal must operate continuously on a facility LAN even when external links are down, serving cached clinical snapshots without throwing errors.

---

## 2. Decisions

### 2.1 Demolition & Repurposing of Portal Clinical Tables as Read-Through Cache
The duplicated clinical tables in the portal schema:
- `Observations`
- `Conditions`
- `Allergies`
- `Prescriptions`

have been marked with `[Obsolete("Clinical data is mastered in OpenMRS via FHIR R4. Portal DB tables are retained strictly as a read-through cache / offline snapshot.")]`.

- The portal database no longer acts as the primary master for clinical charting.
- These tables are retained and repurposed exclusively as a **read-through cache and offline snapshot store** for patient-facing portal views.
- Direct clinician authoring of clinical entries in the portal is prohibited; writes originate from OpenMRS or patient appointment requests.

### 2.2 Patient Identity & Logical Identifier Binding
- Added `FhirPatientId` (indexed `varchar(64)`) to `Patient` in `CoreEntities.cs` and migration `20261024152402_AddFhirPatientIdAndClinicalCoreSync`.
- During inbound synchronization, the portal resolves the OpenMRS FHIR logical ID (`Patient/{id}`) via `IClinicalCoreFhirClient.GetPatientByMrnAsync(mrn)` and caches `FhirPatientId` on the patient record.

### 2.3 `IClinicalCoreSyncService` Implementation
Introduced [`IClinicalCoreSyncService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/IClinicalCoreSyncService.cs) and [`ClinicalCoreSyncService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/ClinicalCoreSyncService.cs):
1. **Inbound Reconciled Pull (`SyncPatientClinicalDataAsync`):**
   - Fetches FHIR R4 resources:
     - `Observation` (vital signs, laboratory panels via LOINC codes)
     - `AllergyIntolerance` (substance, criticality, severity, clinicalStatus)
     - `MedicationRequest` (medication reference/codeable concept, dosage instructions, status)
     - `Encounter` (reason codes mapped to active conditions/diagnoses)
   - Upserts local cache entities matching on unique clinical identifiers/codes.
   - Refreshes the portal's `HealthSnapshot` table.
   - Updates `patient.HmsLastSync = DateTime.UtcNow`.
2. **Outbound Push (`SyncOutboundAppointmentsAsync`):**
   - Scans portal appointments where `!HmsSynced` and status is `CONFIRMED` or `REQUESTED`.
   - Projects them into FHIR R4 `Appointment` resources (`booked` or `proposed`) with participant references to `Patient/{fhirPatientId}` and department codes.
   - Pushes them to OpenMRS via `CreateAppointmentAsync` and marks `HmsSynced = true`.
3. **Stale Patient Batch Sync (`SyncStalePatientsAsync`):**
   - Background worker processes least recently synced patients in batches.
4. **Graceful Offline Degradation:**
   - FHIR network timeouts, offline states, or HTTP 5xx responses from the core are caught, logged, and absorbed cleanly without throwing. Existing cached snapshots in the portal DB are preserved and returned to patients and clinicians.

### 2.4 Clinician Encounter Charting Cutover to OpenMRS O3
- Clinician encounter charting is mastered inside the OpenMRS O3 UI.
- The portal provides the endpoint:
  ```http
  GET /api/clinical/o3-chart-url
  Headers: Authorization: Bearer <clinician_token>, x-patient-id: <patient_uuid>
  ```
  Guarded by `[RequireClinician]` and `[RequirePatientAccess]`.
- Returns `{ "chartUrl": "https://emr.hospital.internal/openmrs/spa/patient/{fhirPatientId}/chart" }`.
- Non-clinicians (patients, proxies, standard users) receive `403 Forbidden`. Unauthorized patient access is rejected with `403 Forbidden`.

### 2.5 Clinical Decision Support (CDS) Rules Decoupling
- In `DrugInteractionLogic.cs` and `AllergyLogic.cs`, the legacy embedded check routines have been annotated with `ClinicalCoreMasteryNotice`.
- Canonical clinical decision support is mastered inside OpenMRS 2.8+ using standard CDS modules and order sets. Portal-side CDS routines serve strictly as defensive client-side validation hints when offline.

---

## 3. Consequences & Verification

- **Backward Compatibility:** All existing portal APIs (`/api/clinical/snapshot`, `/api/clinical/labs`, `/api/clinical/vitals`, `/api/clinical/allergies`, `/api/clinical/medications`, `/api/clinical/conditions`) maintain identical JSON response contracts.
- **Architectural Boundary Intact:** Portal talks to OpenMRS exclusively via FHIR R4 REST (`application/fhir+json`).
- **Offline Reliability:** Verified by automated tests that simulate complete FHIR core client failure; portal gracefully returns cached DB records with zero crashes.
- **Test Verification:**
  - `backend/tests/MedicalPortal.Tests/ClinicalCoreIntegrationAndSyncTests.cs`: 9 tests verifying inbound pull, outbound appointment push, offline resilience, O3 chart URL resolution, stale patient pull, CDS mastery notice, clinician O3 chart access, and access-control negative tests (403).
  - Solution test suite: **342 passed, 0 failed, 0 skipped**.
