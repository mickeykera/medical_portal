# ADR 0011: National Interoperability (DHIS2 HMIS, eCHIS Referrals, Master Facility Registry, and Patient Deduplication)

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Architecture (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`)

---

## 1. Context & Business Drivers

Under Ethiopia's national Digital Health Strategy and the Health Sector Transformation Plan (HSTP-II), public and private hospitals (including facilities like Adot Internal Medicine) must operate as connected nodes within the national health information ecosystem. Crucially, hospitals must interoperate with:

1. **DHIS2 (District Health Information Software 2):** The national Health Management Information System (HMIS) repository utilized by Woreda Health Offices, Regional Health Bureaus, and the Federal Ministry of Health (FMoH) for aggregate disease surveillance, service volume tracking, and population health statistics.
2. **eCHIS (electronic Community Health Information System):** The community-level mobile platform utilized by Health Extension Workers (HEWs) at rural and peri-urban Health Posts to coordinate maternal, child health, and communicable disease referrals to secondary/tertiary facilities.
3. **MFR (Master Facility Registry):** The national single source of truth for health facility identifiers, administrative hierarchy (Region, Zone, Woreda), facility classification (Primary Hospital, General Hospital, Clinic), and operational status.
4. **National Patient Identifier (MPI / Fayya ID):** National digital identity infrastructure and facility-scoped Medical Record Numbers (MRN), requiring robust demographic deduplication and atomic merge semantics to prevent fragmented patient histories across care encounters.

### Architectural Constraints & Working Rules:
- **Target Architecture Principle F (Upstream/Downstream Boundaries):** Never invent external protocols; encapsulate integrations behind formal interfaces backed by configurable test fakes (`FakeDhis2Client`, `FakeEchisClient`, `FakeMfrClient`) ensuring 100% offline development and resilient failover.
- **Facility Data Governance:** Statistical reports pushed upstream must pass a mandatory facility review and approval gate (`FacilityApproved`) before transmission.
- **Offline / Air-Gapped Fallback:** For facilities without stable WAN or experiencing prolonged connectivity cuts, HMIS submissions must support cryptographically signed, tamper-evident offline export bundles (HMAC-SHA256) suitable for flash drive transfer to Woreda offices.
- **Tenant Isolation:** All interoperability datasets, referral transactions, and deduplication candidate queues are strictly scoped by `ITenantScoped` and enforced by multi-tenant query filters and validation hooks.

---

## 2. Architectural Decisions

### 2.1 DHIS2 Aggregate HMIS Integration
- **Entities & Datasets:** Created [`Dhis2ReportDataset`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/NationalInteroperabilityEntities.cs) tracking `DataSetId`, `Period` (e.g. `201902`, `2019Q1`), `OrgUnitCode`, `Status` (`Draft`, `FacilityApproved`, `Submitted`, `Failed`), indicators payload, dry-run validation logs, and submission timestamps.
- **Indicator Extraction Service:** [`Dhis2AggregateService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Interoperability/Dhis2AggregateService.cs) extracts aggregate service indicators directly from local clinical and operational databases:
  - `DE-OPD-TOTAL`: Completed appointments and outpatient encounters in the period.
  - `DE-PATIENT-NEW`: New patient registrations created in the period.
  - `DE-RX-TOTAL`: Total medications/prescriptions dispensed.
  - `DE-LAB-TOTAL`: Total laboratory orders executed.
  - `DE-FORMS-SUBMITTED`: Clinical encounter forms submitted.
- **Two-Phase Approval Workflow:**
  1. *Validation / Dry-Run:* Pushes payload to `IDhis2Client.ValidateDryRunAsync` (`/api/dataValueSets?dryRun=true`), verifying orgUnit authorization and indicator schema compliance without persisting remote state.
  2. *Facility Approval Gate:* A `FacilityAdmin` or `ClinicalDirector` reviews the dry-run summary and executes `ApproveReportAsync`, updating status to `FacilityApproved`.
  3. *Submission:* Only approved reports can be pushed to `SubmitReportAsync`. Submissions without facility approval throw `InvalidOperationException`.
- **Idempotency:** Re-submitting an existing `(DataSet, Period, OrgUnit)` updates the dataset in place and verifies DHIS2 idempotency response headers (`updated: N`).
- **Signed Offline Export Fallback:** Generates a verifiable offline export bundle (`SignedDhis2OfflineBundle`) containing the raw payload, SHA-256 content checksum, HMAC-SHA256 signature using `Dhis2Options.OfflineExportHmacSecret`, and administrative facility metadata.

### 2.2 eCHIS Community Health Referral Integration
- **Feature-Flagging:** Guarded by `Echis:Enabled` configuration. When disabled, API calls return `400 Bad Request` (`"eCHIS integration is disabled for this facility"`), protecting unconfigured facilities from broken external dependencies.
- **Referral Entity:** [`EchisReferral`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/NationalInteroperabilityEntities.cs) manages bi-directional care handoffs:
  - `InboundFromCommunity`: Referrals received from Health Extension Workers at health posts for higher-level outpatient or emergency triage.
  - `OutboundToCommunity`: Counter-referrals discharged back to community health workers with care plans and follow-up directives.
- **Clinician Triage Lifecycle:** Clinicians review incoming referrals via `POST /api/interop/echis/referrals/{id}/triage`, transitioning state through `PendingTriage` $\rightarrow$ `Accepted` | `Rejected` $\rightarrow$ `Completed`.
- **Client Abstraction:** [`IEchisClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Interoperability/IEchisClient.cs) implemented by `FakeEchisClient` and `EchisClient`.

### 2.3 Master Facility Registry (MFR) Directory Sync
- **Facility Entity Enrichment:** Extended `Facility` in [`OrganizationEntities.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/OrganizationEntities.cs) with:
  - `MfrCode`: Official national facility code (e.g. `FAC-ET-AA-042`).
  - `FacilityType`: Primary, General, Tertiary Hospital, or Specialty Clinic.
  - `Region`, `Zone`, `Woreda`: Ethiopian administrative hierarchy strings.
  - `OperationalStatus`: Active, Suspended, or Closed.
  - `MfrLastSyncedAtUtc`: Timestamp of latest directory reconciliation.
- **Sync Service:** [`MfrSyncService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Interoperability/MfrSyncService.cs) fetches official registry details from [`IMfrClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Interoperability/IMfrClient.cs) and reconciles facility naming, operational status, and geographic hierarchy without manual clerical intervention.

### 2.4 Patient Identity, National MPI & Demographic Deduplication
- **National Identifier URIs:** Added to [`Patient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/CoreEntities.cs):
  - `NationalPatientId`: Fayya national ID (e.g. `ET-FAYYA-9928174`).
  - `NationalPatientIdUri`: `http://moh.gov.et/national-id`.
  - `FacilityMrnUri`: `http://hospital.et/mrn/{FacilityCode}`.
- **Demographic Duplicate Detection:** [`PatientDeduplicationService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Interoperability/PatientDeduplicationService.cs) scans for duplicate records using weighted demographic scoring:
  - Exact Fayya National Patient ID Match: Match Score = `1.0`
  - Normalized Phone Number + Exact Date of Birth: Match Score = `0.95`
  - Normalized Phone Number + First & Last Name Match: Match Score = `0.80`
- **Candidate Queue:** Candidates scoring above threshold ($\ge 0.75$) are enqueued as [`PatientDuplicateCandidate`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/NationalInteroperabilityEntities.cs) with status `PendingReview`.
- **Atomic Merge & Audit Trail:**
  - `ResolveDuplicateAsync(candidateId, resolution, targetPatientId, ...)`.
  - On `Merged`: Executes inside a single atomic database transaction. All appointments, encounters, forms, lab orders, prescriptions, and billing records belonging to the source duplicate patient are re-pointed to the target patient.
  - The source duplicate patient is soft-deleted (`IsDeleted = true`, notes updated).
  - An immutable `AuditLog` entry is written logging the merge action, source patient ID, target patient ID, resolving clinician user ID, and justification notes.

### 2.5 Role-Based Authorization
- Controllers enforce strict policy claims:
  - `Dhis2Controller`: Requires `FacilityAdmin` or `ClinicalDirector` for approval and submission.
  - `EchisController`: Requires `Doctor`, `Nurse`, `FacilityAdmin`, or `ClinicalDirector`.
  - `MfrController`: Requires `FacilityAdmin` or `ClinicalDirector`.
  - `MpiController`: Requires `FacilityAdmin`, `ClinicalDirector`, `Doctor`, or `Nurse`.

---

## 3. Consequences & Verification

- **Compliance with FMoH Architecture:** Fulfills national requirements for aggregate HMIS indicator push, community referral triage, and MFR code synchronization.
- **Resilient Offline Operation:** Facilities in low-bandwidth or disconnected environments can validate reports locally and generate signed offline bundles for air-gapped reporting.
- **Test Baseline:** 10 new integration tests in [`NationalInteroperabilityTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/NationalInteroperabilityTests.cs) verifying:
  - Indicator extraction from clinical entities.
  - Dry-run validation and approval gate enforcement.
  - Idempotent re-submission handling.
  - HMAC-SHA256 signed offline export integrity.
  - eCHIS feature flag gating and inbound triage workflow.
  - MFR directory reconciliation.
  - MPI demographic deduplication and atomic merge with audit logging.
  - RBAC policy authorization.
- **Full Solution Verification:** **363 tests passed, 0 failed, 0 skipped**.
