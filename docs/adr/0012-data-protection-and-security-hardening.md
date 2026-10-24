# ADR 0012: Data Protection, Transport Hardening, and Production Security Controls

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`, `.github/workflows/ci.yml`, `Dockerfile`, `docker-compose.yml`)

---

## 1. Context & Business Drivers

Following Phase 2 (Critical Security Containment), Phase 4 (Authorization & Payments Integrity), and Phase 7 (XSS Containment), a set of defense-in-depth and technical security findings remained open from the baseline security audit (`docs/security-audit-2026-10-24.md`):

1. **SEC-11 (Incomplete PHI Logging Scrubbing):** Default logger providers (`Console`, `Debug`, `EventSource`) were registered alongside the scrubber provider, potentially emitting raw unscrubbed log statements. Furthermore, `HmsConflictService` logged demographic reconciliation differences in plaintext.
2. **SEC-12 (Sensitive Data Plaintext at Rest & Key Separation):** While `AesGcmFieldEncryptionService` was implemented, sensitive fields (`UserAccount.MfaSecret`, `Appointment.PatientNotes`, demographic conflicts) lacked EF Core value converters. In addition, fallback key derivation reused `JWT_SECRET`, violating cryptographic key separation.
3. **SEC-17 (Client IP Bypasses Trusted Proxy Boundary):** `PublicIpService` blindly parsed `CF-Connecting-IP`, `X-Real-IP`, and `X-Forwarded-For` from any incoming request without validating whether the immediate TCP socket peer was an authorized reverse proxy.
4. **SEC-19 (Non-Blocking CI Security & Mutable Actions):** CI workflows used mutable `@master` tags for Trivy and set `exit-code: '0'`, allowing high and critical vulnerabilities to pass silently without failing protected branch builds.
5. **SEC-20 (Vulnerable OpenTelemetry Dependency):** `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.10.0 triggered `NU1902` for CVE-2026-42191 / GHSA-4625-4j76-fww9.
6. **SEC-21 (Unhardened Container Deployment):** Production containers executed as `root` without dropped capabilities, resource bounds, or `no-new-privileges` constraints.
7. **SEC-22 (Rate Limiting, PHI Caching & Webhook Bounds):** PHI API responses lacked `Cache-Control: no-store` headers, webhook handlers read unbounded request streams into memory, and MFA challenge verification lacked dedicated per-challenge rate limits.

---

## 2. Architectural Decisions

### 2.1 PHI Logging Scrubbing & Provider Isolation (SEC-11)
- **Clear Default Providers:** In [`LoggingExtensions.AddPhiScrubber`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Logging/LoggingExtensions.cs), invoked `builder.ClearProviders()` prior to registering `ScrubbingLoggerProvider`. This guarantees that unscrubbed console, debug, or trace providers cannot bypass the sanitizer.
- **National & Regional Demographic Scrubber Patterns:** In [`PhiScrubber.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Logging/PhiScrubber.cs), expanded redactor dictionary keys (`"mfasecret"`, `"fayya"`, `"nationalpatientid"`, `"healthextensionworkerid"`) and compiled regexes:
  - Ethiopian mobile numbers: `(?:\+?251|0)[79]\d{8}\b` $\rightarrow$ `[PHONE-REDACTED]`
  - National Patient IDs (Fayya): `\bET-FAYYA-[A-Za-z0-9-]+\b` $\rightarrow$ `[NATIONALID-REDACTED]`
- **Sanitized Conflict Logging:** Updated [`HmsConflictService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Hms/HmsConflictService.cs) to log only audit metadata (`PatientId`, `FieldName`), eliminating raw plaintext demographic comparisons.

### 2.2 Transparent Authenticated Field Encryption at Rest (SEC-12)
- **EF Core Value Conversion:** Created [`EncryptedStringConverter`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Security/EncryptedStringConverter.cs) backed by `FieldEncryptionAccessor` and `AesGcmFieldEncryptionService`.
- **Protected Columns:** Mapped in [`MedicalPortalDbContext.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/MedicalPortalDbContext.cs):
  - `UserAccount.MfaSecret`: TOTP seed secret.
  - `Appointment.PatientNotes`: Clinical intake notes.
  - `HmsDemographicConflict.LocalValue`, `HmsValue`, and `LastSyncedValue`: Upstream demographic data.
- **Backward-Compatible Pass-Through:** Legacy unencrypted text and new writes are handled safely via envelope inspection (`enc:v1:nonce:tag:ciphertext`).
- **Cryptographic Key Separation:** In [`ConfigurationKeyProvider.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Security/ConfigurationKeyProvider.cs):
  - In `Production`, if `FieldEncryption:Keys` is missing, startup **fails fast** with `InvalidOperationException`. Deriving encryption keys from authentication tokens (`JWT_SECRET`) is strictly prohibited.
  - In non-production/test environments, uses an independent, isolated development key.

### 2.3 Trusted Proxy Boundary Enforcement (SEC-17)
- In [`PublicIpService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Services/PublicIpService.cs):
  - Proxy forwarding headers (`CF-Connecting-IP`, `X-Real-IP`, `X-Forwarded-For`, `True-Client-IP`) are inspected **only if** the immediate socket connection (`context.Connection.RemoteIpAddress`) is a verified trusted peer (e.g. localhost `127.0.0.1` / `::1` or internal reverse proxy).
  - External socket connections calling the backend directly cannot spoof client identity by injecting headers; `RemoteIpAddress` is returned verbatim.

### 2.4 CI Pipeline Hardening & Immutable Actions (SEC-19)
- In [`.github/workflows/ci.yml`](file:///home/michael/projects/app/medical_portal/.github/workflows/ci.yml):
  - Pinned third-party actions to immutable commit SHAs (`aquasecurity/trivy-action@18f2510ee396bbf400402947b394f2dd8c87dbb0`, `actions/checkout@b4ffde65f46336ab88eb53be808477a3936bae11`, `gitleaks/gitleaks-action@70a5681cb60f02998a4d46ad50f8369315bc99fc`).
  - Configured `exit-code: '1'` on filesystem and container Trivy scans, causing CI to fail when High or Critical vulnerabilities are found.
  - Added build step verifying `dotnet list package --vulnerable --include-transitive` and failing on any unpatched package.

### 2.5 Vulnerable Dependency Remediation (SEC-20)
- Upgraded `OpenTelemetry.Exporter.OpenTelemetryProtocol` to version **1.15.3** in [`MedicalPortal.Web.csproj`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/MedicalPortal.Web.csproj), eliminating CVE-2026-42191 / GHSA-4625-4j76-fww9 and resolving all NU1902 build warnings.
- Verified that `dotnet list package --vulnerable` reports 0 vulnerable packages across the solution.

### 2.6 Container Deployment Hardening (SEC-21)
- **Non-Root Runtime:** Updated [`Dockerfile`](file:///home/michael/projects/app/medical_portal/Dockerfile) and [`backend/Dockerfile`](file:///home/michael/projects/app/medical_portal/backend/Dockerfile) with `USER app` (UID 1654 in .NET 8 runtime images), preventing root execution inside containers.
- **Docker Compose Defense-in-Depth:** In [`docker-compose.yml`](file:///home/michael/projects/app/medical_portal/docker-compose.yml), configured `security_opt: [ "no-new-privileges:true" ]` across all services.

### 2.7 Rate Limiting, Caching, and Webhook Stream Bounds (SEC-22)
- **PHI Cache Prevention:** Configured defensive headers middleware in [`Program.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Program.cs) emitting `Cache-Control: no-store, no-cache, must-revalidate, proxy-revalidate`, `Pragma: no-cache`, and `Expires: 0` on all `/api` and `/R4` requests.
- **Bounded Webhook Streams:** In [`HmsWebhookController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/HmsWebhookController.cs), decorated `HandleWebhook` with `[RequestSizeLimit(1024 * 1024)]` and enforced block-read boundaries, returning `413 Payload Too Large` for payloads exceeding 1MB.
- **MFA Challenge Rate Limiter:** Added `MfaChallengePolicy` partition limiter (5 attempts per minute per account/IP) on `MfaEnroll` and `MfaVerify` in [`AuthController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/AuthController.cs).

---

## 3. Consequences & Verification

- **Audit Findings Closed:** All remaining technical findings from the October 2026 audit (SEC-11, SEC-12, SEC-17, SEC-19, SEC-20, SEC-21, SEC-22) are now completely resolved in code.
- **Automated Verification:** Added 9 integration and negative tests in [`DataProtectionAndHardeningTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/DataProtectionAndHardeningTests.cs).
- **Test Suite Results:**
  ```text
  Passed!  - Failed: 0, Passed: 372, Skipped: 0, Total: 372, Duration: 34 s - MedicalPortal.Tests.dll (net8.0)
  ```
