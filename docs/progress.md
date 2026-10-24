# Modular Low-Cost Hospital Platform & Transformation Progress

**Repository:** `medical_portal`  
**Current Phase:** Phase 11: Data Protection & Hardening [APPROVAL GATE 11]  
**Last Updated:** 2026-10-24  
**Current Test Suite Baseline:** **372 passed, 0 failed, 0 skipped** (.NET 8)

---

## 📅 Phase Execution Roadmap

| Phase | Description | Status | Approval Gate | Test Results / Findings Addressed |
|:---:|---|:---:|:---:|---|
| **0** | **Baseline & Security Audit Reconciliation** | **Approved** | **GATE 0 (Approved)** | 259 passing tests; verified SEC-01..SEC-22 + SAFE-01; wrote ADR 0001 & Open Questions |
| **1** | **Restructure (`backend/`, `frontend/`, Same-Origin Nginx)** | **Approved** | **GATE 1 (Approved)** | 259 passing tests in `backend/`; same-origin Nginx serving `frontend/` at `/`; Postgres 5432 & Redis 6379 isolated |
| **2** | **Contain Critical Security Findings (SEC-01, 03, 04, 13, 14, 18, SAFE-01)** | **Approved** | **GATE 2 (Approved)** | 275 passing tests; eliminated lazy sessions, token-DB reconciliation, AuthorizationVersion revocation, unified SignalR auth, production fail-fast assertions, HSTS |
| **3** | **Clinical Core Spike (OpenMRS 2.8+ / O3 on PostgreSQL vs Bahmni)** | **Approved** | **GATE 3 (Approved)** | 288 passing tests; selected OpenMRS 2.8+ / O3 on PostgreSQL over Bahmni; proven FHIR R4 JSON REST client for 6 core resources; docker-compose dev stack; ADR 0004 & spike report |
| **4** | **Fix Portal Authorization & Payments (SEC-05, 06, 07, 08, 09, 10, 15, 16)** | **Approved** | **GATE 4 (Approved)** | 300 passing tests; ConsumedPaymentIntents, FHIR share scope filtering, mandatory dept scope enforcement, HMS RBAC, refill BOLA/IDOR fix, 128-bit email-bound proxy invites, form write permission, segregated billing refunds; ADR 0005 |
| **5** | **Multi-Tenancy Architecture & Isolation** | **Approved** | **GATE 5 (Approved)** | 311 passing tests; Tenant model, ITenantScoped on 48 tables, EF global filters, SaveChanges isolation validation, Postgres RLS, HKDF keys, PLATFORM_ADMIN zero PHI, API module gating, WCAG AA, Adot provisioning plan & compliance notes; ADR 0006 |
| **6** | **Ethiopia Foundations (Dual Calendar Engine & Localization)** | **Approved** | **GATE 6 (Approved)** | 328 passing tests; Pure Ethiopian<->Gregorian engine (73,414 days verified), `calendar_dim` SQL table, DHIS2 period formats, En/Am/Om localization catalogs, local vendored offline assets, zero browser dialogs; ADR 0007 |
| **7** | **Frontend Rebuild & XSS Containment (SEC-02)** | **Approved** | **GATE 7 (Approved)** | 333 passing tests; Stored DOM XSS eliminated, escapeHtml encoder, textContent DOM assignment, strict CSP without unsafe-inline or remote CDNs, ClinicalSummaryGenerator encoding, print script; ADR 0008 |
| **8** | **Clinical Core Integration & EMR Sync** | **Approved** | **GATE 8 (Approved)** | 342 passing tests; OpenMRS FHIR R4 client integration (`IClinicalCoreSyncService`), deprecation of duplicated clinical tables as read-through cache/offline snapshot, clinician O3 charting cutover & deep linking, decoupled CDS notice; ADR 0009 |
| **9** | **Offline-First Facility Stack & LAN Operations** | **Approved** | **GATE 9 (Approved)** | 353 passing tests; Transactional outbox in PostgreSQL (`ITenantScoped`), per-patient strict FIFO ordering, exponential backoff with jitter, HMAC-SHA256 signed reference packages with anti-downgrade, 24h network cut chaos tests, facility backup script; ADR 0010 |
| **10** | **National Interoperability (DHIS2, eCHIS, MFR)** | **Approved** | **GATE 10 (Approved)** | 363 passing tests; DHIS2 aggregate HMIS extraction, dry-run validation & facility approval gate, signed offline JSON export, eCHIS community referrals with clinician triage, MFR reconciliation, MPI Fayya ID & demographic deduplication with atomic merge; ADR 0011 |
| **11** | **Data Protection & Hardening (SEC-11, 12, 17, 19, 20, 21, 22)** | **Completed** | **GATE 11 (Pending Approval)** | 372 passing tests; Cleared default logger providers with national/regional regex scrubbing (SEC-11); transparent authenticated field encryption for MfaSecret/PatientNotes/conflicts with fail-fast production key separation (SEC-12); trusted proxy IP boundary enforcement (SEC-17); immutable CI actions with blocking exit codes (SEC-19); OpenTelemetry upgraded to 1.15.3 resolving NU1902 (SEC-20); non-root USER app and no-new-privileges containers (SEC-21); no-store PHI cache control, 1MB bounded webhook streams, and MFA challenge rate limiting (SEC-22); ADR 0012 |
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

## 🔒 Phase 4 Portal Authorization & Payments Verification

- **Branch:** `phase-4-fix-authorization-payments`
- **Findings Addressed:** SEC-05, SEC-06, SEC-07, SEC-08, SEC-09, SEC-10, SEC-15, SEC-16
- **Architectural Reference:** [ADR 0005: Portal Authorization Hardening and Payment Integrity](adr/0005-portal-authorization-and-payment-integrity.md)
- **Key Changes Implemented:**
  - **SEC-05 (Payment Intent Replay & Misattribution):**
    - Added `ConsumedPaymentIntent` entity with unique index on `GatewayPaymentIntentId` and composite index on `(PatientId, IdempotencyKey)`.
    - Added intent `PatientId` check in `BillingController.cs` before allocation; verified against `TargetPatientId`.
    - Atomic recording in `ConsumedPaymentIntents`; duplicate execution rejected with `409 Conflict`.
  - **SEC-06 (Record-Share Scope Enforcement):**
    - Verified requested categories in `ShareRedemptionController.cs` are a strict subset of `ScopeJson`.
    - Implemented `GetScopedPatientBundleAsync` in `FhirService.cs` filtering FHIR bundle resources (`Allergies`, `Conditions`, `Medications`, `Observations`, `Appointments`).
  - **SEC-07 (Mandatory Operational Staff Scoping):**
    - Enforced membership checks in `AccessControlService.cs`: operational staff without assignments rejected with `unassigned_staff_scope` (`403 Forbidden`).
    - Unambiguous department membership auto-derived; multi-department staff without `x-department-id` rejected with `department_scope_required`; mismatched department rejected with `department_scope_mismatch`.
  - **SEC-08 (HMS Admin RBAC):**
    - Protected `/api/integrations/hms/conflicts` and `/api/integrations/hms/dead-letters` with `[RequireAuth]` and `[RequireAdmin]`. Read access restricted to `ADMIN`, `SYSTEM_ADMIN`, `COMPLIANCE_AUDITOR`; mutating operations strictly restricted to `ADMIN` and `SYSTEM_ADMIN`.
  - **SEC-09 (BOLA/IDOR in Refills):**
    - Added `r.Prescription.PatientId == TargetPatientId` verification in `ApproveRefill` and `DenyRefill`. Cross-patient refill mutations rejected with `404 Not Found`.
  - **SEC-10 (128-Bit Cryptographic Proxy Invites & Recipient Binding):**
    - Upgraded invite token entropy to 16 bytes (128 bits; 32 hex chars prefixed with `INV-`).
    - Enforced recipient account email binding (`actorPerson.Email == invite.Email`) and prohibited self-acceptance in `AcceptProxyInvite`.
  - **SEC-15 (Dedicated Form Submission Authorization):**
    - Added `Permission.submitForms` and `CanSubmitForms` flag on `ProxyGrant` and `ProxyInvite`.
    - Granted write matrix entry `(UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Write)`.
    - Guarded `POST /api/forms/{id}/submit` with `[RequirePatientAccess(Permission.submitForms, AppResource.ClinicalResults, AppAction.Write)]`.
  - **SEC-16 (Segregated Refund Authorization):**
    - Restricted `POST /api/billing/refunds` direct gateway execution strictly to billing staff (`BILLING`, `ADMIN`, `SYSTEM_ADMIN`) with mandatory refund audit reason. Blocked patient/proxy self-refunds with `403 Forbidden`.
- **Negative & Abuse Security Test Suite:**
  - Added [`AuthorizationAndPaymentsSecurityTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/AuthorizationAndPaymentsSecurityTests.cs) (14 tests covering replay attacks, cross-patient misattribution, share scope leakage, unassigned staff scopes, multi-department headers, HMS role gating, refill BOLA, invite entropy & email mismatch, read-only proxy form submission, and self-refund attempts).
- **Automated Test Results:**
  ```text
  Passed!  - Failed: 0, Passed: 300, Skipped: 0, Total: 300, Duration: 27 s - MedicalPortal.Tests.dll (net8.0)
  ```

---

## 🏛️ Phase 5 Multi-Tenancy Architecture & Isolation Verification

- **Branch:** `phase-5-multi-tenancy-architecture`
- **Tenant Modeling & Settings Engine:**
  - Added [`Tenant`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/TenantEntities.cs) with unique index on `Slug`, configuration properties (`Region`, `TimeZone`, `LanguagesJson`, `SettingsJson`), master key fingerprint, and genesis audit block hash.
  - Implemented `TenantSettings` model with strongly-typed `Branding`, `Modules`, `Hosting`, `Integrations`, and `Security` sub-models without code forks.
  - Configured `ITenantScoped` with default `TenantConstants.DefaultTenantId` across all 48 tenant-owned entities.
- **EF Core Global Query Filters & Isolation Enforcement:**
  - Injected `ITenantContext` into [`MedicalPortalDbContext.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/MedicalPortalDbContext.cs).
  - Applied query filters `e => !IsTenantResolved || e.TenantId == CurrentTenantId` across all `ITenantScoped` entities.
  - Implemented `EnforceTenantIsolation()` in `SaveChanges()` and `SaveChangesAsync()` (auto-stamps tenant ID, blocks cross-tenant entity creation with `SecurityException`, blocks tenant reassignment with `SecurityException`).
- **PostgreSQL Row-Level Security (RLS) Helper:**
  - Implemented [`PostgresRlsHelper.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/PostgresRlsHelper.cs) generating RLS policies (`<Table>_tenant_isolation`) and enforcing RLS across all 48 tenant-scoped tables.
- **Cryptographic Isolation:**
  - Implemented [`TenantKeyProvider.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Tenancy/ITenantKeyProvider.cs) using RFC 5869 HKDF SHA-256 for deterministic per-tenant key derivation.
- **Server-Side Tenant Resolution & Session Binding:**
  - Implemented [`TenantResolutionMiddleware.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Middleware/TenantResolutionMiddleware.cs) with precedence: `X-Tenant-Slug` -> `X-Tenant-Id` -> `Host` header -> default tenant (`springfield`).
  - Added token-to-tenant validation: cross-tenant token mismatch is rejected with `403 Forbidden` (`tenant_mismatch`).
- **Platform Admin Governance:**
  - Created `UserRole.PLATFORM_ADMIN`. Enforced zero PHI access in `PermissionMatrix` and `AccessControlService` (`platform_admin_no_phi`).
- **API-Level Module Gating:**
  - Created [`RequireModuleAttribute`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Filters/RequireModuleFilter.cs) and decorated modular controllers (`BillingController`, `AppointmentsController`, `RefillsController`, `MessagesController`, `SharingController`, `FormsController`, `FamilyAndSettingsController`, `FhirController`). Returns `403 Forbidden` (`module_disabled`) when calling endpoints of a disabled module.
- **Tenant Onboarding & Compliance Documentation:**
  - Provisioned Adot Internal Medicine (`Adot-Hospital`) via `/api/platform/tenants/provision` with 1 facility and 10 departments.
  - Calculated WCAG 2.1 AA contrast for Adot primary color `#e01b24`: `4.81:1` against white (Passes AA normal and large text).
  - Authored [`docs/tenants/Adot-Hospital/provisioning-plan.md`](file:///home/michael/projects/app/medical_portal/docs/tenants/Adot-Hospital/provisioning-plan.md) (dedicated database isolation plan).
  - Authored [`docs/tenants/Adot-Hospital/compliance-notes.md`](file:///home/michael/projects/app/medical_portal/docs/tenants/Adot-Hospital/compliance-notes.md) (mapping controls to Ethiopian laws, explicitly claiming no compliance).
  - Authored [`docs/tenants/Adot-Hospital/handover.md`](file:///home/michael/projects/app/medical_portal/docs/tenants/Adot-Hospital/handover.md) (config summary, credentials needed, open items).
  - Authored [`docs/adr/0006-multi-tenancy-architecture-and-isolation.md`](file:///home/michael/projects/app/medical_portal/docs/adr/0006-multi-tenancy-architecture-and-isolation.md).
- **Test Suite Results:**
  - Added [`MultiTenancySecurityTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/MultiTenancySecurityTests.cs) (11 tests covering tenant resolution, Adot-Hospital provisioning, WCAG AA contrast calculation, theme endpoint, cross-tenant read leak, cross-tenant write leak, cross-tenant mutation leak, token tenant mismatch, module gating, platform admin zero-PHI, and Postgres RLS SQL generator).
  - Total tests passing: **311 passed, 0 failed, 0 skipped**.

---

## 🇪🇹 Phase 6 Ethiopia Foundations Verification

- **Branch:** `phase-6-ethiopia-foundations`
- **Architectural Reference:** [ADR 0007: Ethiopian Calendar Engine and Offline-First Localization Architecture](adr/0007-ethiopian-calendar-engine-and-offline-localization.md)
- **Key Deliverables Implemented:**
  - **Pure Bidirectional Conversion Engine:**
    - Implemented [`EthiopianCalendar.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Common/EthiopianCalendar.cs) with exact epoch day offset 2,795, 4-year cycle (1,461 days), leap rule ($Year \pmod 4 == 3$), and Pagume (5/6 days).
    - Verified against 73,414 consecutive days (1900-01-01 to 2100-12-31) with zero drift.
    - Verified historical benchmarks: Ethiopian Millennium (2007-09-12 -> 2000-01-01), Battle of Adwa (1896-03-01 -> 1888-06-23), Enkutatash 2016/2017, Pagume 5/6, and 1 Hamle fiscal year boundary.
    - Mirrored in [`ethiopian-calendar.js`](file:///home/michael/projects/app/medical_portal/frontend/js/ethiopian-calendar.js) for offline browser client.
  - **Precalculated `calendar_dim` SQL Dimension:**
    - Created [`CalendarDim`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/CalendarEntities.cs) and EF Core migration `AddCalendarDimensionTable`.
    - Populated 73,414 rows (1900..2100) via [`DbSeeder.SeedCalendarDimensionAsync`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/DbSeeder.cs) with `ChangeTracker.Clear()` chunking.
    - Indexed on `(ethiopian_year, ethiopian_month)`, `(ethiopian_fiscal_year, ethiopian_fiscal_quarter)`, and `dhis2_monthly_period`.
    - Enables high-speed SQL reporting and HMIS aggregation directly in PostgreSQL without runtime conversions.
  - **Fiscal Periods & DHIS2 Interoperability Formats:**
    - Ethiopian Fiscal Year (EFY) calculation: Hamle 1 (July 8) starts EFY $Year+1$, Q1 (Hamle-Meskerem), Q2 (Tikimt-Tahsas), Q3 (Tir-Magabit), Q4 (Miyazya-Sene).
    - DHIS2 period generators: Monthly (`YYYYMM`), Quarterly (`YYYYQ#`), Yearly (`YYYY`).
  - **Three-Language Localization Catalogs (`en`, `am`, `om`):**
    - [`LocalizationCatalog.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Common/LocalizationCatalog.cs) with 100% complete key parity across English, Amharic (አማርኛ), and Afaan Oromoo.
    - [`LocalizationService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Localization/LocalizationService.cs) with graceful fallback.
    - Static offline catalogs in `frontend/locales/en.json`, `am.json`, `om.json`.
    - Offline client engine [`i18n.js`](file:///home/michael/projects/app/medical_portal/frontend/js/i18n.js) with dynamic DOM translation and events.
  - **Calendar & Localization REST Endpoints:**
    - `GET /api/calendar/convert?date=YYYY-MM-DD`
    - `GET /api/calendar/convert-ethiopian?year=Y&month=M&day=D`
    - `GET /api/calendar/today` (localized to `Africa/Addis_Ababa` UTC+3)
    - `GET /api/calendar/month-view?year=Y&month=M`
    - `GET /api/localization/{language}`
    - `GET /api/localization/languages`
  - **Working Rule 8 Offline Self-Sufficiency & Dialog Elimination:**
    - Vendored Microsoft SignalR client locally in `frontend/js/vendor/signalr.min.js`.
    - Vendored Ethiopic typography locally in `frontend/fonts/NotoSansEthiopic-Regular.woff2`.
    - Zero remote CDN dependencies in `frontend/index.html`.
    - Eliminated all 28 browser dialogs (`window.alert`, `window.confirm`, `window.prompt`), replacing them with accessible in-app modal cards and toast notifications.
- **Automated Test Results:**
  - Added [`EthiopianCalendarAndLocalizationTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/EthiopianCalendarAndLocalizationTests.cs) (17 comprehensive tests covering roundtrip math, historical dates, fiscal periods, negative boundaries, DHIS2 formats, multi-language month/day names, catalog parity, localization fallback, database dimension SQL queries, API endpoints, Rule 8 browser hygiene, and offline asset verification).
  - Total solution tests passing: **328 passed, 0 failed, 0 skipped**.

---

## 🛡️ Phase 7 Frontend Rebuild & XSS Containment Verification

- **Branch:** `phase-7-frontend-rebuild`
- **Findings Addressed:** SEC-02 (Stored DOM XSS across the portal)
- **Architectural Reference:** [ADR 0008: Stored DOM XSS Containment and Strict Content Security Policy (CSP)](adr/0008-stored-xss-containment-and-strict-csp.md)
- **Key Changes Implemented:**
  - **Server-Side Clinical Summary Encoding:**
    - Applied `WebUtility.HtmlEncode` to all provider names (`patient.PrimaryProvider.FirstName`, `patient.PrimaryProvider.LastName`), textual observation values (`v.ValueText`), units (`v.Unit`), and reference ranges in [`ClinicalSummaryGenerator.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs).
    - Removed inline event handler `onclick="window.print()"` from the export summary button; introduced same-origin external script [`frontend/js/print.js`](file:///home/michael/projects/app/medical_portal/frontend/js/print.js).
  - **Strict Content-Security-Policy (CSP):**
    - Enforced `script-src 'self'` without `'unsafe-inline'` and without `'unsafe-eval'` in [`Program.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Program.cs) and [`nginx.conf`](file:///home/michael/projects/app/medical_portal/nginx.conf).
    - Removed remote script and font CDN origins (`cdnjs.cloudflare.com`, `fonts.googleapis.com`, `fonts.gstatic.com`).
    - Added `object-src 'none'`, `base-uri 'self'`, and `frame-ancestors 'none'`.
  - **Client-Side Contextual Escaping & DOM Node Assignment:**
    - Introduced universal `escapeHtml` contextual encoder in [`frontend/js/app.js`](file:///home/michael/projects/app/medical_portal/frontend/js/app.js).
    - Refactored `showToast`, `showInAppAlert`, and `showInAppConfirm` to assign user-supplied messages and titles via DOM element `.textContent` rather than `.innerHTML` string interpolation.
    - Contextually escaped all dynamic data across overview, encounter cockpit, allergies, medications, message threads/replies, billing statements, proxy relationships, record shares, facility locators, and admin console workforce tables.
- **Negative & Abuse Test Suite:**
  - Added [`backend/tests/MedicalPortal.Tests/XssContainmentAndCspTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/XssContainmentAndCspTests.cs) (5 tests covering strict CSP header verification, anti-sniff and frame-deny security headers, multi-entity XSS injection encoding into ClinicalSummaryGenerator, frontend static hygiene, and offline print script).
- **Automated Test Results:**
  - Full test suite passing: **333 passed, 0 failed, 0 skipped**.

---

## 🏥 Phase 8 Clinical Core Integration & EMR Sync Verification

- **Branch:** `phase-8-clinical-core-integration`
- **Architectural Reference:** [ADR 0009: Clinical Core OpenMRS FHIR R4 Integration & Clinical Cache Demolition](adr/0009-clinical-core-openmrs-fhir-integration.md)
- **Key Changes Implemented:**
  - **Entity Deprecation & Read-Through Cache Repurposing:**
    - Formally deprecated duplicated clinical tables in portal (`Observations`, `Conditions`, `Allergies`, `Prescriptions`) using `[Obsolete("Clinical data is mastered in OpenMRS via FHIR R4. Portal DB tables are retained strictly as a read-through cache / offline snapshot.")]`.
    - Portal clinical tables now operate strictly as a read-through cache / offline snapshot store.
    - Decoupled demo CDS rule sets (`DrugInteractionLogic`, `AllergyLogic`) with explicit `ClinicalCoreMasteryNotice` pointing canonical rules to OpenMRS 2.8+ / O3.
  - **FHIR Logical Identifier & Migration:**
    - Added `FhirPatientId` (indexed string) on `Patient` entity in `MedicalPortal.Core/Entities/CoreEntities.cs`.
    - Applied migration `20261024152402_AddFhirPatientIdAndClinicalCoreSync`.
  - **Clinical Core Synchronization Service (`IClinicalCoreSyncService`, `ClinicalCoreSyncService`):**
    - `SyncPatientClinicalDataAsync(patientId)`: Inbound FHIR R4 pull reconciles Observations (vitals & LOINC labs), Allergies, MedicationRequests, and Encounters (reason codes) into cached records, updates `HmsLastSync`, and refreshes `HealthSnapshot`. Gracefully degrades on FHIR network failure or offline mode.
    - `SyncOutboundAppointmentsAsync()`: Outbound push converting portal appointments to FHIR R4 `Appointment` resources (`booked`/`proposed`), linking `Patient/{fhirPatientId}`, and marking `HmsSynced = true`.
    - `SyncStalePatientsAsync(limit)`: Inbound batch pull ordered by `HmsLastSync` ascending.
    - `GetO3PatientChartUrlAsync(patientId)`: Resolves OpenMRS FHIR logical ID and formats deep link `${O3BaseUrl}/patient/{fhirPatientId}/chart`.
  - **Background Worker & DI Integration:**
    - Upgraded `HmsSyncWorker` to invoke `IClinicalCoreSyncService` alongside legacy sync.
    - Registered `IClinicalCoreSyncService, ClinicalCoreSyncService` in `Program.cs`.
  - **Clinical Controller & Chart Cutover:**
    - Dynamic FHIR sync-on-read in `ClinicalController.cs` for snapshot, labs, allergies, vitals, medications, and conditions with transparent fallback to local cached snapshots.
    - Added `GET /api/clinical/o3-chart-url` guarded by `[RequireClinician]` and `[RequirePatientAccess]` to route clinicians to OpenMRS O3 for encounter charting.
- **Automated Test Results:**
  - Added [`ClinicalCoreIntegrationAndSyncTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/ClinicalCoreIntegrationAndSyncTests.cs) (9 tests covering inbound pull, outbound appointment push, offline resilience, O3 chart URL resolution, stale patient pull, CDS mastery notice, clinician O3 chart access, non-clinician 403 rejection, cross-patient access 403 rejection).
  - Total solution tests passing: **342 passed, 0 failed, 0 skipped**.

---

## ⚡ Phase 9 Offline-First Facility Stack & LAN Operations Verification

- **Branch:** `phase-9-offline-first-stack`
- **Architectural Reference:** [ADR 0010: Offline-First Facility Stack, Transactional Outbox, and LAN Operations](adr/0010-offline-first-facility-stack-and-outbox-sync.md)
- **Key Changes Implemented:**
  - **Deployment Node Role Architecture:**
    - Introduced [`FacilityNodeOptions.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/FacilityNodeOptions.cs) supporting `DeploymentNodeRole.Facility` (authoritative local system of record on hospital LAN) vs `DeploymentNodeRole.RegionalCloud` (aggregator replica).
    - Added node monitoring endpoint `GET /api/system/node-status` returning role, facility code, connectivity state, queue depths, and installed reference packages.
  - **Transactional Outbox in PostgreSQL (`SyncOutboxMessage`):**
    - Created [`SyncOutboxMessage`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/OfflineSyncEntities.cs) entity implementing `ITenantScoped`.
    - Atomic persistence: Outbox records are generated within the same transaction as business events.
    - Unique index on `(TenantId, IdempotencyKey)`. Composite indexes on `(TenantId, Status, ScheduledAtUtc)` and `(TenantId, PatientId, PerPatientSequence)`.
    - Database migration `20261024155857_AddOfflineSyncAndOutbox`.
  - **Per-Patient Strict FIFO Ordering Barrier:**
    - Implemented in [`OutboxSyncDispatcher.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/OutboxSyncDispatcher.cs).
    - If message $N$ for patient $P$ encounters a transient failure or is awaiting retry in backoff, subsequent messages $N+1, N+2$ for patient $P$ are held back and never dispatched out of order.
  - **Store-and-Forward HTTPS with Exponential Backoff & Jitter:**
    - Dispatches to [`ICloudAggregatorClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/ICloudAggregatorClient.cs) with `Idempotency-Key` and `X-Facility-Code`.
    - Exponential backoff algorithm: $\min(\text{MaxBackoff}, \text{InitialBackoff} \times 2^{\text{Attempt}-1}) \times \text{jitter}(0.9 .. 1.1)$.
    - Idempotency deduplication: Replays returning HTTP 409 Conflict are recognized as already processed and marked `Delivered` without stopping the queue.
    - Unrecoverable 4xx or exceeded `MaxAttempts` move to `DeadLetter` status.
  - **Signed & Versioned Reference Data Packages (`ReferencePackage`):**
    - Created [`ReferencePackage`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/OfflineSyncEntities.cs) and [`ReferenceDataService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/ReferenceDataService.cs).
    - Validates HMAC-SHA256 signatures via constant-time comparison (`CryptographicOperations.FixedTimeEquals`).
    - Anti-downgrade version protection rejects incoming packages with `VersionOrdinal <= ExistingActiveVersionOrdinal`.
    - Supports automated online pull and offline import via `POST /api/system/reference-packages/install` (for USB drive distribution).
  - **Automated Facility Backup & Rotation:**
    - Created [`scripts/facility-backup.sh`](file:///home/michael/projects/app/medical_portal/scripts/facility-backup.sh) with compressed PostgreSQL dumps (`gzip -9`), SHA-256 manifests, strict permissions (`chmod 600`), and automated 30-day rotation.
- **Chaos Simulation & Automated Test Results:**
  - Added [`OfflineFirstFacilityStackTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/OfflineFirstFacilityStackTests.cs) (11 tests covering 24h network cut chaos simulation, network restoration queue draining, strict per-patient FIFO ordering barrier, idempotency deduplication replay resilience, exponential backoff scheduling, cryptographic signature verification, tampered payload rejection, anti-downgrade protection, cross-tenant outbox isolation, and node-status/drain-sync RBAC APIs).
  - Total solution tests passing: **353 passed, 0 failed, 0 skipped**.

---

## 🌐 Phase 10 National Interoperability Verification

- **Branch:** `phase-10-national-interoperability`
- **Architectural Reference:** [ADR 0011: National Interoperability (DHIS2 HMIS, eCHIS Referrals, Master Facility Registry, and Patient Deduplication)](adr/0011-national-interoperability-dhis2-echis-mfr.md)
- **Key Deliverables & Architectural Implementations:**
  - **DHIS2 Aggregate HMIS Integration:**
    - Indicator extraction engine (`Dhis2AggregateService`) computing aggregate indicators from local database records (`DE-OPD-TOTAL`, `DE-PATIENT-NEW`, `DE-RX-TOTAL`, `DE-LAB-TOTAL`, `DE-FORMS-SUBMITTED`) parameterized by reporting period (monthly `201902`, quarterly `2019Q1`).
    - Two-phase submission workflow: Mandatory dry-run validation (`/api/dataValueSets?dryRun=true`) followed by an explicit facility approval gate (`FacilityApproved`) before final submission.
    - Idempotent re-submission handling: Re-sending existing aggregate datasets updates reports in place with DHIS2 idempotency confirmation.
    - Cryptographically signed offline export fallback: Generates an air-gapped HMAC-SHA256 signed JSON export bundle with SHA-256 content checksums for manual transport to Woreda health offices during prolonged WAN cuts.
  - **eCHIS Community Health Referral Integration:**
    - Feature-flagged behind `Echis:Enabled` to prevent runtime failures in standalone facilities.
    - Bi-directional referral entity (`EchisReferral`) managing community-to-hospital inbound referrals and hospital-to-community counter-referrals.
    - Clinician triage workflow (`POST /api/interop/echis/referrals/{id}/triage`) allowing clinical staff to review, accept, reject, or complete community referrals with clinical notes.
    - Testable abstraction via `IEchisClient` and `FakeEchisClient`.
  - **Master Facility Registry (MFR) Directory Sync:**
    - Enriched `Facility` entity with national registry attributes (`MfrCode`, `FacilityType`, `Region`, `Zone`, `Woreda`, `OperationalStatus`, `MfrLastSyncedAtUtc`).
    - Automated reconciliation service (`MfrSyncService`) synchronizing official facility codes, administrative hierarchy, and operational status from `IMfrClient`.
  - **National Patient Identity (MPI) & Demographic Deduplication:**
    - Enriched `Patient` entity with `NationalPatientId` (Fayya ID), `NationalPatientIdUri` (`http://moh.gov.et/national-id`), and `FacilityMrnUri` (`http://hospital.et/mrn/{FacilityCode}`).
    - Demographic duplicate scanner (`PatientDeduplicationService`) computing probabilistic match confidence (exact Fayya ID = 1.0, Phone + DOB = 0.95, Phone + Name = 0.80) and queuing duplicate candidates.
    - Atomic database merge transaction re-keying appointments, encounters, forms, lab orders, prescriptions, and billing records to the master patient record, soft-deleting the duplicate, and writing an immutable audit log entry.
  - **RBAC Policy Enforcement:**
    - Interoperability endpoints secured with role-based policies (`FacilityAdmin`, `ClinicalDirector`, `Doctor`, `Nurse`).
- **Automated Test Results:**
  - Added [`NationalInteroperabilityTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/NationalInteroperabilityTests.cs) (10 tests covering DHIS2 indicator extraction, dry-run validation, approval gate enforcement, idempotent re-submission, HMAC-SHA256 offline bundle export, eCHIS feature flag gating, inbound triage workflow, MFR directory reconciliation, MPI demographic merge with audit log verification, and RBAC authorization).
  - Total solution tests passing: **363 passed, 0 failed, 0 skipped**.

---

## 🔐 Phase 11 Data Protection & Hardening Verification

- **Branch:** `phase-11-data-protection-and-hardening`
- **Architectural Reference:** [ADR 0012: Data Protection, Field Encryption, and Deployment Hardening (SEC-11, 12, 17, 19, 20, 21, 22)](adr/0012-data-protection-and-security-hardening.md)
- **Key Deliverables & Architectural Implementations:**
  - **SEC-11 (Incomplete PHI Logging Scrubbing):**
    - `LoggingExtensions.AddPhiScrubber`: Invokes `builder.ClearProviders()` prior to registering `ScrubbingLoggerProvider`, preventing unscrubbed default console, debug, or event source log leakage.
    - `PhiScrubber.cs`: Enriched with Ethiopian phone number regex patterns (`(?:\+?251|0)[79]\d{8}\b`), national Fayya IDs (`\bET-FAYYA-[A-Za-z0-9-]+\b`), and sensitive keys (`mfasecret`, `totpsecret`, `nationalpatientid`).
    - `HmsConflictService.cs`: Eliminated plaintext demographic value logging (`Local: '...', HMS: '...'`), logging only property names and lengths.
  - **SEC-12 (Sensitive Data Plaintext at Rest & Key Separation):**
    - `EncryptedStringConverter.cs`: Transparent authenticated AES-256-GCM field encryption converter integrated into EF Core `MedicalPortalDbContext`.
    - Protected columns: `UserAccount.MfaSecret`, `Appointment.PatientNotes`, `HmsDemographicConflict.LocalValue`, `HmsValue`, `LastSyncedValue`.
    - `ConfigurationKeyProvider.cs`: Enforces fail-fast startup abort in Production if `FieldEncryption:Keys` is missing or empty. Prohibits deriving encryption keys from `JWT_SECRET`. Uses an isolated deterministic dev master key in non-production environments.
  - **SEC-17 (Client IP Bypasses Trusted Proxy Boundary):**
    - `PublicIpService.cs`: Inspects forwarded headers (`CF-Connecting-IP`, `X-Real-IP`, `X-Forwarded-For`, `True-Client-IP`) strictly when the immediate TCP connection `RemoteIpAddress` is loopback (`127.0.0.1`/`::1`) or an explicitly authorized proxy CIDR. Rejects spoofed headers from untrusted external peers.
  - **SEC-19 (Non-Blocking CI Security & Mutable Actions):**
    - `.github/workflows/ci.yml`: Pinned all third-party actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `aquasecurity/trivy-action`, `gitleaks/gitleaks-action`) to immutable 40-character commit SHAs.
    - Enforced `exit-code: '1'` on Trivy scans to block PRs on High/Critical findings, and added an automated step asserting `dotnet list package --vulnerable --include-transitive`.
  - **SEC-20 (Vulnerable OpenTelemetry Protocol Exporter):**
    - `MedicalPortal.Web.csproj`: Upgraded `OpenTelemetry.Exporter.OpenTelemetryProtocol` to 1.15.3 and OpenTelemetry core dependencies to 1.11.2 / 1.15.3, eliminating CVE-2026-42191 / GHSA-4625-4j76-fww9. Confirmed zero vulnerable packages.
  - **SEC-21 (Unhardened Container Deployment):**
    - `Dockerfile` & `backend/Dockerfile`: Enforced non-root execution via `USER app`.
    - `docker-compose.yml`: Added `security_opt: [ "no-new-privileges:true" ]` across all application services. (PostgreSQL and Redis host port exposure was already eliminated in Phase 1).
  - **SEC-22 (Rate Limiting, PHI Caching & Webhook Bounds):**
    - `Program.cs`: Enforced anti-caching headers (`Cache-Control: no-store, no-cache, must-revalidate, proxy-revalidate`, `Pragma: no-cache`, `Expires: 0`) across all `/api` and `/R4` endpoints.
    - `HmsWebhookController.cs`: Applied `[RequestSizeLimit(1024 * 1024)]` (1MB cap) and bounded stream reading to defend against buffer exhaustion.
    - `Program.cs` & `AuthController.cs`: Configured `MfaChallengePolicy` (5 req/min per account/IP) with `[EnableRateLimiting("MfaChallengePolicy")]` on `MfaEnroll` and `MfaVerify`.
- **Automated Test Results:**
  - Added [`DataProtectionAndHardeningTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/DataProtectionAndHardeningTests.cs) (9 tests covering SEC-11 through SEC-22).
  - Total solution tests passing: **372 passed, 0 failed, 0 skipped**.

---

## 🔍 Security Audit Verification Matrix (SEC-01 .. SEC-22, SAFE-01)

Every audit finding from `docs/security-audit-2026-10-24.md` has been verified against the current codebase:

| Finding ID | Severity | Category | Status in Code | Code Evidence & Current Behavior | Planned Remediation |
|---|:---:|---|:---:|---|---|
| **SEC-01** | **Critical** | Auth Bypass / Privilege Escalation | **CLOSED** | Lazy session creation removed in `SessionService.cs`. Token claims reconciled with authoritative DB in `AuthFilters.cs`. `AuthorizationVersion` increments immediately revoke tokens. Static cluster key removed from `docker-compose.yml`. Covered by negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-02** | **Critical** | Stored DOM XSS | **CLOSED** | Contextual encoding applied in `ClinicalSummaryGenerator.cs` (provider names, observation texts, units). Strict CSP (`script-src 'self'`) without `'unsafe-inline'` or CDNs enforced in `Program.cs` and `nginx.conf`. In-app toasts/dialogs refactored to `.textContent`. Contextual `escapeHtml` applied across all template interpolations in `app.js`. Covered by negative tests in `XssContainmentAndCspTests.cs`. | Closed in Phase 7 |
| **SEC-03** | **Critical** | Plaintext Production Transport | **CLOSED** | HSTS middleware configured in `Program.cs` (`Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`). Production fail-fast assertions prohibit plain-HTTP endpoints for external integrations. | Closed in Phase 2 |
| **SEC-04** | **Critical** | Silent Fake Payments in Production | **CLOSED** | Removed `STRIPE_USE_FAKE=true` from `docker-compose.yml`. Added pre-build and post-build startup validations in `Program.cs` that abort if `Stripe:UseFake` is true, if `Stripe:SecretKey` is placeholder/empty, or if `FakePaymentGateway` is resolved in Production. Negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-05** | **High** | Payment Intent Replay & Misattribution | **CLOSED** | `ConsumedPaymentIntent` entity records processed payment intents with unique index on `GatewayPaymentIntentId` and composite index on `(PatientId, IdempotencyKey)`. `BillingController.cs` asserts `intent.PatientId == TargetPatientId` and rejects replayed intents with 409 Conflict. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-06** | **High** | Unenforced Record-Share Scopes | **CLOSED** | `ShareRedemptionController.cs` validates requested categories against `candidate.ScopeJson`. `FhirService.cs` provides `GetScopedPatientBundleAsync` filtering bundle resources by authorized categories (`conditions`, `allergies`, `medications`, `observations`, `appointments`). Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-07** | **High** | Caller-Optional Department Scoping | **CLOSED** | `AccessControlService.cs` rejects unassigned operational staff with `unassigned_staff_scope` (403 Forbidden). Single department membership is automatically derived; multi-department staff must provide `x-department-id` (`department_scope_required`). Mismatches rejected with `department_scope_mismatch`. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-08** | **High** | Missing HMS Admin Authorization | **CLOSED** | `/api/integrations/hms/conflicts` and `/api/integrations/hms/dead-letters` decorated with `[RequireAuth]` and `[RequireAdmin]` in `HmsWebhookController.cs`. Read access restricted to `ADMIN`, `SYSTEM_ADMIN`, `COMPLIANCE_AUDITOR`; mutating actions restricted to `ADMIN` and `SYSTEM_ADMIN`. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-09** | **High** | BOLA / IDOR in Refill Processing | **CLOSED** | `AppointmentsAndRefillsControllers.cs` verifies `r.Prescription.PatientId == TargetPatientId` in `ApproveRefill` and `DenyRefill`. Cross-patient attempts return 404 Not Found. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-10** | **High** | Weak Proxy Invites & Unbound Acceptance | **CLOSED** | `SharingAndFormsAndFamilyControllers.cs` generates 128-bit cryptographic random tokens (`INV-<32 hex chars>`). `AcceptProxyInvite` verifies `actorPerson.Email == invite.Email` and rejects self-acceptance (`actorPerson.Id == invite.InviterPersonId`). Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-11** | **High** | Incomplete PHI Logging Scrubbing | **CLOSED** | `LoggingExtensions.cs` invokes `builder.ClearProviders()` before registering `ScrubbingLoggerProvider`. `PhiScrubber.cs` masks Ethiopian phones (`+2519...`, `09...`), Fayya IDs (`ET-FAYYA-...`), and sensitive keys. Cleartext demographic comparison values eliminated from `HmsConflictService.cs`. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-12** | **High** | Sensitive Data Plaintext at Rest | **CLOSED** | EF Core `EncryptedStringConverter` provides authenticated AES-256-GCM encryption for `UserAccount.MfaSecret`, `Appointment.PatientNotes`, and HMS demographic conflict values. `ConfigurationKeyProvider.cs` enforces key separation and fails fast in Production without `FieldEncryption:Keys`. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-13** | **High** | Known Fallback HMS Secrets & Plain HTTP | **CLOSED** | Default fallback secret removed from `HmsWebhookHandler.cs`. Fallback `admin`/`Admin` credentials removed from `HmsClient.cs`. Startup validator in `Program.cs` prohibits fallback secrets and enforces HTTPS. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-14** | **Medium** | SignalR Bypasses Session Revocation | **CLOSED** | `PortalHub.cs` now validates session through `ISessionService.ValidateSessionAsync`, verifying `AuthorizationVersion`, active status, and database role prior to establishing connection. Revoked/unknown sessions rejected. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-15** | **Medium** | Form Writes Require Only Read Permission | **CLOSED** | Dedicated `Permission.submitForms` added and mapped to `CanSubmitForms` on `ProxyGrant` and `ProxyInvite`. Permission matrix configured with `(UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Write)`. `POST /api/forms/{id}/submit` guarded with `[RequirePatientAccess(Permission.submitForms, AppResource.ClinicalResults, AppAction.Write)]`. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-16** | **Medium** | Direct Patient/Proxy Refund Triggering | **CLOSED** | `BillingController.cs` restricts `POST /api/billing/refunds` direct gateway execution strictly to billing staff (`BILLING`, `ADMIN`, `SYSTEM_ADMIN`) with mandatory refund audit reason. Patient/proxy self-refund attempts return 403 Forbidden. Covered in `AuthorizationAndPaymentsSecurityTests.cs`. | Closed in Phase 4 |
| **SEC-17** | **Medium** | Client IP Bypasses Trusted Proxy Boundary | **CLOSED** | `PublicIpService.cs` validates immediate TCP `RemoteIpAddress`. Forwarded proxy headers (`CF-Connecting-IP`, `X-Forwarded-For`, etc.) are rejected from untrusted peers and only accepted from loopback or verified trusted proxies. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-18** | **Medium** | Plain HTTP Third-Party Geolocation Call | **CLOSED** | Plain-HTTP calls to `http://ip-api.com` in `PublicIpService.cs` completely eliminated; fallback geolocation returns clean offline/mock metadata. Tested in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |
| **SEC-19** | **Medium** | Non-Blocking CI Security & Mutable Actions | **CLOSED** | All GitHub Actions in `.github/workflows/ci.yml` pinned to immutable commit SHAs. Trivy scans enforce `exit-code: '1'` on High/Critical findings. Added automated transitive vulnerability scan `dotnet list package --vulnerable`. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-20** | **Medium** | Vulnerable OpenTelemetry Protocol Exporter | **CLOSED** | Upgraded `OpenTelemetry.Exporter.OpenTelemetryProtocol` to 1.15.3 and OpenTelemetry dependencies to 1.11.2 / 1.15.3, eliminating CVE-2026-42191 / GHSA-4625-4j76-fww9. `dotnet list package --vulnerable` confirms 0 vulnerable packages. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-21** | **Medium** | Unhardened Container Deployment | **CLOSED** | `Dockerfile` and `backend/Dockerfile` run as non-root `USER app`. `docker-compose.yml` services enforce `security_opt: [ "no-new-privileges:true" ]`. Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SEC-22** | **Medium** | Rate Limiting, Caching & Webhook Limits | **CLOSED** | `/api` and `/R4` endpoints enforce `Cache-Control: no-store, no-cache, must-revalidate, proxy-revalidate`, `Pragma: no-cache`, `Expires: 0`. `HmsWebhookController` restricts payload size to 1MB (`[RequestSizeLimit]`). Auth MFA endpoints protected by `MfaChallengePolicy` (5 req/min). Tested in `DataProtectionAndHardeningTests.cs`. | Closed in Phase 11 |
| **SAFE-01** | **Safety** | Unconditional Demo Decision-Support Data | **CLOSED** | Pre-build and post-build startup validations in `Program.cs` prevent production boot if `Clinical:UseDemoKnowledgeBase` is true or if demo knowledge base implementations are registered in DI. Negative tests in `CriticalSecurityContainmentTests.cs`. | Closed in Phase 2 |

---

## ⚠️ Key Architectural & Operational Risks

1. **Client-Side Cross-Site Scripting (SEC-02):**
   Remediated in Phase 7. Server-side clinical HTML summaries encoded with `WebUtility.HtmlEncode`, client-side dynamic rendering secured via universal `escapeHtml` contextual encoder and `.textContent` DOM node assignments, strict CSP without `'unsafe-inline'` or remote CDNs enforced in `Program.cs` and `nginx.conf`. Verified across 333 tests.
2. **Access Control & Scope Verification (SEC-05, SEC-06, SEC-07, SEC-08, SEC-09, SEC-10, SEC-15, SEC-16):**
   Remediated in Phase 4. Payment intent replays are blocked, share bundles are filtered strictly by granted categories, operational staff scoping is enforced server-side, HMS integration endpoints are restricted to administrators, refill BOLA is resolved, proxy invites are 128-bit and email-bound, form submissions require write permission, and direct patient refunds are segregated. Verified across 300 tests.
3. **Clinical Scope vs EMR Integration (SAFE-01, Phase 3/8):**
   Remediated in Phase 8. Clinical entities in the portal database have been deprecated and repurposed strictly as read-through caches / offline snapshots. Clinician charting is cut over to OpenMRS O3 SPA via deep-linking, inbound/outbound sync is powered by `IClinicalCoreSyncService` over FHIR R4, and canonical CDS is mastered in OpenMRS. Verified across 342 tests.
4. **Offline LAN Stack & Outbox Store-and-Forward (Phase 9):**
   Remediated in Phase 9. Local facility servers operate as the primary authoritative system of record. Outbound data is queued atomically in a PostgreSQL transactional outbox with per-patient FIFO ordering, exponential backoff with jitter, idempotency deduplication, HMAC-SHA256 signed reference packages, and automated database dumps with 30-day rotation. Verified across 353 tests.
5. **National Interoperability Unknowns (Phase 10):**
   Remediated in Phase 10. DHIS2 aggregate HMIS extraction, dry-run validation, facility approval gating, and HMAC-SHA256 signed offline export bundles implemented; eCHIS community referrals with clinician triage workflow abstracted behind `IEchisClient`/`FakeEchisClient`; MFR directory reconciliation integrated via `IMfrClient`; national Fayya ID and demographic deduplication scanner with atomic merge transactions implemented. Verified across 363 tests.
