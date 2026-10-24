# Modular Low-Cost Hospital Platform & Transformation Progress

**Repository:** `medical_portal`  
**Current Phase:** Phase 0: Baseline [APPROVAL GATE]  
**Last Updated:** 2026-10-24  
**Current Test Suite Baseline:** **259 passed, 0 failed, 0 skipped** (.NET 8)

---

## 📅 Phase Execution Roadmap

| Phase | Description | Status | Approval Gate | Test Results / Findings Addressed |
|:---:|---|:---:|:---:|---|
| **0** | **Baseline & Security Audit Reconciliation** | **Completed** | **GATE 0 (Pending Approval)** | 259 passing tests; verified SEC-01..SEC-22 + SAFE-01; wrote ADR 0001 & Open Questions |
| 1 | Restructure (`backend/`, `frontend/`, Same-Origin Nginx) | Planned | Gate 1 | Directory reorg via `git mv`; zero behavior change |
| 2 | Contain Critical Security Findings (SEC-01, 03, 04, 13, 14, 18) | Planned | Gate 2 | Eliminate lazy sessions, enforce token-DB sync, fail-fast prod settings, HTTPS/HSTS |
| 3 | Clinical Core Spike (OpenMRS 2.8+ / O3 on PostgreSQL vs Bahmni) | Planned | Gate 3 | FHIR R4 JSON & REST evaluation; memory/startup footprint; DHIS2 push |
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

## 🔍 Security Audit Verification Matrix (SEC-01 .. SEC-22, SAFE-01)

Every audit finding from `docs/security-audit-2026-10-24.md` has been verified against the current codebase:

| Finding ID | Severity | Category | Status in Code | Code Evidence & Current Behavior | Planned Remediation |
|---|:---:|---|:---:|---|---|
| **SEC-01** | **Critical** | Auth Bypass / Privilege Escalation | **OPEN** | `docker-compose.yml:70,94` contains static secret `JWT_SECRET=ThisIsAClusterSecretKeyForMultiInstanceDeployments12345!`. `SessionService.cs:108` lazily creates session for unknown `sid`. `AuthFilters.cs:57` blindly trusts token claims (`claims.Role`) without reconciling against database. | Phase 2 |
| **SEC-02** | **Critical** | Stored DOM XSS | **OPEN** | `app.js:12,417,545,650,2034` assigns user input directly to `.innerHTML` without contextual escaping. `ClinicalSummaryGenerator.cs:135,220` interpolates unencoded strings into HTML. `Program.cs:496` CSP permits `'unsafe-inline'`. | Phase 7 |
| **SEC-03** | **Critical** | Plaintext Production Transport | **OPEN** | `nginx.conf:8` listens only on port 80. `Program.cs` lacks HTTPS redirection and HSTS. `AuthController.cs:668` marks cookies `Secure` only if incoming request is HTTPS. `docker-compose.yml:105` publishes port 80. | Phase 2 |
| **SEC-04** | **Critical** | Silent Fake Payments in Production | **OPEN** | `Program.cs:240` selects `FakePaymentGateway` when Stripe key is unset even in Production. `docker-compose.yml:71,95` sets `STRIPE_USE_FAKE=true` on containers marked `ASPNETCORE_ENVIRONMENT=Production`. `FakePaymentGateway.cs:94` succeeds any unknown intent for $50. | Phase 2 |
| **SEC-05** | **High** | Payment Intent Replay & Misattribution | **OPEN** | `BillingController.cs:176` accepts existing intents based solely on gateway success without verifying `intent.PatientId == TargetPatientId` or prior consumption. `MedicalPortalDbContext.cs` lacks unique indexes on `GatewayPaymentIntentId` and `IdempotencyKey`. | Phase 4 |
| **SEC-06** | **High** | Unenforced Record-Share Scopes | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:97` persists `ScopeJson`, but `ShareRedemptionController.cs:139` ignores requested categories and unconditionally returns `GetPatientEverythingBundleAsync` exposing full PHI. | Phase 4 |
| **SEC-07** | **High** | Caller-Optional Department Scoping | **OPEN** | `AuthFilters.cs:161` reads `x-department-id` optionally. `AccessControlService.cs:58,84` skips scope checks if header omitted, granting operational staff (`SCHEDULER`, `BILLING`, `NURSE`, etc.) global patient access. | Phase 4 |
| **SEC-08** | **High** | Missing HMS Admin Authorization | **OPEN** | `HmsWebhookController.cs:54` endpoints (`/conflicts`, `/conflicts/{id}/resolve`, `/dead-letters`) require only basic `[RequireAuth]`. Any authenticated patient or staff member can inspect and mutate integration records. | Phase 4 |
| **SEC-09** | **High** | BOLA / IDOR in Refill Processing | **OPEN** | `AppointmentsAndRefillsControllers.cs:468,496` queries refills solely by route ID without asserting `Prescription.PatientId == TargetPatientId`. Clinician authorized for Patient A can approve/deny refills for Patient B. | Phase 4 |
| **SEC-10** | **High** | Weak Proxy Invites & Unbound Acceptance | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:325` generates 32-bit entropy (4 bytes). `AcceptProxyInvite` does not verify that accepting user's email matches `invite.Email`. No dedicated rate limit. | Phase 4 |
| **SEC-11** | **High** | Incomplete PHI Logging Scrubbing | **OPEN** | `LoggingExtensions.cs:8` registers an extra logger provider without wrapping or clearing default providers (`Console`, `Debug`, `EventSource`). `HmsConflictService.cs:113` logs demographic comparison strings directly. | Phase 11 |
| **SEC-12** | **High** | Sensitive Data Plaintext at Rest | **OPEN** | `AesGcmFieldEncryptionService` is registered but not hooked into EF Core `ValueConverter` mappings. `AuthController.cs:262` stores `account.TotpSecret` in plaintext. `ConfigurationKeyProvider.cs:43` derives fallback encryption key from `JWT_SECRET`. | Phase 11 |
| **SEC-13** | **High** | Known Fallback HMS Secrets & Plain HTTP | **OPEN** | `HmsWebhookHandler.cs:33` falls back to `demo-hms-webhook-secret`. `HmsClient.cs:57` defaults to `admin`/`Admin`. `Program.cs` defaults to plain `http://localhost:3210`. | Phase 2 |
| **SEC-14** | **Medium** | SignalR Bypasses Session Revocation | **OPEN** | `PortalHub.cs:23` validates JWT signature directly without calling `ISessionService.ValidateSessionAsync`. Revoked sessions or deactivated accounts retain real-time SignalR hub connectivity. | Phase 2 |
| **SEC-15** | **Medium** | Form Writes Require Only Read Permission | **OPEN** | `SharingAndFormsAndFamilyControllers.cs:228` guards form response submission using `Permission.viewRecords` (read permission) instead of a dedicated write permission. | Phase 4 |
| **SEC-16** | **Medium** | Direct Patient/Proxy Refund Triggering | **OPEN** | `BillingController.cs:438` permits callers with `payBills` permission to directly invoke gateway refund operations without a billing staff review or approval workflow. | Phase 4 |
| **SEC-17** | **Medium** | Client IP Bypasses Trusted Proxy Boundary | **OPEN** | `PublicIpService.cs:62` reads `CF-Connecting-IP`, `X-Real-IP`, and `X-Forwarded-For` directly from headers without checking if immediate socket peer is an authorized proxy. | Phase 11 |
| **SEC-18** | **Medium** | Plain HTTP Third-Party Geolocation Call | **OPEN** | `PublicIpService.cs:241,321` makes outbound HTTP calls to `http://ip-api.com` transmitting client network metadata in unencrypted plaintext. | Phase 2 |
| **SEC-19** | **Medium** | Non-Blocking CI Security & Mutable Actions | **OPEN** | `.github/workflows/ci.yml:106,131` uses mutable `@master` tags for Trivy and sets `exit-code: '0'`, preventing high/critical vulnerabilities from failing the CI build. | Phase 11 |
| **SEC-20** | **Medium** | Vulnerable OpenTelemetry Protocol Exporter | **OPEN** | `MedicalPortal.Web.csproj:19` references `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.10.0, which emits `NU1902` for CVE-2026-42191 / GHSA-4625-4j76-fww9. Requires update to 1.15.3+. | Phase 11 |
| **SEC-21** | **Medium** | Unhardened Container Deployment | **OPEN** | `Dockerfile` runs container as `root`. `docker-compose.yml:9,50` publishes PostgreSQL port 5432 and Redis port 6379 directly to host with default credentials. | Phase 11 |
| **SEC-22** | **Medium** | Rate Limiting, Caching & Webhook Limits | **OPEN** | Memory-only rate limits in `Program.cs`. No `Cache-Control: no-store` on PHI API routes. `HmsWebhookController.cs:29` reads unbounded request streams into memory without size limit. | Phase 11 |
| **SAFE-01** | **Safety** | Unconditional Demo Decision-Support Data | **OPEN** | `Program.cs:232,233` unconditionally registers `DemoInteractionKnowledgeBase` and `DemoAllergyKnowledgeBase` across all environments including Production, with no fail-fast guard. | Phase 2 & 8 |

---

## ⚠️ Key Architectural & Operational Risks

1. **Authentication Integrity (SEC-01, 14):**
   Until Phase 2 eliminates lazy session creation and reconciles token claims against the database on each request, forged role claims in tokens signed by default cluster keys present immediate privilege escalation risk.
2. **Client-Side Cross-Site Scripting (SEC-02):**
   The legacy vanilla JS SPA relies on direct `.innerHTML` interpolation across nearly all screens. Replacing this during Phase 7 with a type-safe component framework (React/TypeScript) is essential before loading real patient data.
3. **Clinical Scope vs EMR Integration (SAFE-01, Phase 3/8):**
   Clinical entities currently duplicated in the portal database (observations, allergies, prescriptions) must be cleanly decoupled and handed over to the dedicated open-source EMR core (OpenMRS 2.8+ on PostgreSQL) through standardized FHIR R4 interfaces.
4. **National Interoperability Unknowns (Phase 10):**
   Critical Ethiopian health informatics specifications (DHIS2 Ethiopian calendar period encoding, eCHIS referral payloads, Master Facility Registry synchronization) must remain decoupled behind interfaces with fakes until official Ministry guidelines are confirmed.
