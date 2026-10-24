# Medical Portal Security Audit

**Assessment date:** 2026-10-24  
**Repository:** `medical_portal`  
**Assessment type:** Static application security and implementation review  
**Status:** Findings reported; remediation was not part of this assessment

## Executive summary

The application has useful security foundations, including password hashing, signed JWT validation, server-side sessions, centralized authorization filters, audit logging, rate limiting, webhook signature checks, and an extensive automated test suite. However, multiple implementation defects undermine those controls.

The most urgent issue is a complete privilege-escalation chain in the supplied clustered production configuration: a source-controlled JWT signing key can be used to forge a token, unknown token session IDs are automatically accepted and persisted, and security-sensitive role claims are not reconciled with the database. A regular account can therefore forge a `SYSTEM_ADMIN` session when the application is deployed with the supplied key.

Other high-impact findings include widespread stored DOM XSS, plaintext production traffic, fake payment behavior in production, payment-intent replay, ignored record-share scopes, caller-optional department scoping, insufficient authorization on HMS administration endpoints, cross-patient refill actions, PHI logging, and sensitive data stored without the registered encryption service.

This repository should not be exposed to production healthcare data or real payment processing until the critical and high-severity findings are remediated and independently retested.

## Severity summary

| Severity | Finding |
|---|---|
| Critical | SEC-01: Forged JWTs can become valid administrator sessions |
| Critical | SEC-02: Stored DOM XSS across the portal |
| Critical | SEC-03: Production deployment transmits authentication and PHI over HTTP |
| Critical | SEC-04: Production can silently accept fake payments |
| High | SEC-05: Payment intents can be replayed or applied to the wrong patient context |
| High | SEC-06: Record-share scopes are stored but not enforced |
| High | SEC-07: Department and facility authorization is caller-optional |
| High | SEC-08: Any authenticated user can administer HMS conflicts and dead letters |
| High | SEC-09: Refill approval and denial have cross-patient object authorization flaws |
| High | SEC-10: Proxy invitations are weak and not bound to the intended recipient |
| High | SEC-11: PHI scrubbing does not cover the default logging providers |
| High | SEC-12: Field encryption is registered but sensitive data remains plaintext |
| High | SEC-13: Known fallback HMS secrets and credentials are accepted |
| Medium | SEC-14: SignalR bypasses server-side session revocation checks |
| Medium | SEC-15: Form writes require only a read permission |
| Medium | SEC-16: Patients and proxies can initiate payment refunds |
| Medium | SEC-17: Client IP handling bypasses the trusted-proxy boundary |
| Medium | SEC-18: IP and location data is sent to a third party over HTTP |
| Medium | SEC-19: CI security checks are non-blocking and supply-chain references are mutable |
| Medium | SEC-20: A vulnerable OpenTelemetry dependency is present |
| Medium | SEC-21: Containers and service exposure are not hardened |
| Medium | SEC-22: Rate limiting, webhook limits, caching, and MFA controls need hardening |
| Safety | SAFE-01: Demo clinical decision-support knowledge bases are registered unconditionally |

## Scope and methodology

The assessment covered:

- ASP.NET Core startup, middleware, authentication, sessions, and filters
- Role, patient, facility, department, proxy, and break-glass authorization
- Billing, payments, refunds, and Stripe integration
- Record sharing, proxy invitations, forms, appointments, and refills
- HMS integration, webhooks, synchronization conflicts, and dead letters
- SignalR authentication and authorization
- PHI handling, logging, audit trails, encryption, and browser caching
- Static SPA rendering and content security policy
- Docker, Compose, nginx, PostgreSQL, Redis, and production defaults
- GitHub Actions and dependency configuration
- Existing automated tests

The review used source inspection, configuration analysis, targeted data-flow tracing, build output, and the complete existing test suite. It did not include dynamic penetration testing against a deployed environment, cloud/IAM review, database inspection with real data, third-party account configuration review, or a formal legal/compliance certification.

## Detailed findings

### SEC-01: Forged JWTs can become valid administrator sessions

**Severity:** Critical  
**Category:** Authentication bypass / privilege escalation  
**Relevant weaknesses:** CWE-798, CWE-287, CWE-639

#### Evidence

- The clustered production definition contains a static JWT signing key at [`docker-compose.yml:70`](../docker-compose.yml#L70) and [`docker-compose.yml:94`](../docker-compose.yml#L94).
- When a JWT contains an unknown session ID, session validation creates a new session for the claimed account at [`SessionService.cs:108`](../src/MedicalPortal.Infrastructure/Auth/SessionService.cs#L108).
- The authentication filter verifies the signed token and session but continues using the token-supplied `Role` and `PersonId` at [`AuthFilters.cs:57`](../src/MedicalPortal.Web/Filters/AuthFilters.cs#L57) and [`AuthFilters.cs:105`](../src/MedicalPortal.Web/Filters/AuthFilters.cs#L105).
- The token role is not compared with the current database account role.

#### Exploit scenario

1. An attacker authenticates as any normal user and learns their account identifier through normal application responses.
2. The attacker signs a JWT with the source-controlled cluster key, retaining their valid account ID but claiming `SYSTEM_ADMIN` and supplying a fresh `sid`.
3. `ValidateSessionAsync` finds no session and creates one for the account.
4. Subsequent authorization uses the forged role claim.

This provides direct administrative privilege escalation in deployments using the supplied key. Even after the key issue is corrected, stale role claims remain authoritative until token expiration.

#### Remediation

- Rotate the signing key in every environment where the checked-in value may have been used.
- Remove secrets from Compose and source control. Load them from a secret manager or deployment-time secret file.
- Delete the lazy-session creation path. An unknown `sid` must be rejected.
- Resolve the effective user, person, role, active state, and authorization version from trusted server-side state.
- Alternatively, compare all security-relevant claims with the database and reject mismatches.
- Invalidate existing sessions when roles, account state, MFA state, or sensitive permissions change.
- Add tests for forged roles, altered person IDs, unknown session IDs, disabled accounts, and role changes during active sessions.

### SEC-02: Stored DOM XSS across the portal

**Severity:** Critical  
**Category:** Injection / session compromise  
**Relevant weakness:** CWE-79

#### Evidence

The SPA repeatedly places API and user-controlled values into `innerHTML` without contextual encoding. Representative examples include:

- Generic toast content at [`app.js:12`](../src/MedicalPortal.Web/wwwroot/js/app.js#L12)
- Server error messages at [`app.js:417`](../src/MedicalPortal.Web/wwwroot/js/app.js#L417)
- Appointment provider and visit reason at [`app.js:545`](../src/MedicalPortal.Web/wwwroot/js/app.js#L545)
- Allergy substance, class, severity, and reaction at [`app.js:650`](../src/MedicalPortal.Web/wwwroot/js/app.js#L650)
- Administrative user names, email addresses, MRNs, memberships, and HTML attributes at [`app.js:2034`](../src/MedicalPortal.Web/wwwroot/js/app.js#L2034)

The clinical-summary HTML encoder is incomplete. Provider names and textual observation results are inserted without encoding at [`ClinicalSummaryGenerator.cs:135`](../src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs#L135) and [`ClinicalSummaryGenerator.cs:220`](../src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs#L220).

The CSP permits inline scripts at [`Program.cs:496`](../src/MedicalPortal.Web/Program.cs#L496), so injected inline event handlers are not blocked.

#### Impact

An attacker who can control a profile name, appointment reason, message, form answer, clinical text field, integration value, or error message may execute JavaScript in another user's browser. Because the payload runs same-origin, HttpOnly cookies do not prevent it from reading displayed PHI or making authenticated state-changing requests.

#### Remediation

- Replace string-built HTML with DOM nodes and assign untrusted values through `textContent`.
- Apply contextual encoding to all server-generated HTML.
- Use a reviewed sanitizer only where intentionally supported rich text is required.
- Never interpolate untrusted values into event attributes or `data-*` attributes.
- Remove `'unsafe-inline'` and deploy a nonce- or hash-based CSP.
- Self-host or apply SRI to third-party browser assets.
- Add stored-XSS regression tests for all major user and integration input paths.

### SEC-03: Production deployment transmits authentication and PHI over HTTP

**Severity:** Critical  
**Category:** Transport security  
**Relevant weakness:** CWE-319

#### Evidence

- nginx listens only on port 80 at [`nginx.conf:7`](../nginx.conf#L7).
- The application does not configure HTTPS redirection or HSTS.
- The session cookie is marked Secure only if the incoming request is already HTTPS at [`AuthController.cs:668`](../src/MedicalPortal.Web/Controllers/AuthController.cs#L668).
- The Compose proxy publishes the HTTP endpoint at [`docker-compose.yml:105`](../docker-compose.yml#L105).

#### Impact

On a network deployment matching the repository configuration, authentication cookies, patient records, messages, billing data, and administrative actions can be intercepted or modified in transit.

#### Remediation

- Terminate TLS at a controlled load balancer or reverse proxy using modern protocols and ciphers.
- Enforce HTTPS and HSTS in production.
- Mark authentication cookies `Secure` unconditionally in production.
- Reject production startup when forwarded HTTPS configuration is absent or inconsistent.
- Document the trust boundary between the edge proxy and Kestrel.

### SEC-04: Production can silently accept fake payments

**Severity:** Critical  
**Category:** Financial integrity / insecure configuration

#### Evidence

- The application selects `FakePaymentGateway` when no Stripe secret is present at [`Program.cs:296`](../src/MedicalPortal.Web/Program.cs#L296).
- The cluster marked as Production explicitly enables fake Stripe and HMS integrations at [`docker-compose.yml:65`](../docker-compose.yml#L65).
- An unknown payment intent is returned as a successful $50 payment by [`FakePaymentGateway.cs:94`](../src/MedicalPortal.Infrastructure/Payments/FakePaymentGateway.cs#L94).

#### Impact

A production instance can mark debt as paid without receiving money. An arbitrary string can be submitted as a payment intent and accepted by the fake gateway.

#### Remediation

- Prohibit fake integrations when `ASPNETCORE_ENVIRONMENT=Production`.
- Fail startup if production payment credentials, webhook secrets, or required endpoints are missing.
- Make fake providers available only through test-specific dependency injection or an explicit local-development profile.
- Add deployment tests that assert the concrete production provider types.

### SEC-05: Payment intents can be replayed or applied to the wrong patient context

**Severity:** High  
**Category:** Payment integrity / broken object binding

#### Evidence

- Existing intents are accepted based only on gateway success status at [`BillingController.cs:176`](../src/MedicalPortal.Web/Controllers/BillingController.cs#L176).
- The code does not verify patient metadata, expected amount, currency, actor, or whether the intent was previously consumed.
- New local payment rows are created at [`BillingController.cs:219`](../src/MedicalPortal.Web/Controllers/BillingController.cs#L219).
- `GatewayPaymentIntentId` and `IdempotencyKey` have non-unique indexes at [`MedicalPortalDbContext.cs:344`](../src/MedicalPortal.Infrastructure/Data/MedicalPortalDbContext.cs#L344).
- Idempotency lookup is global and not scoped to patient or operation at [`BillingController.cs:153`](../src/MedicalPortal.Web/Controllers/BillingController.cs#L153).

#### Impact

A successful intent may be submitted multiple times or applied under a different patient context if the identifier becomes known. Concurrent requests may also bypass application-level duplicate checks.

#### Remediation

- Store a unique gateway intent record before allocation and enforce a database unique constraint.
- Verify amount, currency, patient/account metadata, environment, and final gateway state.
- Scope idempotency keys to actor, patient, and operation; enforce uniqueness in the database.
- Perform validation, allocation, and persistence in one serializable transaction.
- Make webhook processing the authoritative settlement path where possible.

### SEC-06: Record-share scopes are stored but not enforced

**Severity:** High  
**Category:** Excessive data exposure

#### Evidence

- Selected share categories are serialized into `ScopeJson` at [`SharingAndFormsAndFamilyControllers.cs:97`](../src/MedicalPortal.Web/Controllers/SharingAndFormsAndFamilyControllers.cs#L97).
- The redemption body accepts categories at [`ShareRedemptionController.cs:27`](../src/MedicalPortal.Web/Controllers/ShareRedemptionController.cs#L27).
- Redemption ignores both values and returns `GetPatientEverythingBundleAsync` at [`ShareRedemptionController.cs:139`](../src/MedicalPortal.Web/Controllers/ShareRedemptionController.cs#L139).

#### Impact

A share intended for a narrow category such as medications or allergies exposes the patient's complete FHIR bundle to an anonymous code holder.

#### Remediation

- Treat stored share scope as the maximum allowed server-side scope.
- Validate requested categories as a subset of the stored scope.
- Construct a filtered FHIR bundle containing only authorized resource types.
- Deny unknown categories and test every category combination.

### SEC-07: Department and facility authorization is caller-optional

**Severity:** High  
**Category:** Broken access control

#### Evidence

- Department scope comes from an optional `x-department-id` header at [`AuthFilters.cs:161`](../src/MedicalPortal.Web/Filters/AuthFilters.cs#L161).
- The access service checks membership only when a department ID is supplied at [`AccessControlService.cs:61`](../src/MedicalPortal.Infrastructure/Services/AccessControlService.cs#L61).
- Operational roles are otherwise allowed access to any patient at [`AccessControlService.cs:83`](../src/MedicalPortal.Infrastructure/Services/AccessControlService.cs#L83).
- Staff with no memberships are treated as unscoped and allowed at [`AccessControlService.cs:169`](../src/MedicalPortal.Infrastructure/Services/AccessControlService.cs#L169).

#### Impact

Scheduling, billing, laboratory, pharmacy, department-administration, and nursing accounts can omit the header and bypass intended organizational boundaries.

#### Remediation

- Derive scope from authenticated server-side memberships, the requested resource, and patient/facility relationships.
- Require explicit enterprise-wide grants instead of interpreting missing membership as unrestricted access.
- Reject ambiguous or absent scope for scoped roles.
- Add negative tests where the department header is absent, altered, or unrelated to the target patient.

### SEC-08: Any authenticated user can administer HMS conflicts and dead letters

**Severity:** High  
**Category:** Missing function-level authorization

#### Evidence

The following endpoints require only general authentication at [`HmsWebhookController.cs:54`](../src/MedicalPortal.Web/Controllers/HmsWebhookController.cs#L54):

- List demographic conflicts
- Resolve demographic conflicts
- List integration dead letters
- Resolve integration dead letters

Conflict records contain local, HMS, and baseline demographic values at [`HmsConflictService.cs:99`](../src/MedicalPortal.Infrastructure/Hms/HmsConflictService.cs#L99), and the unfiltered query returns all pending conflicts at [`HmsConflictService.cs:212`](../src/MedicalPortal.Infrastructure/Hms/HmsConflictService.cs#L212).

#### Impact

Any patient or other authenticated account can read integration PHI and perform administrative resolution actions that may overwrite demographics or alter integration processing.

#### Remediation

- Introduce explicit HMS operations permissions.
- Restrict these endpoints to designated integration administrators.
- Apply patient-level authorization when a patient-specific view is appropriate.
- Return DTOs containing only required fields rather than persistence entities or raw payloads.
- Require reauthentication or approval for destructive conflict resolution.

### SEC-09: Refill approval and denial have cross-patient object authorization flaws

**Severity:** High  
**Category:** BOLA / IDOR

#### Evidence

The actions use `[RequireClinician]`, which authorizes access to the patient supplied in the request header, but then query refill requests solely by route ID at [`AppointmentsAndRefillsControllers.cs:468`](../src/MedicalPortal.Web/Controllers/AppointmentsAndRefillsControllers.cs#L468) and [`AppointmentsAndRefillsControllers.cs:496`](../src/MedicalPortal.Web/Controllers/AppointmentsAndRefillsControllers.cs#L496).

#### Impact

A clinician authorized for patient A can approve or deny patient B's refill if the refill UUID is obtained.

#### Remediation

- Include `Prescription.PatientId == TargetPatientId` in the database query.
- Apply provider/care-team authorization to the refill's actual patient.
- Return the same not-found response for nonexistent and unauthorized identifiers.
- Add mismatched header/object tests for every ID-based clinical mutation.

### SEC-10: Proxy invitations are weak and not bound to the intended recipient

**Severity:** High  
**Category:** Authorization token weakness

#### Evidence

- Invite codes contain four random bytes, or 32 bits, at [`SharingAndFormsAndFamilyControllers.cs:325`](../src/MedicalPortal.Web/Controllers/SharingAndFormsAndFamilyControllers.cs#L325).
- Acceptance finds the invite by code but does not compare the authenticated account with `invite.Email` at [`SharingAndFormsAndFamilyControllers.cs:373`](../src/MedicalPortal.Web/Controllers/SharingAndFormsAndFamilyControllers.cs#L373).
- The endpoint has no dedicated invite-code rate limit.

#### Impact

Any authenticated user possessing or guessing a valid code can become the grantee, regardless of the intended recipient email.

#### Remediation

- Use at least 128 bits of random entropy.
- Bind the invitation to a verified email address or authenticated account ID.
- Add per-account, per-code, and per-IP throttling.
- Accept and consume the invitation atomically.
- Notify the grantor and intended recipient when an invite is accepted.

### SEC-11: PHI scrubbing does not cover the default logging providers

**Severity:** High  
**Category:** Sensitive data exposure through logs

#### Evidence

- `AddPhiScrubber` registers an additional provider but does not remove or wrap existing providers at [`LoggingExtensions.cs:8`](../src/MedicalPortal.Infrastructure/Logging/LoggingExtensions.cs#L8).
- The same message is therefore emitted by built-in providers without scrubbing and by the custom provider with scrubbing.
- HMS conflict handling explicitly logs local, HMS, and baseline demographic values at [`HmsConflictService.cs:113`](../src/MedicalPortal.Infrastructure/Hms/HmsConflictService.cs#L113).

#### Impact

Names, dates of birth, addresses, identifiers, or other PHI may be stored in console, container, cloud, or telemetry logs even though a PHI scrubber appears to be enabled.

#### Remediation

- Do not log PHI in the first place; use internal record IDs and event codes.
- Configure an allowlist of safe structured properties.
- Clear or explicitly configure providers so every sink enforces the same policy.
- Review exception, HTTP client, tracing, and OpenTelemetry attributes for PHI.
- Add tests capturing every active provider, not only the custom sink.

### SEC-12: Field encryption is registered but sensitive data remains plaintext

**Severity:** High  
**Category:** Data protection at rest

#### Evidence

- Encryption services are registered at [`Program.cs:272`](../src/MedicalPortal.Web/Program.cs#L272).
- No production calls to the encrypt/decrypt methods or EF encryption converters were found.
- The MFA TOTP secret is assigned directly to the account entity at [`AuthController.cs:262`](../src/MedicalPortal.Web/Controllers/AuthController.cs#L262).
- The fallback encryption key is derived from the JWT secret at [`ConfigurationKeyProvider.cs:43`](../src/MedicalPortal.Infrastructure/Security/ConfigurationKeyProvider.cs#L43), violating key separation.

#### Impact

Database compromise can expose MFA seed material, demographics, clinical records, messages, integration conflict values, and dead-letter payloads. Registering an encryption service does not protect fields unless it is actually used.

#### Remediation

- Perform a data-classification exercise and identify fields requiring application-level encryption.
- Encrypt MFA secrets and especially sensitive PHI with authenticated encryption.
- Use a KMS/HSM or secret manager and separate keys by purpose.
- Support versioned keys and tested rotation/migration procedures.
- Do not derive data-encryption keys from authentication signing keys.
- This finding is a technical control gap, not by itself a formal legal HIPAA determination.

### SEC-13: Known fallback HMS secrets and credentials are accepted

**Severity:** High  
**Category:** Insecure default credentials

#### Evidence

- HMS webhook validation falls back to `demo-hms-webhook-secret` at [`HmsWebhookHandler.cs:33`](../src/MedicalPortal.Infrastructure/Hms/HmsWebhookHandler.cs#L33).
- The HMS client falls back to username `admin` and password `Admin` at [`HmsClient.cs:57`](../src/MedicalPortal.Infrastructure/Hms/HmsClient.cs#L57).
- The real HMS client defaults to an HTTP base URL at [`Program.cs:342`](../src/MedicalPortal.Web/Program.cs#L342).

#### Impact

If real HMS mode is enabled without complete configuration, an attacker may forge webhook traffic or intercept integration credentials and patient data.

#### Remediation

- Fail production startup if HMS secrets, credentials, or an HTTPS endpoint are missing.
- Remove all known fallback credentials and secrets.
- Rotate webhook secrets and support a controlled overlap window.
- Consider mutual TLS or signed service identity in addition to webhook HMAC.

### SEC-14: SignalR bypasses server-side session revocation checks

**Severity:** Medium  
**Category:** Session management

#### Evidence

The hub validates the JWT directly at [`PortalHub.cs:23`](../src/MedicalPortal.Web/Hubs/PortalHub.cs#L23) but does not call `ISessionService`, check current account status, or compare the role with server-side state.

#### Impact

A revoked or disabled token can establish or retain SignalR access until JWT expiration. Role changes may also remain stale.

#### Remediation

- Use the same authentication/session-validation pipeline for hubs and controllers.
- Revalidate sensitive hub operations.
- Disconnect active connections when sessions or accounts are revoked.

### SEC-15: Form writes require only a read permission

**Severity:** Medium  
**Category:** Incorrect authorization

#### Evidence

Form submission uses `Permission.viewRecords` at [`SharingAndFormsAndFamilyControllers.cs:228`](../src/MedicalPortal.Web/Controllers/SharingAndFormsAndFamilyControllers.cs#L228).

#### Impact

A proxy or other actor intended to have read-only record access may create or modify patient form responses.

#### Remediation

Introduce a dedicated form-submission/write permission and grant it explicitly through proxy and role policy.

### SEC-16: Patients and proxies can initiate payment refunds

**Severity:** Medium  
**Category:** Financial workflow design

#### Evidence

The refund endpoint requires the same `payBills` write permission used for payments at [`BillingController.cs:438`](../src/MedicalPortal.Web/Controllers/BillingController.cs#L438). It invokes the payment gateway refund operation directly.

#### Impact

Patients or billing proxies may trigger real refunds without a billing-review workflow, approval, or step-up authentication. Whether this is intended is a business decision, but it is outside common payment-control practice.

#### Remediation

- Separate `requestRefund` from `approveRefund`.
- Restrict gateway execution to billing staff or a controlled automated workflow.
- Require reason codes, approval thresholds, reconciliation, and strong audit records.

### SEC-17: Client IP handling bypasses the trusted-proxy boundary

**Severity:** Medium  
**Category:** Spoofed security metadata

#### Evidence

ASP.NET forwarded-header middleware has an allowlist, but `PublicIpService` independently trusts `CF-Connecting-IP`, `X-Real-IP`, `X-Forwarded-For`, and `True-Client-IP` from any request at [`PublicIpService.cs:62`](../src/MedicalPortal.Infrastructure/Services/PublicIpService.cs#L62).

#### Impact

Callers can spoof the IP used by features relying on this service, reducing the integrity of location, risk, or audit context.

#### Remediation

Use only `HttpContext.Connection.RemoteIpAddress` after trusted forwarded-header middleware processing. Provider-specific headers should be accepted only when the immediate peer is a verified provider proxy.

### SEC-18: IP and location data is sent to a third party over HTTP

**Severity:** Medium  
**Category:** Privacy and transport security

#### Evidence

`PublicIpService` calls `http://ip-api.com` for public-IP and geolocation lookup at [`PublicIpService.cs:241`](../src/MedicalPortal.Infrastructure/Services/PublicIpService.cs#L241) and [`PublicIpService.cs:321`](../src/MedicalPortal.Infrastructure/Services/PublicIpService.cs#L321).

#### Impact

IP/location queries can be observed or modified in transit, and patient/client network metadata is disclosed to an external service without an evident production privacy control.

#### Remediation

- Remove this dependency unless required.
- If required, use HTTPS and an approved privacy/contractual arrangement.
- Do not send patient IP information to third parties by default.

### SEC-19: CI security checks are non-blocking and supply-chain references are mutable

**Severity:** Medium  
**Category:** Software supply chain / secure SDLC

#### Evidence

- Trivy is referenced as mutable `aquasecurity/trivy-action@master` at [`.github/workflows/ci.yml:106`](../.github/workflows/ci.yml#L106) and [`.github/workflows/ci.yml:131`](../.github/workflows/ci.yml#L131).
- Both scans specify `exit-code: '0'` at [`.github/workflows/ci.yml:113`](../.github/workflows/ci.yml#L113) and [`.github/workflows/ci.yml:136`](../.github/workflows/ci.yml#L136), so critical/high findings do not fail CI.
- Other third-party actions are tag-pinned rather than commit-SHA pinned.

#### Remediation

- Pin actions to reviewed commit SHAs.
- Make critical/high vulnerability and secret findings block protected branches.
- Parse and enforce the result of `dotnet list package --vulnerable`.
- Add automated dependency update tooling and an exception process with expiration dates.

### SEC-20: Vulnerable OpenTelemetry dependency is present

**Severity:** Medium, conditional  
**Category:** Vulnerable dependency

#### Evidence

The build emitted `NU1902` for `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.10.0, referenced at [`MedicalPortal.Web.csproj:18`](../src/MedicalPortal.Web/MedicalPortal.Web.csproj#L18).

The package is affected by CVE-2026-42191 / GHSA-4625-4j76-fww9 when disk retry is enabled without a dedicated protected directory. Affected versions are 1.8.0 through 1.15.2; 1.15.3 is the first patched version according to the [GitHub Security Advisory](https://github.com/advisories/GHSA-4625-4j76-fww9).

#### Remediation

- Upgrade the OTLP exporter to 1.15.3 or later after compatibility testing.
- Until upgraded, do not enable disk retry in a shared temporary directory.

### SEC-21: Containers and service exposure are not hardened

**Severity:** Medium  
**Category:** Deployment hardening

#### Evidence

- The final container has no non-root `USER` at [`Dockerfile:16`](../Dockerfile#L16).
- PostgreSQL is published to the host with `postgres/postgres` credentials at [`docker-compose.yml:4`](../docker-compose.yml#L4).
- Redis is published without authentication at [`docker-compose.yml:44`](../docker-compose.yml#L44).
- Images use mutable tags rather than immutable digests.
- No read-only filesystem, capability drop, `no-new-privileges`, resource limits, or explicit internal-only networks are configured.

#### Remediation

- Run the application as a dedicated non-root user.
- Remove host port publication for PostgreSQL and Redis.
- Use generated secrets and Redis authentication/TLS where appropriate.
- Add read-only root filesystem support, writable temporary mounts, dropped capabilities, and `no-new-privileges`.
- Pin production base images by digest and establish patch/rebuild automation.

### SEC-22: Rate limiting, webhook limits, caching, and MFA controls need hardening

**Severity:** Medium  
**Category:** Defense in depth

#### Evidence and impact

- Rate limits are stored in process memory and keyed primarily by IP at [`Program.cs:219`](../src/MedicalPortal.Web/Program.cs#L219). A cluster multiplies limits by node count, while distributed attackers can rotate IPs.
- Authentication and MFA do not maintain a dedicated account/challenge attempt budget.
- Webhook endpoints read the complete request body into memory without endpoint-specific request-size limits at [`HmsWebhookController.cs:29`](../src/MedicalPortal.Web/Controllers/HmsWebhookController.cs#L29).
- No consistent `Cache-Control: no-store` policy was found for authenticated PHI responses.
- Registration and login responses disclose whether an account exists, is inactive, or is locked.

#### Remediation

- Use a distributed limiter for clustered sensitive endpoints.
- Combine IP, account, challenge, and device/risk signals for authentication and MFA.
- Apply strict request-size limits before reading webhook bodies.
- Add `Cache-Control: no-store, private` to PHI and authentication responses.
- Normalize externally visible authentication errors while preserving detailed internal audit events.

### SAFE-01: Demo clinical decision-support knowledge bases are registered unconditionally

**Severity:** High clinical-safety concern; not a conventional cybersecurity vulnerability

#### Evidence

`DemoInteractionKnowledgeBase` and `DemoAllergyKnowledgeBase` are registered for all environments at [`Program.cs:289`](../src/MedicalPortal.Web/Program.cs#L289).

#### Impact

Users may interpret incomplete demo drug-interaction and allergy results as authoritative clinical decision support.

#### Remediation

- Prevent production startup with demo knowledge bases.
- Integrate validated, licensed clinical data sources appropriate to the intended use.
- Clearly define whether the feature is informational or regulated clinical decision support.
- Establish clinical validation, update, provenance, monitoring, and safety-governance processes.

## Additional implementation observations

The following issues should also be addressed during hardening:

- The CSP includes `'unsafe-inline'`; `X-XSS-Protection` is obsolete and should not be considered an active security control.
- The SignalR browser library is loaded from a CDN without Subresource Integrity at [`index.html:9`](../src/MedicalPortal.Web/wwwroot/index.html#L9).
- `AllowedHosts` is permissive and should be restricted in deployment configuration.
- The Npgsql legacy timestamp switch is enabled globally at [`Program.cs:28`](../src/MedicalPortal.Web/Program.cs#L28), increasing the chance of inconsistent date/time semantics.
- `ValidateSessionAsync` updates and saves `LastActiveAt` on every authenticated request at [`SessionService.cs:153`](../src/MedicalPortal.Infrastructure/Auth/SessionService.cs#L153), producing avoidable database write amplification.
- Break-glass access does not require reauthentication, MFA step-up, or supervisor approval. Audit and notification controls exist, but compromised clinician sessions retain a broad emergency-access path.
- Refresh-token handling is inconsistent: the controller can read an `mp_refresh` cookie, while session setup only sets `mp_session`. This is primarily a functional/session-design defect.
- The compliance-auditor role is admitted by the general administrative filter at [`AuthFilters.cs:203`](../src/MedicalPortal.Web/Filters/AuthFilters.cs#L203). Mutation actions appear to add further checks, but the broad filter naming and data visibility should be reviewed against least privilege.
- The supplied default and clustered Compose profiles both compete for host port 5000 when launched together, and configuration sources may conflict over whether Kestrel listens on 5000 or 8080. This contributed to the observed address-in-use startup failure and should be normalized.

## Positive controls observed

The following controls provide a useful foundation and should be preserved while remediation is performed:

- BCrypt enhanced password hashing with an explicit work factor
- Password length and common-password validation
- JWT signature, issuer, audience, and expiration validation
- Random refresh tokens stored as hashes and rotated during refresh
- HttpOnly, SameSite Strict session cookies
- A custom-header CSRF requirement for cookie-authenticated mutations
- Central permission matrix and patient-access filters
- Parameterized EF Core queries; no raw SQL injection surface was identified
- Constant-time comparison for webhook signatures and record-share hashes
- Webhook event deduplication
- Expiring/revocable shares with use limits and atomic use-count updates
- CORS allowlisting instead of wildcard credentialed origins
- Trusted-network configuration for ASP.NET forwarded-header middleware
- Safe external exception responses
- Audit trails and audit-chain functionality
- Production Swagger disabled by default
- PostgreSQL required in the production environment
- Extensive automated test coverage

## Verification results

The full repository test suite was executed with:

```text
dotnet test MedicalPortal.sln --no-restore --nologo -m:1
```

Result:

```text
Passed: 259
Failed: 0
Skipped: 0
Duration: 2m 38s
```

The passing suite does not currently exercise the principal exploit paths identified by this review. Missing or insufficient negative coverage includes:

- Forged JWT roles and altered person claims
- Unknown `sid` values
- Omitted department headers
- Share-category enforcement
- Replayed or cross-patient payment intents
- HMS administrative access by patient accounts
- Refill ID/header patient mismatch
- Proxy acceptance by the wrong authenticated identity
- Stored-XSS payloads across browser rendering paths
- Session revocation for SignalR connections
- Production fail-closed behavior for fake integrations and missing secrets

## Prioritized remediation plan

### Immediate containment

1. Rotate the JWT, database, Redis, HMS, and payment-related secrets that may have been exposed or derived from repository defaults.
2. Do not expose the current cluster profile to untrusted networks.
3. Remove fake payment/HMS providers from every production deployment.
4. Require TLS and stop publishing PostgreSQL/Redis ports.
5. Restrict HMS administrative endpoints until proper authorization is implemented.

### First remediation release

1. Remove lazy session creation and bind authorization to current server-side identity.
2. Fix all DOM and server-generated HTML injection paths.
3. Enforce record-share scope.
4. Replace caller-selected department scope with server-derived scope.
5. Bind refill and payment objects to the authorized patient.
6. Enforce unique payment intent/idempotency constraints transactionally.
7. Bind proxy invitations to verified recipient identity.
8. Correct PHI logging and encrypt high-risk stored secrets and PHI.

### Hardening release

1. Unify controller and SignalR session enforcement.
2. Introduce distributed, account-aware rate limiting.
3. Harden containers, Compose networks, CI action pinning, and scan gates.
4. Upgrade vulnerable dependencies.
5. Add no-store caching policy and webhook body limits.
6. Replace demo clinical safety services with production-approved sources.
7. Add abuse-case and security regression tests for every finding.

## Retest criteria

The critical and high findings should be considered resolved only after:

- Code and deployment configuration changes have been reviewed.
- Automated negative tests demonstrate that each exploit path is rejected.
- Database constraints protect uniqueness and authorization invariants during concurrent requests.
- A production-like environment is dynamically tested through the TLS proxy.
- Logs and telemetry are inspected to confirm that PHI is absent.
- Payment and HMS integrations are validated against sandbox environments using real provider implementations.
- Existing secrets and sessions have been rotated or invalidated.

## Assessment limitations

This was a repository-level static review. It does not establish regulatory compliance, certify clinical safety, or guarantee the absence of vulnerabilities. Results may differ based on deployment infrastructure, secret management, external identity configuration, database content, cloud controls, third-party service settings, and code not present in this repository.
