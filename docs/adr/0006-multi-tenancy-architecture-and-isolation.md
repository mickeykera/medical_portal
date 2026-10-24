# ADR 0006: Multi-Tenancy Architecture and Isolation Engine

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`)
- **Implements:** Target Architecture Rule D (Tenancy & Isolation), SEC-01..SEC-22 Safeguards, Onboarding for Adot Internal Medicine (`Adot-Hospital`)

---

## 1. Context & Business Drivers

To transition the medical portal prototype into an enterprise, multi-hospital platform configurable per hospital system across Ethiopia and international deployments without maintaining separate code forks, the architecture requires:
1. **Zero Code Forks**: Everything hospital-specific (branding, modules, hosting, integrations, security policies, schedules) must be tenant configuration stored in a schema-migrated model.
2. **Strict Multi-Tenancy & Leak Prevention**: Strong guarantees against cross-tenant data leaks across reads, inserts, and mutations.
3. **Defense-in-Depth Isolation**:
   - Application Layer: EF Core global query filters dynamically scoped to the resolved tenant.
   - Persistence Layer: PostgreSQL Row-Level Security (RLS) policies that the application role cannot bypass.
   - Physical Deployment Option: Support for dedicated database deployments for hospitals requiring on-premises hardware isolation.
4. **Cryptographic Isolation**: Deterministic per-tenant key derivation (HKDF SHA-256) ensuring encrypted fields cannot be decrypted using keys from another tenant.
5. **Platform Administration Governance**: `PLATFORM_ADMIN` role with administrative cluster-level authority (provisioning, status) but strictly zero access to Protected Health Information (PHI).
6. **API-Level Module Gating**: Modules disabled for a hospital must be rejected at the API controller action level (`403 Forbidden`), not merely hidden in the frontend SPA.

---

## 2. Decision & Architecture

### 2.1 Tenant Data Model & Domain Scoping
- **`Tenant` Entity:** Added [`Tenant`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/TenantEntities.cs) with unique index on `Slug`, configuration properties (`Region`, `TimeZone`, `LanguagesJson`, `SettingsJson`), master key fingerprint, and genesis audit block hash.
- **`TenantSettings` Model:** Typed configuration classes:
  - `TenantBrandingSettings`: `PrimaryColor`, `Logo`, `SupportEmail`.
  - `TenantModuleSettings`: `Enabled`, `Disabled` lists with `IsEnabled(string module)`.
  - `TenantHostingSettings`: `Provider`, `DataResidency`, `Isolation` (`"Shared database"` or `"Dedicated database"`).
  - `TenantIntegrationSettings`: `Ehr`, `Payments`, `Notifications`, `StaffSignIn`.
  - `TenantSecuritySettings`: `MfaStaff`, `MfaPatients`, `IdleTimeoutMinutes`, `RetentionYears`, `BreakGlass`.
- **`ITenantScoped` Interface:** Implemented across all 48 tenant-owned entities (including `UserAccount`, `Person`, `Patient`, `Appointment`, `Statement`, `AuditLog`, `WeeklyScheduleTemplate`, etc.) with default value `TenantConstants.DefaultTenantId` (`00000000-0000-0000-0000-000000000001`).

### 2.2 EF Core Global Query Filters & Isolation Enforcement
- In [`MedicalPortalDbContext.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/MedicalPortalDbContext.cs):
  - Injected `ITenantContext?`.
  - Configured global query filter `builder.Entity<T>().HasQueryFilter(e => !IsTenantResolved || e.TenantId == CurrentTenantId);` dynamically across every `ITenantScoped` entity.
  - Implemented `EnforceTenantIsolation()` in `SaveChanges()` and `SaveChangesAsync()`:
    - Automatically stamps `TenantId = CurrentTenantId` on added entities when empty.
    - Throws `System.Security.SecurityException` if a caller attempts to insert an entity with another tenant's ID (cross-tenant write prevention).
    - Throws `SecurityException` if an existing entity's `TenantId` property is modified (cross-tenant reassignment prevention).
    - Supports provisioning graphs by whitelisting entities whose `TenantId` matches a `Tenant` currently marked as `EntityState.Added`.

### 2.3 PostgreSQL Row-Level Security (RLS) Engine
- Created [`PostgresRlsHelper.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/PostgresRlsHelper.cs) defining:
  - `ALTER TABLE "<Table>" ENABLE ROW LEVEL SECURITY;`
  - `ALTER TABLE "<Table>" FORCE ROW LEVEL SECURITY;`
  - `CREATE POLICY "<Table>_tenant_isolation" ON "<Table>" FOR ALL USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);`
  - Automated application during startup migrations on PostgreSQL connections (`PostgresRlsHelper.ApplyRlsAsync(db)`).

### 2.4 Cryptographic Tenant Key Derivation
- Created [`TenantKeyProvider.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Tenancy/ITenantKeyProvider.cs) implementing HKDF (RFC 5869) SHA-256:
  - Salt: `tenantId.ToByteArray()` (16 bytes).
  - Context Info: `MedicalPortal-Tenant-Encryption-Key-{version}`.
  - Generates 256-bit AES-GCM encryption keys cryptographically separated per tenant.

### 2.5 Server-Side Tenant Resolution & Session Binding
- Implemented [`TenantResolutionMiddleware.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Middleware/TenantResolutionMiddleware.cs):
  - Resolution precedence: `X-Tenant-Slug` header -> `X-Tenant-Id` header -> `Host` header -> default tenant (`springfield`).
  - Cross-Tenant Token Binding: Validates bearer token or session cookie claims. If `claims.TenantId != tenant.Id`, rejects immediately with `403 Forbidden` (`tenant_mismatch`).
  - Inactive tenant rejection: returns `403 Forbidden` (`tenant_inactive`) if tenant is suspended.
- Enhanced [`TokenService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Auth/TokenService.cs) to encode and validate `tenantId` claim in JWT access tokens.

### 2.6 Zero-PHI Platform Admin Governance
- Added `UserRole.PLATFORM_ADMIN` in [`Enums.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Enums/Enums.cs).
- In [`PermissionMatrix.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Domain/PermissionMatrix.cs):
  - `PLATFORM_ADMIN` is assigned zero permissions to clinical, observation, diagnostic, messaging, prescription, or billing data.
  - Granted only `AppResource.AuditCompliance` (Read/Admin) for tenant provisioning and cluster operations.
- In [`AccessControlService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Services/AccessControlService.cs), `ResolveAccessAsync` and `CanAccessPatientAsync` explicitly return `AccessDecision.Deny("platform_admin_no_phi")` for `PLATFORM_ADMIN`.

### 2.7 API-Level Module Gating
- Created [`RequireModuleAttribute`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Filters/RequireModuleFilter.cs) and [`RequireModuleFilter`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Filters/RequireModuleFilter.cs).
- Inspects active `TenantSettings.Modules.IsEnabled(moduleName)`.
- If disabled, blocks execution with `403 Forbidden` and `{ error, code: "module_disabled", module }`.
- Applied across all modular controllers:
  - `BillingController` (`"billing"`)
  - `AppointmentsController` (`"appointments"`)
  - `RefillsController` (`"refills"`)
  - `MessagesController` (`"messaging"`)
  - `SharingController` (`"sharing"`)
  - `FormsController` (`"forms"`)
  - `FamilyAndSettingsController` (`"proxy"`)
  - `FhirController` (`"fhir"`)

### 2.8 WCAG 2.1 AA Contrast Verification
- Created [`WcagColorContrastCalculator.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/TenantEntities.cs) implementing relative luminance per ITU-R BT.709 and WCAG 2.1 contrast formulas.
- Validates Adot Internal Medicine primary color `#e01b24`:
  - Ratio against White: **`4.81:1`** -> **Passes** WCAG AA normal text ($\ge 4.5:1$) and large text ($\ge 3.0:1$).
  - Ratio against Black: **`4.36:1`** -> Passes large text, fails normal text.

---

## 3. Consequences

### Positive
- **Complete Tenant Isolation**: Hard barriers against cross-tenant data leaks enforced at middleware, application/ORM, and PostgreSQL engine levels.
- **Auditable Provisioning**: Tenants provisioned with deterministic cryptographic fingerprints, genesis audit blocks, and seeded organizational structures.
- **Zero Regressions**: All 300 pre-existing security, authorization, billing, and scheduling tests continue passing green, plus 11 new multi-tenancy tests.
- **Configurable Platform**: Enables seamless multi-hospital rollout in Ethiopia without branching code.

### Trade-offs & Mitigations
- Global query filters require explicit `.IgnoreQueryFilters()` when performing administrative cross-tenant maintenance or legacy backfilling.
- Dedicated database provisioning requires an explicit deployment plan (delivered in `docs/tenants/Adot-Hospital/provisioning-plan.md`).
