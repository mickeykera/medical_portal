# ADR 0001: Target Architecture for Modular, Offline-First Hospital Platform (Ethiopia & Global Multi-Tenant)

- **Status:** Accepted / Target Architecture Baseline
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`medical_portal`)

---

## 1. Context & Business Drivers

The Springfield Health "Medical Portal" prototype is being transitioned into a modular, low-cost, open-source-based hospital platform suitable for deployment across public and private hospitals in Ethiopia and emerging health systems globally.

### Critical Operating Constraints:
1. **Offline-First LAN Execution**: Regional and rural hospitals frequently experience prolonged internet outages. The hospital LAN server must act as the primary, authoritative system of record, allowing registration, triage, clinical charting, laboratory diagnostics, dispensing, and payments to proceed with zero internet connectivity.
2. **National Health Informatics Interoperability**: Must align with Ethiopia’s national digital health architecture, including **DHIS2** (District Health Information Software 2 for national HMIS indicator reporting), **eCHIS** (electronic Community Health Information System for primary/community referrals), and the **Master Facility Registry (MFR)**.
3. **Dual Calendar System**: Ethiopia utilizes the Ethiopian (Ge'ez) calendar, consisting of 12 months of 30 days plus a 13th month (*Pagume*) of 5 or 6 days, with an 8-year/7-year offset from the Gregorian calendar and a fiscal year starting on *Hamle 1* (July 8). The platform must seamlessly bridge clinical, administrative, and statutory reporting across both calendars.
4. **Multilingual Localization**: Complete native language support in **Amharic** (*አማርኛ*, Ethiopic script) and **Afaan Oromoo** (Latin script/Qubee) alongside English, with bundled fonts for offline rendering and formal clinical review workflows.
5. **Security, Privacy & Data Protection**: Full remediation of security audit findings (SEC-01 through SEC-22, SAFE-01), zero credential leaks, cryptographic audit trails, application-level field encryption, and compliance with the Ethiopian Personal Data Protection Proclamation.

---

## 2. Target Architecture Decisions

```
+-----------------------------------------------------------------------------------+
|                                HOSPITAL LAN BORDER                                 |
|                                                                                   |
|   +-----------------------+           +---------------------------------------+   |
|   |   Static Frontend     |           |        Reverse Proxy (Nginx)          |   |
|   | (React + Vite + TS)   | <-------> |   - Same-origin routing (/api, /R4)   |   |
|   | - Bundled Ethiopic/   |           |   - Strict CSP, TLS Termination       |   |
|   |   Latin fonts         |           |   - No external DB / Redis ports      |   |
|   +-----------------------+           +-------------------+-------------------+   |
|                                                           |                       |
|                             +-----------------------------+                       |
|                             | (Internal Docker Network)                           |
|                             v                                                     |
|   +---------------------------------------------------+                           |
|   |            Hospital Portal Backend                |                           |
|   |                 (.NET 8 Web API)                  |                           |
|   |  - Patient & Proxy Auth, RBAC, Sessions (SEC-01)  |                           |
|   |  - Dual Calendar Engine (Gregorian <-> Ethiopic)  |                           |
|   |  - Multi-Tenancy (TenantId, PostgreSQL RLS)       |                           |
|   |  - Billing & FIFO Payments (SEC-04, 05, 16)       |                           |
|   |  - Interoperability (DHIS2, eCHIS, MFR Outbox)    |                           |
|   +-------------+-----------------------+-------------+                           |
|                 |                       |                                         |
|                 v (Direct SQL)          v (FHIR R4 JSON over REST only)           |
|   +--------------------------+   +--------------------------------------------+   |
|   |   Portal Database (PG)   |   |        Clinical Core EMR (Private)         |   |
|   | - Shared multi-tenant DB |   |   - OpenMRS 2.8+ / O3 on PostgreSQL        |   |
|   | - Row-Level Security     |   |   - Isolated database per tenant           |   |
|   | - Outbox queue for sync  |   |   - Authoritative clinical system of record|   |
|   | - Tenant encryption keys |   |   - Never exposed to external network      |   |
|   +--------------------------+   +--------------------------------------------+   |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### A. Repository Structure & Network Security
- **Top-Level Layout**: Restructure root into `backend/` (.NET 8 Web API, Domain, Infrastructure, Tests) and `frontend/` (TypeScript client application).
- **Reverse Proxy Architecture**: Nginx serves the compiled frontend at `/` and proxies `/api`, `/hubs`, `/R4`, and `/health` to Kestrel over an isolated internal Docker bridge.
- **Network Isolation**: PostgreSQL and Redis containers do **NOT** publish ports to the host interface. All inter-service traffic remains on private container networks.
- **Cookie & Transport Security**: Authentication cookies marked `SameSite=Strict`, `HttpOnly`, and `Secure` unconditionally. CORS closed to external origins.

### B. Clinical Core EMR Separation
- **Technology Choice**: An open-source, proven EMR platform on PostgreSQL—specifically **OpenMRS platform 2.8+ with the O3 frontend** (evaluated against Bahmni in the Phase 3 spike).
- **Strict Network & Protocol Boundary**: The portal communicates with the clinical core exclusively via **FHIR R4 JSON** and REST APIs using a dedicated service account. The portal **never** connects directly to the core's relational database.
- **Physical Isolation**: The clinical core runs on an isolated internal network and is never accessible from the public internet.

### C. Portal Ownership & Boundary Definitions
- **Portal Owns**:
  - Patient and proxy authentication, MFA enrollment, and session lifecycle.
  - Proxy delegations, record sharing scopes, and invite workflows.
  - Secure patient-provider messaging and notification delivery.
  - Patient billing, payment plans, payment gateway integration, and receipt generation.
  - Hospital tenant provisioning, branding tokens, and departmental configuration.
  - Portal access control enforcement and tamper-evident audit logging.
- **Clinical Core Owns**:
  - Inpatient and outpatient clinical charting (vitals, diagnoses, encounters, orders, clinical notes).
  - Clinical decision support (drug-drug interactions, allergy contraindications).
  - Pharmacy dispensing and lab diagnostic workflows.
- **Retirement Path**: Once Phase 8 proves clinical parity with the core, duplicate clinical tables in the portal database (`Observations`, `Prescriptions`, `Allergies`, `Medications`) and the legacy Doctor Cockpit UI will be deprecated.

### D. Multi-Tenancy Architecture
- **Tenant Definition**: A tenant represents one independent hospital or hospital network.
- **Hybrid Isolation Model**:
  - **Clinical Core**: Hard isolation (dedicated OpenMRS container instance and dedicated database per tenant).
  - **Portal Layer**: Shared database utilizing `TenantId` on every tenant-owned entity, enforced via EF Core Global Query Filters and database-level PostgreSQL **Row-Level Security (RLS)** using `current_setting('app.current_tenant_id')`. The application user cannot bypass RLS.
- **Context Binding**: Tenant resolved from the HTTP Host / sub-domain and bound server-side to the authenticated user session.
- **Key Separation**: Dedicated AES-256 field encryption keys per tenant, managed independently of authentication signing keys.

### E. Offline-First Facility Architecture
- **System of Record**: The local on-premises facility server on the hospital LAN is the primary system of record.
- **Single-Writer Invariant**: Patient charts are written to the local facility server to eliminate distributed multi-master split-brain conflicts.
- **Store-and-Forward Outbox**: Outbound data synchronization to cloud/regional aggregators uses a transactional outbox table in PostgreSQL. Sync messages are serialized as FHIR R4 Bundles and dispatched over HTTPS with idempotency keys, exponential backoff, and per-patient FIFO ordering.
- **Signed Reference Data**: Clinical terminology, drug formularies, facility directories, and DHIS2 form definitions arrive as cryptographically signed, versioned packages downloaded when connectivity is available.

### F. National Interoperability (Ethiopia)
- **DHIS2 Aggregate HMIS Integration**:
  - Routine aggregate indicator extraction (e.g., OPD visits, maternal health, immunization totals, mortality).
  - Workflow: Dry-run preview &rarr; Medical Director approval &rarr; Automated `POST /api/dataValueSets` submission with stored import summaries &rarr; Offline JSON export file fallback for air-gapped facilities.
- **eCHIS Interoperability**: Standardized FHIR International Patient Summary (IPS) and referral message exchange for community health worker referrals, gated behind a feature flag with mock sandboxes.
- **Master Facility Registry & Patient Identity**: Facility identifiers mapped to the Ethiopian Master Facility Registry. Patient identifiers modeled with facility-scoped system URIs, prepared for national Master Patient Index (MPI) deduplication.

### G. Dual Calendar Engine (Gregorian & Ethiopian)
- **Storage Rule**: Gregorian calendar **ONLY** is stored in the database (`timestamptz` in UTC, `date` for calendar dates, default operational timezone `Africa/Addis_Ababa`).
- **Input & Display Layer**: The Ethiopian calendar is strictly a presentation and input layer.
- **Deterministic Math**: Pure, mathematically proven conversion algorithms implemented in C#, SQL, and TypeScript:
  - 12 months of 30 days + *Pagume* (5 days, or 6 days in leap years).
  - Leap year condition: `(year + 1) % 4 == 0`.
  - New Year (*Enkutatash* / *Meskerem 1*): September 11 (or September 12 in the Gregorian year preceding a Gregorian leap year).
- **`calendar_dim` Table**: Database dimension table covering 1900–2200 Gregorian dates, mapping Gregorian date to Ethiopian year, month (1–13), day, Amharic/Afaan Oromoo month names, quarter, and fiscal year starting *Hamle 1*.
- **User Interface**: Clear labeling of calendar systems; dual Gregorian and Ethiopian date display on all patient cards, birth dates, prescriptions, lab results, and appointments.

### H. Multilingual Localization (Amharic & Afaan Oromoo)
- **Languages**: English (`en`), Amharic (`am`, *አማርኛ*), and Afaan Oromoo (`om`).
- **Offline Font Bundling**: Self-hosted web fonts supporting the Ethiopic script (e.g. *Noto Sans Ethiopic*) bundled directly into the container—zero runtime CDN reliance.
- **Translation Quality**: Formal review workflow requiring Ethiopian medical professionals to review and approve all translated clinical, pharmaceutical, and consent strings.

### I. Frontend Modernization & Accessibility
- **Technology**: Single-page application rewritten in **TypeScript** using **React + Vite**.
- **Security Hardening**: Elimination of all `innerHTML` and raw string HTML builders. Contextual DOM binding via React JSX. Strict Content Security Policy banning `'unsafe-inline'`.
- **Dialog Modernization**: Complete elimination of browser dialogs (`window.alert`, `confirm`, `prompt`), replaced with accessible, keyboard-trapped modal dialogs, non-intrusive toasts, and step-up confirmations.

---

## 3. Gap Analysis & Conflicts with Current Codebase

The following architectural conflicts exist between the current codebase and the target architecture:

| Area | Target Architecture Requirement | Current Code Implementation | Conflict / Remediation Phase |
|---|---|---|---|
| **Repository Layout** | `backend/` and `frontend/` top-level directories | Single flat root containing `src/MedicalPortal.Web/` with mixed embedded `wwwroot/` static files | **Conflict:** Requires Phase 1 `git mv` restructuring and Docker/CI path updates. |
| **Network Boundaries** | PostgreSQL and Redis ports unpublished; internal Docker bridge only | `docker-compose.yml:9,50` publishes `5432:5432` and `6379:6379` directly to the host | **Conflict:** Must be removed in Phase 1 & 11 to prevent unauthorized LAN database probing. |
| **Clinical System of Record** | External OpenMRS / Bahmni core via FHIR R4; portal owns only engagement/billing | Portal database contains complete clinical schema (`Observations`, `Prescriptions`, `Allergies`, `CarePlans`, `Medications`) | **Conflict:** Duplicate clinical state. Requires Phase 3 spike and Phase 8 migration to external core. |
| **Session & Role Authority** | Server-side database validation of role, account active state, and session validity | `AuthFilters.cs:57` blindly trusts JWT claims; `SessionService.cs:108` lazily creates sessions for unknown `sid` | **Conflict:** Direct privilege escalation (SEC-01). Must be contained in Phase 2. |
| **Multi-Tenancy** | Shared database with mandatory `TenantId` on all tables, EF global filters, and PostgreSQL RLS | Single-tenant database schema; no `TenantId` columns; no tenant resolution or RLS policies | **Conflict:** Fundamental data model gap. Requires Phase 5 schema migration and RLS scripts. |
| **Calendar System** | Gregorian UTC storage with dual Ethiopian presentation layer & `calendar_dim` | Pure Gregorian logic hardcoded throughout; no Ethiopian date calculation or presentation | **Conflict:** Must implement calendar math library and UI formatters in Phase 6. |
| **Localization** | Bundled Amharic (*አማርኛ*) and Afaan Oromoo (*om*) locale packages and fonts | English-only hardcoded strings in `app.js` and HTML templates; zero localization infrastructure | **Conflict:** Must build i18n localization catalog and font assets in Phase 6. |
| **Frontend Framework** | React + Vite in TypeScript; strict JSX escaping; accessible modals | Legacy vanilla JS (`app.js`, 2,400+ lines); widespread `.innerHTML` XSS sinks; `window.alert` calls | **Conflict:** Critical security vulnerability (SEC-02). Must be rewritten in Phase 7. |
| **Interoperability** | DHIS2 aggregate HMIS exporter, eCHIS referral interface, Master Facility Registry | Bespoke, legacy HMS adapter with custom webhook receivers; zero DHIS2 or eCHIS integration | **Conflict:** Requires Phase 10 national interoperability module. |
| **Decision Support** | Validated, licensed pharmacology data; production fails fast if demo rules active | `DemoInteractionKnowledgeBase` and `DemoAllergyKnowledgeBase` unconditionally registered in prod | **Conflict:** Clinical safety violation (SAFE-01). Must be restricted in Phase 2 & 8. |
| **Field Encryption** | Authenticated AES-256 field encryption for MFA secrets, demographics, and sensitive PHI | `AesGcmFieldEncryptionService` registered in DI but never invoked; TOTP secrets stored in plaintext | **Conflict:** Inactive encryption control (SEC-12). Must be implemented in Phase 11. |

---

## 4. Consequences & Migration Strategy

1. **Phased Decoupling**: Security vulnerabilities (SEC-01..SEC-22) will be mitigated prior to functional expansion.
2. **Zero Clinical Forking**: Hospital-specific customizations must remain configuration-driven (tenant metadata, enabled modules, branding tokens, regional parameters) and never result in repository forks.
3. **Preservation of Automated Tests**: The baseline of 259 passing tests must be preserved at every phase, with negative abuse-case tests added for each resolved audit finding.
