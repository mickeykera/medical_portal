# ADR 0013: Productization, Tenant Configuration Schema, and Onboarding

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `scripts/`, `docs/tenants/Adot-Hospital/`)
- **Implements:** Phase 12 Productization, Tenant Configuration Schema, API Module Gating, Adot Internal Medicine Dedicated On-Premises Onboarding

---

## 1. Context & Business Drivers

With the core hospital platform foundations in place—including multi-tenancy (Phase 5), Ethiopian dual calendar and localization (Phase 6), frontend hardening (Phase 7), OpenMRS FHIR sync (Phase 8), offline LAN outbox queueing (Phase 9), national DHIS2/eCHIS interoperability (Phase 10), and authenticated field encryption and deployment hardening (Phase 11)—the platform requires productization and operational readiness:

1. **Formal Tenant Configuration Schema & Validation**: Prevent corrupted or unserviceable tenant configurations at onboarding time. Validate brand color accessibility against WCAG 2.1 AA standards, language availability (`en`, `am`, `om`), valid timezones, and valid system modules before database persistence.
2. **Complete & Hierarchical API-Level Module Gating**: Ensure that when a feature module or category (such as `"findcare"`, `"dhis2"`, `"echis"`, `"mfr"`, `"mpi"`, or the parent `"interop"`) is disabled in a hospital's tenant settings, all corresponding backend API endpoints return HTTP `403 Forbidden` (`module_disabled`), leaving zero unmanaged surface.
3. **Dynamic Tenant Management Without Downtime**: Empower platform administrators to update branding tokens, adjust timeouts, and toggle feature modules dynamically via authenticated administrative APIs with immediate effect across active sessions.
4. **Turnkey On-Premises Deployment for Adot Internal Medicine**: Deliver an end-to-end dedicated database container orchestration package (`docker-compose.adot.yml`, `scripts/init-adot-db.sql`, `scripts/provision-tenant.sh`, `.env.adot.example`) and operational procedures (`runbook.md`) for hospital IT staff.

---

## 2. Decision & Architecture

### 2.1 Canonical Tenant Module Registry & Hierarchical Gating
We define 14 canonical system modules in [`TenantModules`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/TenantEntities.cs):
- Clinical & Core: `appointments`, `messaging`, `billing`, `refills`, `proxy`, `sharing`, `fhir`, `findcare`, `forms`
- National Interoperability: `interop`, `dhis2`, `echis`, `mfr`, `mpi`

In [`TenantModuleSettings.IsEnabled(moduleName)`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/TenantEntities.cs), hierarchical module resolution is enforced:
1. If the parent `"interop"` module is disabled, all interop submodules (`dhis2`, `echis`, `mfr`, `mpi`) are automatically rejected.
2. If individual submodules are explicitly disabled in `Disabled`, they are blocked even if parent `"interop"` is enabled.
3. Decorated all endpoints with `[RequireModule]`:
   - `CareController` (`findcare`)
   - `Dhis2Controller` (`dhis2`)
   - `EchisController` (`echis`)
   - `MfrController` (`mfr`)
   - `MpiController` (`mpi`)

### 2.2 Formal Tenant Configuration Validator
Implemented [`TenantConfigurationValidator.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Domain/TenantConfigurationValidator.cs) validating:
- **Slug Integrity**: Regex `^[a-zA-Z0-9_-]+$`, 2–60 characters.
- **Language Support**: Must contain at least one language; rejects unsupported language codes outside `en`, `am`, `om`.
- **WCAG 2.1 AA Accessibility Analysis**: Computes ITU-R BT.709 relative luminance. Rejects primary colors failing minimum 3.0:1 contrast ratio against white. Emits clear warnings if large text passes ($\ge 3.0:1$) but normal text fails ($< 4.5:1$).
- **Module Disjointness**: Asserts that `Enabled` and `Disabled` module lists do not contain overlapping modules.
- **Hosting Isolation**: Enforces valid values (`Shared database` or `Dedicated database`).
- **Security Bounds**: Validates idle timeout (5–120 minutes) and record retention ($\ge 1$ year, recommending $\ge 10$ years for Ethiopian health regulations).

### 2.3 Dynamic Tenant Administration Endpoints
In [`TenantController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/TenantController.cs), added administrative endpoints restricted strictly to `PLATFORM_ADMIN` and `SYSTEM_ADMIN`:
- `POST /api/platform/tenants/validate`: Dry-run validation of proposed configuration without persistence.
- `GET /api/platform/tenants/{id}`: Detailed tenant profile inspection.
- `PUT /api/platform/tenants/{id}/settings`: Comprehensive settings update with schema and WCAG re-validation.
- `PATCH /api/platform/tenants/{id}/modules`: Atomic module toggle (`{ module, enabled }`) with instantaneous effect.
- `GET /api/platform/tenants/{id}/export-config`: Clean portable JSON export for facility configuration backup.

### 2.4 Dedicated Deployment Package for Adot Internal Medicine
- Created [`docker-compose.adot.yml`](file:///home/michael/projects/app/medical_portal/docker-compose.adot.yml) modeling physical database segregation on hospital LAN (`adot-postgres` with `adot_portal_db` and `adot_openmrs_db`, `adot-redis`, `adot-backend`, `adot-proxy`).
- Authored [`scripts/init-adot-db.sql`](file:///home/michael/projects/app/medical_portal/scripts/init-adot-db.sql) configuring dedicated service accounts `adot_portal_app` and `adot_openmrs_app`, revoking public permissions.
- Authored [`scripts/provision-tenant.sh`](file:///home/michael/projects/app/medical_portal/scripts/provision-tenant.sh) providing turnkey CLI automation with pre-flight schema validation.
- Authored [`docs/tenants/Adot-Hospital/runbook.md`](file:///home/michael/projects/app/medical_portal/docs/tenants/Adot-Hospital/runbook.md) with comprehensive operations, backup, and failover instructions.

---

## 3. Consequences

### Positive
- **Guaranteed Configuration Integrity**: Erroneous, unserviceable, or inaccessible branding configurations cannot be introduced.
- **Complete Module Gating**: Disabling a module or national interop stack leaves zero unprotected endpoints.
- **Operational Autonomy**: Hospital IT can deploy and operate the on-premises platform independently using structured runbooks and automated scripts.
- **Zero-PHI Safety**: Platform administrative capabilities remain completely isolated from clinical patient records.

### Trade-offs & Mitigations
- Dedicated on-premises database instances require independent database maintenance and backups; mitigated by providing [`scripts/facility-backup.sh`](file:///home/michael/projects/app/medical_portal/scripts/facility-backup.sh) and automated daily cron configuration in the runbook.
