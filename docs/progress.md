# Modular Low-Cost Hospital Platform & Transformation Progress

**Repository:** `medical_portal`  
**Current Phase:** Phase 3: Clinical Core Spike [APPROVAL GATE]  
**Last Updated:** 2026-10-24  
**Current Test Suite Baseline:** **288 passed, 0 failed, 0 skipped** (.NET 8)

---

## 📅 Phase Execution Roadmap

| Phase | Description | Status | Approval Gate | Test Results / Findings Addressed |
|:---:|---|:---:|:---:|---|
| **0** | **Baseline & Security Audit Reconciliation** | **Approved** | **GATE 0 (Approved)** | 259 passing tests; verified SEC-01..SEC-22 + SAFE-01; wrote ADR 0001 & Open Questions |
| **1** | **Restructure (`backend/`, `frontend/`, Same-Origin Nginx)** | **Approved** | **GATE 1 (Approved)** | 259 passing tests in `backend/`; same-origin Nginx serving `frontend/` at `/`; Postgres 5432 & Redis 6379 isolated |
| **2** | **Contain Critical Security Findings (SEC-01, 03, 04, 13, 14, 18, SAFE-01)** | **Approved** | **GATE 2 (Approved)** | 275 passing tests; eliminated lazy sessions, token-DB reconciliation, AuthorizationVersion revocation, unified SignalR auth, production fail-fast assertions, HSTS |
| **3** | **Clinical Core Spike (OpenMRS 2.8+ / O3 on PostgreSQL vs Bahmni)** | **Completed** | **GATE 3 (Pending Approval)** | 288 passing tests; selected OpenMRS 2.8+ / O3 on PostgreSQL over Bahmni; proven FHIR R4 JSON REST client for 6 core resources; docker-compose dev stack; ADR 0004 & spike report |
| 4 | Fix Portal Authorization & Payments (SEC-05, 06, 07, 08, 09, 10, 15, 16) | Planned | Gate 4 | Payment idempotency, share scope, server-derived dept scope, HMS RBAC, BOLA/IDOR |
| 5 | Multi-Tenancy Architecture & Isolation | Planned | Gate 5 | Shared portal DB with TenantId & RLS, isolated EMR cores, cross-tenant leak tests |
| 6 | Ethiopia Foundations (Dual Calendar Engine & Localization) | Planned | Gate 6 | Gregorian/Ethiopian conversion engine, `calendar_dim`, Amharic/Afaan Oromoo locales |
| 7 | Frontend Rebuild (TypeScript, React/Vite, Accessible Dialogs) | Planned | Gate 7 | Eliminate stored DOM XSS (SEC-02), ban browser dialogs, strict CSP without unsafe-inline |
| 8 | Clinical Core Integration & EMR Sync | Planned | Gate 8 | Replace HMS adapter with Core FHIR R4 client; retire duplicated clinical tables; remove demo rules |
| 9 | Offline-First Facility Stack & LAN Operations | Planned | Gate 9 | Local LAN server as system of record, outbox sync, 24h network cut chaos tests |
| 10 | National Interoperability (DHIS2, eCHIS, MFR) | Planned | Gate 10 | DHIS2 aggregate push for HMIS, eCHIS referral interface/fake, MFR sync |
| 11 | Data Protection & Hardening (SEC-11, 12, 17, 19, 20, 21, 22) | Planned | Gate 11 | Complete logging scrubber, authenticated field encryption, non-root containers, CI gates |
| 12 | Productization & Tenant Provisioning | Planned | Gate 12 | Tenant configuration schema, module gating at API level, handover docs, compliance notes |
| 13 | Final Proof, Verification & Delivery Checklist | Planned | Gate 13 | Negative tests for all findings, OWASP ZAP baseline, penetration test and delivery checklist |

---

## 🧪 Test Suite Baseline (Phase 0)

Execution Command:
```bash
dotnet test --no-restore --nologo
```

Results:
```text
Passed!  - Failed: 0, Passed: 259, Skipped: 0, Total: 259, Duration: 27 s - MedicalPortal.Tests.dll (net8.0)
```
- **MedicalPortal.Core:** 82.8% line coverage (domain logic, scheduling, billing math, permission matrix)
- **MedicalPortal.Web:** 58.2% line coverage (controllers, filters, middleware, health endpoints)
- **MedicalPortal.Infrastructure:** 54.1% line coverage (excluding auto-generated EF Core migrations)

---

## 🏗️ Phase 1 Restructure Verification

- **Branch:** `phase-1-restructure`
- **Top-Level Split (`git mv`):**
  - `backend/`: `MedicalPortal.sln`, `src/` (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`), `tests/` (`MedicalPortal.Tests`, `load/k6-portal-flow.js`)
  - `frontend/`: `index.html`, `css/app.css`, `js/app.js` (unbundled SPA assets moved out of `backend/src/MedicalPortal.Web/wwwroot/`)
  - Cleaned stale `.obsidian/` files from legacy `portal/` directory.
- **Same-Origin Reverse Proxy (`nginx.conf`):**
  - Serves static assets directly from `/usr/share/nginx/html` at root (`/`) with SPA `try_files` fallback.
  - Reverse-proxies `/api/`, `/hubs/` (with WebSocket upgrade), `/R4/`, `/health`, and `/swagger/` to internal `backend:8080`.
- **Network Isolation (`docker-compose.yml`):**
  - Created internal bridge network `portal-internal`.
  - Removed host port mappings for PostgreSQL (`5432:5432`) and Redis (`6379:6379`); both are accessible only inside `portal-internal`.
  - Removed host port mapping `5000:8080` from `medical-portal` backend; external entrypoint is strictly Nginx `proxy` on port `5000:80`.
- **CI & Build Pipelines:**
  - Updated `.github/workflows/ci.yml` paths for solution restore, build, test, and vulnerability scanning.
  - Provided root and `backend/` Dockerfiles.
- **Automated Test Results:**
  ```text
  Passed!  - Failed: 0, Passed: 259, Skipped: 0, Total: 259, Duration: 28 s - MedicalPortal.Tests.dll (net8.0)
  ```

---

## 🛡️ Phase 2 Critical Security Containment Verification

- **Branch:** `phase-2-contain-critical`
- **Findings Addressed:** SEC-01, SEC-03, SEC-04, SEC-13, SEC-14, SEC-18, SAFE-01
- **Architectural Reference:** [ADR 0003: Critical Security Containment](adr/0003-critical-security-containment.md)
- **Key Changes Implemented:**
  - **SEC-01 (Auth Bypass & Privilege Escalation):**
    - Removed lazy auto-creation of missing sessions in `SessionService.cs`. Unpersisted sessions are immediately rejected.
    - Updated `AuthFilters.cs` to query the authoritative `UserAccount` record, verifying `IsActive == true` and matching `claims.Role`. Claims are reconciled against the database.
    - Added `AuthorizationVersion` to `UserAccount` and JWT tokens; on role modification in `AdminController.cs`, `AuthorizationVersion` is incremented and active sessions are revoked.
  - **SEC-14 (SignalR Session Revocation Bypass):**
    - Updated `PortalHub.cs` to invoke `ISessionService.ValidateSessionAsync`, verifying `AuthorizationVersion`, active status, and database role prior to establishing connection.
  - **SEC-03, SEC-04, SEC-13, SAFE-01 (Production Fail-Fast & Transport Hardening):**
    - Implemented pre-build and post-build fail-fast checks in `Program.cs` rejecting production startup if fake payments (`Stripe:UseFake`), fake HMS (`Hms:UseFake`), missing/placeholder secrets, plain-HTTP external endpoints, hardcoded JWT secrets, or demo clinical knowledge bases (`Clinical:UseDemoKnowledgeBase`) are active.
    - Enforced HSTS (`Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`).
  - **SEC-13 & SEC-18 (Secrets & Cleartext Geolocation):**
    - Removed default webhook fallback secret in `HmsWebhookHandler.cs` and hardcoded credentials in `HmsClient.cs`.
    - Removed plain-HTTP calls to `http://ip-api.com` in `PublicIpService.cs`.
    - Removed static cluster secret keys and `STRIPE_USE_FAKE=true` from `docker-compose.yml`.
- **Negative & Abuse Test Suite:**
  - Added `backend/tests/MedicalPortal.Tests/CriticalSecurityContainmentTests.cs` (16 negative tests covering token forgery, role tampering, session revocation, SignalR hub connection rejection, and production startup fail-fast triggers).
- **Automated Test Results:**
  ```text
  Passed!  - Failed: 0, Passed: 275, Skipped: 0, Total: 275, Duration: 18 s - MedicalPortal.Tests.dll (net8.0)
  ```

---

## 🏥 Phase 3 Clinical Core Spike Verification

- **Branch:** `phase-3-clinical-core-spike`
- **Spike Report:** [`docs/clinical-core-spike.md`](clinical-core-spike.md)
- **Architectural Reference:** [ADR 0004: Clinical Core Selection & FHIR R4 Integration Boundary](adr/0004-clinical-core-selection-and-fhir-boundary.md)
- **Key Deliverables & Findings:**
  - **Platform Selection:** Formally selected **OpenMRS Platform 2.8+ with the O3 microfrontend on PostgreSQL 17** over Bahmni:
    - *Single Engine:* OpenMRS matches portal's PostgreSQL 17 engine, eliminating polyglot database management (MySQL + Postgres).
    - *Low Footprint:* Stack requires **~2.2 GB - 2.5 GB RAM total** (vs 8 - 14 GB for Bahmni), runnable on $300-$500 mini-PCs in Ethiopian district hospitals.
    - *Startup Resilience:* Boots in 60-90 seconds on cold start (vs 8-12 minutes for Bahmni).
    - *Modern UX:* OpenMRS O3 provides offline-capable, responsive tablet charting for bedside rounds.
  - **Dev Stack Definition:** Provided [`docker-compose.clinical-core.yml`](../docker-compose.clinical-core.yml) isolating `clinical-db` (Postgres 17), `clinical-core` (OpenMRS 2.8+ backend), and `clinical-frontend` (O3 SPA) on `portal-internal`.
  - **FHIR R4 JSON Client & Contract Proof:**
    - Defined [`IClinicalCoreFhirClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/IClinicalCoreFhirClient.cs) and production HTTP implementation [`ClinicalCoreFhirClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/ClinicalCoreFhirClient.cs).
    - Implemented [`FakeClinicalCoreFhirClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Fhir/FakeClinicalCoreFhirClient.cs) with realistic Ethiopian synthetic test data.
    - Verified all 6 core FHIR R4 resources: `Patient`, `Encounter`, `Observation` (vital signs panels), `AllergyIntolerance`, `MedicationRequest`, and `Appointment`.
  - **Automated Integration Tests:**
    - Added [`ClinicalCoreFhirIntegrationTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/ClinicalCoreFhirIntegrationTests.cs) (13 tests verifying FHIR 4.0.1 metadata, wire-format headers, Ethiopian patronymic name mapping, panel components, and 404 resilience).
- **Automated Test Results:**
  ```text
  Passed!  - Failed: 0, Passed: 288, Skipped: 0, Total: 288, Duration: 24 s - MedicalPortal.Tests.dll (net8.0)
  ```

---

## 🔍 Security Audit Verification Matrix (SEC-01 .. SEC-22, SAFE-01)

Every audit finding from `docs/security-audit-2026-10-24.md` has been verified against the current codebase:

| Finding ID | Severity | Category | Status in Code | Code Evidence & Current Behavior | Planned Remediation |
|---|:---:|---|:---:|---|---|
| **SEC-01** | **Critical** | Auth Bypass / Privilege Escalation | **CLOSED** | Lazy session creation removed in `SessionService.cs`. Token claims reconciled with authoritative DB in `AuthFilters.cs`. `AuthorizationVersion` increments immediately revoke tokens. Static cluster key removed from `docker-compose.yml`. Covered by negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-02** | **Critical** | Stored DOM XSS | **OPEN** | `app.js:12,417,545,650,2034` assigns user input directly to `.innerHTML` without contextual escaping. `ClinicalSummaryGenerator.cs:135,220` interpolates unencoded strings into HTML. `Program.cs:496` CSP permits `'unsafe-inline'`. | Phase 7 |
| **SEC-03** | **Critical** | Plaintext Production Transport | **CLOSED** | HSTS middleware configured in `Program.cs` (`Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`). Production fail-fast assertions prohibit plain-HTTP endpoints for external integrations. | Closed in Phase 2 |
| **SEC-04** | **Critical** | Silent Fake Payments in Production | **CLOSED** | Removed `STRIPE_USE_FAKE=true` from `docker-compose.yml`. Added pre-build and post-build startup validations in `Program.cs` that abort if `Stripe:UseFake` is true, if `Stripe:SecretKey` is placeholder/empty, or if `FakePaymentGateway` is resolved in Production. Negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-05** | **High** | Payment Intent Replay & Misattribution | **OPEN** | `BillingController.cs:176` accepts existing intents based solely on gateway success without verifying `intent.PatientId == TargetPatientId` or prior consumption. `MedicalPortalDbContext.cs` lacks unique indexes on `GatewayPaymentIntentId` and `IdempotencyKey`. | Phase 4 |
| **SEC-06** | **High** | Unenforced Record-Share Scopes | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:97` persists `ScopeJson`, but `ShareRedemptionController.cs:139` ignores requested categories and unconditionally returns `GetPatientEverythingBundleAsync` exposing full PHI. | Phase 4 |
| **SEC-07** | **High** | Caller-Optional Department Scoping | **OPEN** | `AuthFilters.cs:161` reads `x-department-id` optionally. `AccessControlService.cs:58,84` skips scope checks if header omitted, granting operational staff (`SCHEDULER`, `BILLING`, `NURSE`, etc.) global patient access. | Phase 4 |
| **SEC-08** | **High** | Missing HMS Admin Authorization | **OPEN** | `HmsWebhookController.cs:54` endpoints (`/conflicts`, `/conflicts/{id}/resolve`, `/dead-letters`) require only basic `[RequireAuth]`. Any authenticated patient or staff member can inspect and mutate integration records. | Phase 4 |
| **SEC-09** | **High** | BOLA / IDOR in Refill Processing | **OPEN** | `AppointmentsAndRefillsControllers.cs:468,496` queries refills solely by route ID without asserting `Prescription.PatientId == TargetPatientId`. Clinician authorized for Patient A can approve/deny refills for Patient B. | Phase 4 |
| **SEC-10** | **High** | Weak Proxy Invites & Unbound Acceptance | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:325` generates 32-bit entropy (4 bytes). `AcceptProxyInvite` does not verify that accepting user's email matches `invite.Email`. No dedicated rate limit. | Phase 4 |
| **SEC-11** | **High** | Incomplete PHI Logging Scrubbing | **OPEN** | `LoggingExtensions.cs:8` registers an extra logger provider without wrapping or clearing default providers (`Console`, `Debug`, `EventSource`). `HmsConflictService.cs:113` logs demographic comparison strings directly. | Phase 11 |
| **SEC-12** | **High** | Sensitive Data Plaintext at Rest | **OPEN** | `AesGcmFieldEncryptionService` is registered but not hooked into EF Core `ValueConverter` mappings. `AuthController.cs:262` stores `account.TotpSecret` in plaintext. `ConfigurationKeyProvider.cs:43` derives fallback encryption key from `JWT_SECRET`. | Phase 11 |
| **SEC-13** | **High** | Known Fallback HMS Secrets & Plain HTTP | **CLOSED** | Default fallback secret removed from `HmsWebhookHandler.cs`. Fallback `admin`/`Admin` credentials removed from `HmsClient.cs`. Startup validator in `Program.cs` prohibits fallback secrets and enforces HTTPS. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-14** | **Medium** | SignalR Bypasses Session Revocation | **CLOSED** | `PortalHub.cs` now validates session through `ISessionService.ValidateSessionAsync`, verifying `AuthorizationVersion`, active status, and database role prior to establishing connection. Revoked/unknown sessions rejected. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-15** | **Medium** | Form Writes Require Only Read Permission | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:228` guards form response submission using `Permission.viewRecords` (read permission) instead of a dedicated write permission. | Phase 4 |
| **SEC-16** | **Medium** | Direct Patient/Proxy Refund Triggering | **OPEN** | `BillingController.cs:438` permits callers with `payBills` permission to directly invoke gateway refund operations without a billing staff review or approval workflow. | Phase 4 |
| **SEC-17** | **Medium** | Client IP Bypasses Trusted Proxy Boundary | **OPEN** | `PublicIpService.cs:62` reads `CF-Connecting-IP`, `X-Real-IP`, and `X-Forwarded-For` directly from headers without checking if immediate socket peer is an authorized proxy. | Phase 11 |
| **SEC-18** | **Medium** | Plain HTTP Third-Party Geolocation Call | **CLOSED** | Plain-HTTP calls to `http://ip-api.com` in `PublicIpService.cs` completely eliminated; fallback geolocation returns clean offline/mock metadata. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-19** | **Medium** | Non-Blocking CI Security & Mutable Actions | **OPEN** | `.github/workflows/ci.yml:106,131` uses mutable `@master` tags for Trivy and sets `exit-code: '0'`, preventing high/critical vulnerabilities from failing the CI build. | Phase 11 |
| **SEC-20** | **Medium** | Vulnerable OpenTelemetry Protocol Exporter | **OPEN** | `MedicalPortal.Web.csproj:19` references `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.10.0, which emits `NU1902` for CVE-2026-42191 / GHSA-4625-4j76-fww9. Requires update to 1.15.3+. | Phase 11 |
| **SEC-21** | **Medium** | Unhardened Container Deployment | **OPEN** | `Dockerfile` runs container as `root`. `docker-compose.yml:9,50` publishes PostgreSQL port 5432 and Redis port 6379 directly to host with default credentials. | Phase 11 |
| **SEC-22** | **Medium** | Rate Limiting, Caching & Webhook Limits | **OPEN** | Memory-only rate limits in `Program.cs`. No `Cache-Control: no-store` on PHI API routes. `HmsWebhookController.cs:29` reads unbounded request streams into memory without size limit. | Phase 11 |
| **SAFE-01** | **Safety** | Unconditional Demo Decision-Support Data | **CLOSED** | Pre-build and post-build startup validations in `Program.cs` prevent production boot if `Clinical:UseDemoKnowledgeBase` is true or if demo knowledge base implementations are registered in DI. Negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |

---

## ⚠️ Key Architectural & Operational Risks

1. **Client-Side Cross-Site Scripting (SEC-02):**
   The legacy vanilla JS SPA relies on direct `.innerHTML` interpolation across nearly all screens. Replacing this during Phase 7 with a type-safe component framework (React/TypeScript) is essential before loading real patient data.
2. **Access Control & Scope Verification (SEC-05, SEC-06, SEC-07, SEC-08, SEC-09, SEC-10):**
   Department-scoping enforcement, share bundle scoping, payment idempotency, and BOLA prevention in refills remain open for Phase 4 remediation.
3. **Clinical Scope vs EMR Integration (SAFE-01, Phase 3/8):**
   Clinical entities currently duplicated in the portal database (observations, allergies, prescriptions) must be cleanly decoupled and handed over to the dedicated open-source EMR core (OpenMRS 2.8+ on PostgreSQL) through standardized FHIR R4 interfaces.
4. **National Interoperability Unknowns (Phase 10):**
   Critical Ethiopian health informatics specifications (DHIS2 Ethiopian calendar period encoding, eCHIS referral payloads, Master Facility Registry synchronization) must remain decoupled behind interfaces with fakes until official Ministry guidelines are confirmed.
