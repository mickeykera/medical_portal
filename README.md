# Medical Portal (.NET 8)

A comprehensive, production-grade clinical EHR and patient medical portal rewritten in **.NET 8** with **C#**, **Entity Framework Core**, **Npgsql PostgreSQL 17/16**, **ASP.NET Core Web API**, and an interactive single-page medical portal client.

---

## 🏛️ Architecture

The solution is organized using Clean Architecture principles:

```
medical_portal/
├── docker-compose.yml                  # Multi-container orchestration (Nginx, App, PostgreSQL 17, Redis 7)
├── Dockerfile                          # Multi-stage optimized .NET 8 container build
├── nginx.conf                          # Same-origin Nginx reverse proxy serving frontend & proxying backend
├── backend/                            # Server-side .NET 8 solution and automated tests
│   ├── MedicalPortal.sln
│   ├── Dockerfile
│   ├── src/
│   │   ├── MedicalPortal.Core/             # Domain entities, enums, clinical and billing logic
│   │   │   ├── Domain/
│   │   │   │   ├── AllergyLogic.cs         # Drug class cross-reactivity safety checks
│   │   │   │   ├── DrugInteractionLogic.cs # Drug-Drug Interaction (DDI) engine
│   │   │   │   ├── ClinicalSummaryGenerator.cs # Printable HTML/PDF clinical summary
│   │   │   │   ├── BillingLogic.cs         # Payment allocation, installment schedules
│   │   │   │   ├── ClinicalFormatting.cs   # Age arithmetic, ISO dates, status tone styling
│   │   │   │   ├── GeoLogic.cs             # Haversine distance, bounding box calculations
│   │   │   │   └── SchedulingLogic.cs      # Appointment conflict detection & slot generator
│   │   │   ├── Entities/                   # EF Core domain entities
│   │   │   └── Enums/                      # Role, AllergySeverity, AppointmentStatus, etc.
│   │   │
│   │   ├── MedicalPortal.Infrastructure/   # Persistence, security, and external services
│   │   │   ├── Auth/                       # BCrypt password hashing, session and JWT service
│   │   │   ├── Data/                       # MedicalPortalDbContext, DbSeeder, PostgresConnectionHelper
│   │   │   └── Services/                   # Workers, encryption, and integrations
│   │   │
│   │   └── MedicalPortal.Web/              # ASP.NET Core Web API host
│   │       ├── Controllers/                # Auth, Profile, Clinical, Appointments, Refills, Messages, Billing, FHIR
│   │       ├── Filters/                    # RequireAuth, RequirePatientAccess, RequireClinician
│   │       ├── Hubs/                       # SignalR Real-Time Hub (PortalHub)
│   │       └── Services/                   # SignalRNotificationService implementation
│   │
│   └── tests/
│       ├── MedicalPortal.Tests/            # 259 xUnit unit and integration tests
│       └── load/                           # k6 performance and load test scripts
│
├── frontend/                           # Single Page Application assets served by Nginx at /
│   ├── index.html                      # Portal single-page application entrypoint
│   ├── css/
│   │   └── app.css                     # Responsive styling and design system tokens
│   └── js/
│       └── app.js                      # Client application logic and views
│
└── docs/                               # Architecture decision records, audit logs, and runbooks
```

---

## 🚀 Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: Docker & Docker Compose (for containerized execution with PostgreSQL 17)

### Run the Application

```bash
# Build the backend solution
dotnet build backend/MedicalPortal.sln

# Run all 259 unit & integration tests
dotnet test backend/MedicalPortal.sln

# Run the backend locally (starts on http://localhost:8080)
dotnet run --project backend/src/MedicalPortal.Web

# Or run the full stack (same-origin Nginx proxy, frontend SPA, backend, Postgres) via Docker Compose:
docker compose up -d
```

Open your browser to:
- **Web Portal**: [http://localhost:5000](http://localhost:5000)
- **Interactive Swagger / OpenAPI Docs**: [http://localhost:5000/swagger](http://localhost:5000/swagger)
- **Kubernetes / Health Probes**:
  - Summary: [http://localhost:5000/health](http://localhost:5000/health)
  - Liveness: [http://localhost:5000/health/live](http://localhost:5000/health/live)
  - Readiness (DB check): [http://localhost:5000/health/ready](http://localhost:5000/health/ready)

---

## 🐘 PostgreSQL Integration

The application integrates with PostgreSQL (including the latest **PostgreSQL 17** and **16**) via **`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0**:

- **Dual Connection String Parsing**:
  - **Standard ADO.NET format**: `Host=localhost;Port=5432;Database=medical_portal;Username=postgres;Password=postgres;`
  - **Cloud URI format**: `postgres://user:password@host:5432/dbname?sslmode=require` (compatible with Supabase, Neon, Render, Railway, Heroku, Docker).
  - Automatically unescapes URL-encoded credentials (e.g. passwords containing `@` or `:`).
  - Parses query string parameters: `sslmode`, `pooling`, `timeout`, `channel_binding`, `searchpath`, `application_name`.
- **Fault-Tolerant Connection Retries**:
  - Automatic retry policy configured (`EnableRetryOnFailure(5)`) to handle cold starts and database container startup delays.
- **Timestamp Compatibility**:
  - Enables `Npgsql.EnableLegacyTimestampBehavior` alongside UTC mappings so timezone conversions are completely transparent.
- **Environment Selection**:
  - Provided via `DATABASE_URL`, `POSTGRES_CONNECTION_STRING`, or `ConnectionStrings:DefaultConnection`.
  - **Production Requirement**: In `Production` (`ASPNETCORE_ENVIRONMENT=Production`), PostgreSQL is strictly required. Startup fails if no PostgreSQL connection is provided.
  - **Development Fallback**: In `Development`, the application falls back to SQLite for local offline testing if PostgreSQL is not configured.

---

## 🗄️ Database Migrations (EF Core)

Database schema evolution is managed via **Entity Framework Core Migrations** targeting PostgreSQL 17/16, located in `src/MedicalPortal.Infrastructure/Migrations/`.

### Development vs Production Execution

- **Development (`ASPNETCORE_ENVIRONMENT=Development`)**:
  - Migrations and initial data seeding (`DbSeeder`) execute automatically when the application starts up.
  - SQLite is supported in local development and integration tests for offline testing.
- **Production (`ASPNETCORE_ENVIRONMENT=Production`)**:
  - **Automatic migrations are disabled** on normal application startup to avoid schema race conditions, deployment deadlocks, and unreviewed DDL executions across horizontal replicas.
  - A separate, dedicated migration step must be executed before starting production web containers.

### Running Migrations in Production

You have four supported options for running production migrations:

#### Option 1: Built-in Application Command (Recommended for Containers)
Run the application binary with the `--migrate` flag in a dedicated Kubernetes InitContainer, pre-deploy job, or container entrypoint:
```bash
# Apply pending migrations and exit
dotnet run --project src/MedicalPortal.Web -- --migrate

# Apply pending migrations, seed initial data if empty, and exit
dotnet run --project src/MedicalPortal.Web -- --migrate --seed
```

#### Option 2: Using the EF Core CLI Tool
```bash
dotnet dotnet-ef database update \
  --project src/MedicalPortal.Infrastructure \
  --startup-project src/MedicalPortal.Web \
  --connection "$DATABASE_URL"
```

#### Option 3: Idempotent SQL Script (Recommended for DBA Review)
Generate an idempotent SQL script that can be audited and applied using `psql` or database migration pipelines:
```bash
dotnet dotnet-ef migrations script --idempotent \
  --project src/MedicalPortal.Infrastructure \
  --startup-project src/MedicalPortal.Web \
  -o migrations.sql

# Execute script against PostgreSQL
psql "$DATABASE_URL" -f migrations.sql
```

#### Option 4: Self-Contained Migration Bundle
Build a self-contained executable bundle suitable for standalone container or runner execution without requiring the .NET SDK:
```bash
dotnet dotnet-ef migrations bundle \
  --project src/MedicalPortal.Infrastructure \
  --startup-project src/MedicalPortal.Web \
  -o efbundle

# Execute bundle against target database
./efbundle --connection "$DATABASE_URL"
```

### How to Add Future Migrations

When domain entities or EF Core configurations change, follow these steps:

1. **Update Domain Entities or DbContext**:
   - Add/modify entities in `src/MedicalPortal.Core/Entities/`.
   - Update model mappings or constraints in `src/MedicalPortal.Infrastructure/Data/MedicalPortalDbContext.cs`.
2. **Generate the Migration**:
   ```bash
   dotnet dotnet-ef migrations add <DescriptiveMigrationName> \
     --project src/MedicalPortal.Infrastructure \
     --startup-project src/MedicalPortal.Web
   ```
   *Note: Design-time migrations automatically target PostgreSQL using `MedicalPortalDbContextFactory`.*
3. **Review the Migration Code**:
   - Inspect the generated migration file in `src/MedicalPortal.Infrastructure/Migrations/` to verify operations, indexes, and constraints.
4. **Test the Migration**:
   - Run unit and integration tests: `dotnet test`.
   - Start the local dev server: `dotnet run --project src/MedicalPortal.Web`.
5. **Deploy to Production**:
   - Commit the migration files to git.
   - Run the production migration step (Option 1, 2, 3, or 4 above) during CI/CD deployment.

---

## 🌐 Public IP & Geolocation Integration

The portal includes an end-to-end Public IP resolution and geolocation subsystem:

1. **Client IP Detection Behind Proxies**:
   - Inspects headers in prioritized order: `CF-Connecting-IP`, `X-Real-IP`, `X-Forwarded-For`, `True-Client-IP`, `RemoteIpAddress`.
   - Configures ASP.NET Core `ForwardedHeadersOptions` to trust reverse proxy networks.
2. **Private & Loopback Classification**:
   - Accurately detects RFC 1918 private IPv4 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), link-local (`169.254.0.0/16`), loopback (`127.0.0.1`, `::1`), and IPv6 unique local ranges (`fc00::/7`).
3. **Public Egress IP Resolution**:
   - Resolves public egress IP via resilient multi-provider lookups (`api.ipify.org`, `icanhazip.com`, `ip-api.com`).
   - Results are cached in `IMemoryCache` (30 min TTL) with fast timeouts to protect rate limits.
4. **Geolocation Mapping**:
   - Public IPs are mapped to real-world latitude, longitude, city, region, country, and ISP, with fallback to Springfield, IL coordinates.
5. **Endpoints**:
   - `GET /api/system/ip`: Full diagnostic report of client IP, egress IP, public status, forwarded headers, and geolocation.
   - `GET /api/system/db`: Real-time report of active database provider (`PostgreSQL` or `SQLite`), connectivity status, and server time.
   - `GET /api/geo/approximate`: Returns geographic location enriched with Public IP details for client proximity calculations.

---

## 🛡️ Enterprise Security & Hardening

1. **ASP.NET Core 8 Rate Limiting**:
   - **`AuthPolicy`**: Fixed-window rate limiter (15 req/min per IP) on `/api/auth/login` and sensitive credential endpoints to prevent brute-force attacks.
   - **`ShareRedemptionPolicy`**: Fixed-window rate limiter (10 req/min per IP) on `/api/share` preventing brute-force probing of record share codes.
   - **`GeneralApiPolicy`**: Sliding-window rate limiter (150 req/min per IP) protecting clinical, billing, and FHIR endpoints.
2. **Anonymous Record Share Redemption Security**:
   - **Timing Attack Resistance**: Uses `CryptographicOperations.FixedTimeEquals` for constant-time hash verification with dummy byte comparisons on misses to eliminate side-channel timing leaks.
   - **Anti-Enumeration Generic Errors**: Returns uniform `404 Not Found` with `"Invalid or expired share code."` for non-existent, expired, revoked, and exhausted codes.
   - **Atomic Concurrency Protection**: Increments `UseCount` using database-level conditional atomic updates (`ExecuteUpdateAsync`) to prevent race condition / TOCTOU over-redemption beyond `MaxUses`.
   - **Immutable Audit Logging**: Every attempt (both failed probes and successful redemptions) is captured in `AuditLogs` with client IP, user agent, action, and timestamps.
3. **Multi-Factor Authentication (TOTP MFA)**:
   - **Policy**: Mandatory for `CLINICIAN` and `ADMIN` accounts; optional for `PATIENT` accounts.
   - **Standard Compliance**: Implements RFC 6238 TOTP with standard Base32 secrets (RFC 4648) and `otpauth://` URI generation compatible with Google Authenticator, Microsoft Authenticator, and 1Password.
   - **Emergency Access**: Generates 8 one-time recovery codes (`XXXX-XXXX`) securely hashed with SHA-256 and stored in the database. Consumed recovery codes cannot be re-used.
   - **Endpoints**: Setup (`POST /api/auth/mfa/setup`), Enrollment (`POST /api/auth/mfa/enroll`), Login Verification (`POST /api/auth/mfa/verify`), Disabling (`POST /api/auth/mfa/disable` - forbidden for clinicians/admins).
4. **Server-Side Tracked Sessions & Idle Timeout**:
   - **Session Lifecycle**: Each authenticated session is tracked in the `UserSessions` table by its unique session ID (`sid`) embedded in access tokens.
   - **Dual Token Flow**: Emits short access tokens paired with rotating cryptographically random refresh tokens (`POST /api/auth/refresh`).
   - **Idle Timeout**: Automatically revokes sessions after 15 minutes of inactivity (`SESSION_IDLE_TIMEOUT_MINUTES`).
   - **Immediate Revocation**: Active sessions are immediately invalidated on user logout (`POST /api/auth/logout`), password change (`POST /api/auth/change-password`), or account deactivation (`IsActive = false`).
5. **Cryptographic PHI Read Auditing & Tamper-Evident Hash Chain**:
   - **Central PHI Read Interception**: Global action filter (`PhiAuditFilter`) automatically captures every read of labs, vitals, medications, allergies, conditions, appointments, messages, export summaries, and FHIR R4 resources without manual controller instrumentation.
   - **Comprehensive Compliance Metadata**: Every read captures actor user account, patient ID, acting-for person ID, proxy delegation flag (`IsProxy`), resource type, remote client IP, and user agent.
   - **Append-Only DbContext Protection**: `MedicalPortalDbContext` intercepts `SaveChanges` and `SaveChangesAsync` to forbid modifications (`EntityState.Modified`) and deletions (`EntityState.Deleted`) of audit logs, throwing `InvalidOperationException`.
   - **Cryptographic Hash Chaining**: Every `AuditLog` row computes a SHA-256 hash chaining against the preceding entry's `PreviousHash` (starting from `GENESIS`), guaranteeing immediate tamper detection if any row, timestamp, or field is modified directly in the database.
   - **Zero-Latency Batch Writes**: Read operations asynchronously enqueue audit logs into an in-memory bounded channel (`IAuditBatchQueue`); background worker `AuditLogBatchWorker` periodically flushes records in atomic batches.
   - **Compliance Staff API**: Paged and filterable query (`GET /api/compliance/audit-logs`) restricted to `ADMIN` accounts, alongside an automated chain integrity verification endpoint (`GET /api/compliance/audit-logs/verify`).
6. **Break-Glass Emergency Access & High-Priority Compliance Auditing**:
   - **Clinical Justification Override**: In life-threatening emergencies, clinicians and administrators can request emergency access to any patient record via `POST /api/clinical/break-glass` (or `POST /api/clinical/emergency-override`), requiring a mandatory written justification reason (minimum 5 characters).
   - **Time-Limited 60-Minute Window**: Overrides are strictly bound to a 60-minute duration and expire automatically. Clinicians can also terminate an active session early via `POST /api/clinical/break-glass/revoke`.
   - **Access Control Integration**: Directly integrated with `AccessControlService.ResolveAccessAsync` and `ListAccessiblePatientsAsync`. When an active override exists, clinical access is permitted with decision reason `"break_glass_emergency_override"`. Responses include emergency headers (`X-Break-Glass-Session: true`, `X-Break-Glass-Expires: <ISO8601>`).
   - **Real-Time Patient & Compliance Notifications**: Whenever break-glass is invoked, real-time SignalR notifications are dispatched to the patient room (`BreakGlassAlert` to `patient_{patientId}`) and the compliance queue (`ComplianceAlert` to `compliance_queue`).
   - **High-Priority Auditing**: Creates immediate `AuditLog` records with `priority = "HIGH"` and `severity = "EMERGENCY"`. All subsequent PHI reads during the emergency session are intercepted by `PhiAuditFilter` and tagged as `PHI_READ_BREAK_GLASS` (`breakGlass = true`).
   - **Prominent UI Emergency Banner**: The client web application renders an emergency banner (`.break-glass-banner`) with a live countdown of remaining minutes and an "End Emergency Session" button.
7. **PHI Hygiene, Structured-Logging Scrubber & Exception Sanitization**:
   - **Log Leakage Prevention**: Eliminated direct patient names and clinical identifiers from background worker logs (`AppointmentReminderWorker`), enforcing pseudonymized IDs (`PatientId`).
   - **Structured-Logging Scrubber (`IPhiScrubber`)**: Global logging provider intercepts and redacts SSNs, Dates of Birth (ISO and US formats), MRNs, phone numbers, and email addresses from formatted strings and sanitizes high-risk dictionary keys (`patientNotes`, `mrn`, `addressLine1`, `body`, `ssn`, `password`, `secret`, `token`).
   - **PHI-Safe Exception Handler (`PhiSafeExceptionHandlerMiddleware`)**: Global middleware intercepts unhandled exceptions, logs sanitized error details with correlation trace IDs, and returns standard RFC 7807/500 JSON payloads (`{ error, traceId }`), completely shielding clients from database queries, stack traces, and internal clinical values.
   - **Swagger OpenAPI Hygiene**: `PhiSwaggerHygieneFilter` sanitizes schema examples with explicit synthetic placeholders (`MRN-000000`, `1970-01-01`, `Jane Doe`) and disables public Swagger UI in production by default (`Swagger:EnabledInProduction = false`).
8. **Application-Level Field Encryption (AES-256-GCM & Key Rotation)**:
   - **Authenticated Encryption**: `IFieldEncryptionService` implements AES-256-GCM authenticated encryption with 96-bit random nonces and 128-bit authentication tags.
   - **Versioned Envelope Architecture**: Ciphertexts are stored as `enc:<keyVersion>:<nonce>:<tag>:<ciphertext>`. Supports seamless key rotation via `IKeyProvider` and `RotateKey(...)` without database downtime, allowing legacy unencrypted text to pass through safely.
   - **Required Database-Level Encryption at Rest**:
     - **Storage/Volume Encryption**: AWS EBS encryption via KMS CMK / Linux LUKS/dm-crypt for PostgreSQL partitions (`/var/lib/postgresql/data`).
     - **Database TDE**: PostgreSQL Transparent Data Encryption for tablespaces, system catalogs, and temp buffers.
     - **WAL & Temp Spilling**: Write-Ahead Log (`pg_wal`) and temp file (`pgsql_tmp`) encryption.
     - **Backup Archives**: S3 SSE-KMS / GPG encryption for `pg_dump` and pgBackRest archives.
     - **In-Transit TLS**: TLS 1.3/1.2 (`sslmode=verify-full`) enforced between application and database.
9. **Security Headers Middleware**:
   - Injected on every response:
     - `Content-Security-Policy`: Restricts script, style, and font sources; enforces `frame-ancestors 'none'`.
     - `X-Frame-Options: DENY`: Clickjacking protection.
     - `X-Content-Type-Options: nosniff`: MIME-sniffing prevention.
     - `Referrer-Policy: strict-origin-when-cross-origin`.
     - `Permissions-Policy: geolocation=(self), camera=(), microphone=()`.
10. **Kubernetes Health Probes**:
   - Dedicated `/health/live` (liveness) and `/health/ready` (readiness with database connectivity check).

---

## 🩺 Clinical Intelligence & Safety

1. **Drug-Drug Interaction (DDI) Safety Engine**:
   - Detects severe pharmacological interactions before prescription issuance:
     - **Anticoagulants + NSAIDs**: Major gastrointestinal and systemic bleeding warning.
     - **ACE Inhibitors/ARBs + Potassium-sparing Diuretics**: Life-threatening hyperkalemia warning.
     - **SSRIs + Tramadol**: Serotonin syndrome warning.
     - **Metformin + Iodinated Contrast**: Lactic acidosis warning.
     - **Statins + CYP3A4 Inhibitors**: Myopathy / rhabdomyolysis warning.
2. **Allergy Cross-Reactivity Engine**:
   - Contraindication checking between proposed medications and patient allergy records (e.g. Penicillin anaphylaxis conflicting with Amoxicillin `J01CA`).
   - High-severity contraindications require explicit clinician override justification logged in `AuditLog`.
3. **Printable Clinical Visit Summary (PDF Export)**:
   - Official document generation at `/api/clinical/summary/export` formatted with `@media print` CSS, clinical header, MRN, allergies, active prescriptions, conditions, and lab trends.

---

## ⚡ Real-Time Collaboration & Background Workers

1. **SignalR WebSocket Hub (`/hubs/portal`)**:
   - **Live Appointment Queue**: Pushes real-time updates when appointments are booked or cancelled.
   - **Live Care Team Messaging**: Instant chat notifications between patients and clinicians.
   - **Refill Updates**: Instant status badge notifications when refills are approved or denied.
2. **Automated Background Workers (`IHostedService`)**:
   - **`AppointmentReminderWorker`**: Periodically scans appointments scheduled in the next 24-48 hours and dispatches automated reminder notifications.
   - **`BillingInstallmentWorker`**: Monitors payment plan installment due dates and transitions overdue installments.

---

## 🏢 Enterprise Organization & Facility Model

The system implements a multi-tier clinical organization hierarchy:
- **Organization**: Top-level healthcare system enterprise (e.g. `Springfield Health System`).
- **Facility**: Operating healthcare sites and pavilions (e.g. `Springfield Regional Medical Center`, `Riverside Outpatient Pavilion`).
- **Department**: Clinical divisions within facilities (e.g. `Family Medicine & Primary Care`, `Cardiology & Internal Medicine`, `Pediatrics & Adolescent Medicine`, `Urgent Care & Occupational Health`).
- **Provider-Department Assignments**: Many-to-many relationship tracking clinical provider affiliations across departments with primary designation (`ProviderDepartment`).
- **Department Linkages**: `Appointment` and `Location` records are linked to clinical departments (`DepartmentId`).
- **User Memberships**: `UserMembership` links user accounts to facilities and departments with operational roles (`CLINICIAN`, `ADMIN`, `PATIENT`, `VISITING_CLINICIAN`).

---

## 🛡️ Granular Roles, Permission Matrix & Department Scoping

The system implements fine-grained Role-Based Access Control (RBAC) with facility and department scope boundaries:

### 1. Extended Role Definitions (`UserRole`)
Alongside `PATIENT`, `CLINICIAN`, and `ADMIN`, `UserRole` includes specialized healthcare workforce roles:
- `SCHEDULER`: Appointment intake, booking, and schedule coordination.
- `NURSE`: Clinical care delivery, vital recording, triage notes, and message management.
- `PHYSICIAN`: Full clinical authority, prescription issuance, notes, and diagnostics.
- `BILLING`: Financial statements, payment plan management, and billing operations.
- `LAB`: Diagnostic laboratory observation and result intake.
- `PHARMACIST`: Prescription fulfillment, safety checks, and medication therapy reviews.
- `DEPT_ADMIN`: Department operations, staff oversight, and scheduling administration.
- `COMPLIANCE_AUDITOR`: Read-only compliance inspection across all records with audit chain verification.
- `SYSTEM_ADMIN`: Enterprise system configuration and technical administration.

### 2. Role-Resource-Action Permission Matrix (`PermissionMatrix`)
Permissions are evaluated at `(UserRole x AppResource x AppAction)` granularity:
- **Resources**: `Appointments`, `ClinicalNotes`, `ClinicalResults`, `Medications`, `Billing`, `Messages`, `AuditCompliance`.
- **Actions**: `Read`, `Write`, `Delete`, `Admin`.
- **Enforced Segregation of Duties**:
  - **Billing must NOT read clinical notes**: `BILLING` is strictly disallowed from `ClinicalNotes.Read`, preventing financial staff from viewing clinical visit notes and narratives.
  - **Schedulers must NOT read results**: `SCHEDULER` is strictly disallowed from `ClinicalResults.Read`, restricting schedulers to calendar and appointment records.

### 3. Facility and Department Scoping
- In `AccessControlService.ResolveAccessAsync` and action filters (`[RequirePatientAccess]`, `[RequireClinician]`, `[RequirePermission]`), staff requests are evaluated against their assigned organizational scope via `UserMembership` or `ProviderDepartment`.
- Contextual department scopes passed via `x-department-id` headers must match either direct department membership, facility-level membership, or enterprise-wide membership. Any out-of-scope request receives `403 Forbidden` (`department_scope_mismatch`).
- All 176 table-driven and integration test cases verify role matrices and department boundaries.

---

## 🎛️ Administrative Operations Console

An interactive single-page administration console accessible to administrative roles (`ADMIN`, `SYSTEM_ADMIN`, `DEPT_ADMIN`) providing full enterprise governance:
1. **User & Workforce Governance**:
   - List, search (name, email, MRN), and filter users by role and active/disabled status.
   - Create new user accounts with automated `Person` and `UserAccount` provisioning, unique MRN allocation for patients, and `Provider` records for clinicians.
   - Disable and re-enable user accounts; disabling an account immediately revokes all active sessions in `UserSessions` and blocks further authentication.
2. **Role & Department Scope Assignment**:
   - Assign granular RBAC roles (`UserRole`) with immediate effect on permission evaluation.
   - Assign and remove facility and department memberships (`UserMembership`), establishing clinical boundaries.
3. **Facility & Department Hierarchy Management**:
   - Multi-tier facility and clinical department management (name, code, specialty, address, phone).
   - Create and edit physical healthcare facilities and clinical divisions.
4. **Audit Trail & Cryptographic Chain Verification**:
   - Paged, filterable audit log viewer with resource type, action, and date filters.
   - On-demand cryptographic verification of the SHA-256 hash chain to prove audit integrity and detect tamper events.
5. **Proxy Grants & Record Shares Oversight**:
   - Monitor and manage active patient proxy delegations with the ability to revoke proxy access.
   - Oversee external record share redemption codes (SHR), tracking usage limits and expiration, with administrative revocation.
6. **Strict RBAC Authorization & Complete Auditability**:
   - Every administrative action is validated against user roles and logged with high-fidelity structured metadata (actor, target, IP address, user-agent) in `AuditLog`.

---

## 📅 Scheduling Engine & Multi-Resource Availability

The appointment scheduling subsystem has been completely rebuilt to replace hardcoded UTC business hours with an enterprise scheduling engine supporting multi-provider, multi-location, and multi-resource configurations:

1. **Per-Provider & Per-Location Weekly Schedule Templates**:
   - Weekly schedules defined via `WeeklyScheduleTemplate` (e.g. Monday-Friday 08:30-16:30 for Dr. Patel at Riverside Family Clinic; 09:00-17:00 for Dr. Kim at Northside Pediatrics).
   - Priority matching rules: Provider + Location specific template > Provider-wide template > Location-wide fallback > Standard operating window fallback.
2. **Location Time Zones & Local Slot Computation**:
   - Locations declare their IANA/Windows time zone ID (e.g. `America/Chicago`).
   - Slot candidate generation steps through operating hours in the location's local time zone and converts to UTC via `SchedulingLogic.ConvertLocalToUtc`.
   - All appointments, schedule blocks, and busy slots are stored and queried in UTC across database providers.
3. **Daylight Saving Time (DST) Resilience**:
   - **Spring Forward Transition**: Clocks advance (e.g. 02:00 -> 03:00 local). Clinics operating at 09:00 local time maintain identical 09:00 local slots while adjusting UTC offsets automatically (UTC-6 to UTC-5). Invalid times falling within the transition gap are handled gracefully without exceptions.
   - **Fall Back Transition**: Clocks fall back (e.g. 02:00 -> 01:00 local). Ambiguous repeated hours resolve cleanly to standard time offsets, preventing slot overlap or schedule distortion.
4. **Appointment Types with Dynamic Durations**:
   - Seeded and configurable types via `AppointmentType`:
     - `ROUTINE` (30 min): Standard follow-up consultations.
     - `WELLNESS` (45 min): Comprehensive annual preventative exam.
     - `NEW_PATIENT` (60 min): Initial intake and history.
     - `URGENT` (15 min): Acute illness and rapid evaluations.
   - Slot candidate step intervals and appointment durations automatically adjust based on selected type.
5. **Blocked Time & Facility Holidays**:
   - Schedule blocks (`ScheduleBlock`) model provider out-of-office, CME, vacation, and facility closures/holidays.
   - Blocked windows are filtered out during slot search and strictly enforced during appointment booking.
6. **Optional Room / Equipment Resource Assignment**:
   - Exam rooms and procedure suites (`RoomResource`) can be designated during booking (e.g. `EXAM-1`, `EXAM-2`, `PROC-1`).
   - Validates room location match and active status, preventing simultaneous bookings of the same room across different clinicians.
7. **Concurrency & Double-Booking Prevention**:
   - Booking requests are wrapped in an isolated database transaction (`IsolationLevel.Serializable`).
   - Strictly enforces zero overlaps across:
     - Provider busy appointments (`startAt < existing.EndAt && endAt > existing.StartAt`).
     - Room resource bookings.
     - Schedule blocks and holiday closures.
   - Conflicting concurrent requests are atomically rolled back with HTTP `409 Conflict`.

---

## 💳 PCI SAQ A Payment Gateway & Hosted Fields (Stripe)

The payment processing subsystem replaces hardcoded payment stubs with an enterprise, PCI SAQ A-scoped gateway architecture:

1. **Zero Cardholder Data Exposure (PCI DSS SAQ A Scope)**:
   - Primary Account Numbers (PANs), CVVs, and expiration dates never touch or transit our application servers.
   - `POST /api/billing/payment-intent` creates a Stripe PaymentIntent and returns a `client_secret` allowing the client to mount Stripe Elements (Hosted Fields) inside an isolated iframe.
   - Database stores exclusively gateway identifiers (`GatewayPaymentIntentId`, `GatewayChargeId`, `GatewayRefundId`), tokens, card brand, and actual `Last4` returned by the processor (hardcoded `4242` eliminated).
2. **Webhook Event Processing**:
   - Anonymous endpoint `POST /api/billing/webhook` handles processor callbacks.
   - **HMAC-SHA256 Signature Verification**: Parses `Stripe-Signature` (`t=...,v1=...`), validates signature against the configured webhook secret with constant-time equality checks (`CryptographicOperations.FixedTimeEquals`), and rejects payloads with expired timestamps (> 300s window) to block replay attacks.
   - **Event Idempotency**: Tracks event IDs (`evt_...`) in `ProcessedWebhookEvents` table, preventing duplicate webhook deliveries from causing double allocations or corrupted ledger states.
   - **Lifecycle Support**:
     - `payment_intent.succeeded` / `charge.succeeded`: Marks payment `SUCCEEDED` and applies FIFO allocation to open statements if not already applied.
     - `payment_intent.payment_failed` / `charge.failed`: Marks payment `FAILED` with detailed failure reason.
     - `charge.refunded` / `payment_intent.refunded`: Marks payment `REFUNDED` and restores statement balances in cents.
3. **Strict FIFO Allocation in Cents**:
   - Statements sorted by due date (`DueAt ?? CreatedAt`) ascending (oldest/overdue first).
   - Payments are allocated to the oldest open statements first down to the penny.
   - Reversing allocations (refunds) restores statement balance in cents and resets status to `OPEN` or `PARTIALLY_PAID`.
4. **Testing with FakePaymentGateway**:
   - High-fidelity `FakePaymentGateway` implementation of `IPaymentGateway` simulates hosted fields, confirmation tokens, failure modes, refunds, and authentic HMAC-SHA256 signature verification for automated test suites.
5. **Configuration-Driven Keys**:
   - All keys read strictly from environment variables or configuration (`Stripe:SecretKey`, `Stripe:PublishableKey`, `Stripe:WebhookSecret`).

---

## 📬 Multi-Channel Notification Delivery & HIPAA PHI Hygiene

The notification subsystem delivers automated, multi-channel appointment reminders and billing notices via enterprise, BAA-capable providers while strictly enforcing HIPAA Minimum Necessary and PHI transmission rules:

1. **BAA-Capable Vendor Abstractions (`INotificationSender`)**:
   - **Email Implementations**:
     - **Twilio SendGrid**: HIPAA-eligible on Pro/Premier plans with signed Business Associate Agreement (BAA).
     - **AWS SES**: HIPAA-eligible email relay under AWS BAA.
     - **Secure TLS SMTP**: Standard SMTP with STARTTLS for hospital-hosted email servers.
     - **FakeNotificationSender**: In-memory fake capturing sent emails/SMS and supporting configurable failure simulation (transient retries, permanent failure).
   - **SMS Implementations**:
     - **Twilio Programmable SMS**: HIPAA-compliant SMS delivery under Twilio healthcare BAA.
     - **AWS SNS**: BAA-eligible SMS dispatch via AWS End User Messaging.
2. **Strict HIPAA / PHI Hygiene in Communications**:
   - Unencrypted channels (standard email, SMS) must never contain Protected Health Information (PHI).
   - **Zero Clinical Details**: Messages strictly exclude reasons for visit (e.g., "Major depressive disorder evaluation"), clinical specialties (e.g., "Psychiatry", "Oncology"), medication names, and provider clinical notes.
   - **Portal Redirection**: All communications provide a generic, non-clinical reminder directing the patient to authenticate at the secure patient portal:
     - *Appointment*: `"Springfield Health System: You have an upcoming appointment scheduled on {Date} at {Time} UTC. Please sign in to your secure patient portal for details and visit preparation: https://portal.springfieldhealth.org/appointments"`
     - *Billing*: `"Springfield Health System: You have an overdue payment installment of ${Amount} due {Date}. Please review and pay securely via your patient portal: https://portal.springfieldhealth.org/billing"`
3. **Preferences & Opt-Out Enforcement**:
   - Consults `NotificationPreference` by person and category (`Appointments`, `Billing`).
   - If `Muted == true`: Dispatches nothing to third-party gateways and persists a `NotificationDeliveryRecord` with status `OPTED_OUT`.
   - If channel is disabled (`EmailEnabled == false` or `SmsEnabled == false`) or contact detail is missing, marks delivery `OPTED_OUT` without calling provider gateways.
4. **Exponential Backoff & Retries**:
   - Handles transient network drops, rate limits (HTTP 429), and gateway outages (HTTP 5xx) with automated retries up to configured max attempts (default: 3).
   - Exponential backoff delay: `InitialBackoffMs * 2^(attempt - 1)`. Non-transient errors (e.g. 400 Bad Request) fail fast without retrying.
5. **Delivery Audit Trail (`NotificationDeliveryRecord`)**:
   - Stores recipient address, category, non-clinical body, delivery status (`QUEUED`, `SENT`, `DELIVERED`, `FAILED`, `OPTED_OUT`), attempt count, error messages, external message IDs, and reference links to appointments or installments.
6. **Automated Background Workers & Controllers**:
   - **`AppointmentReminderWorker`**: Scans 48-hour upcoming appointment window, dispatches reminders via `INotificationDeliveryService`, writes audit logs, and deduplicates to prevent spam.
   - **`BillingInstallmentWorker`**: Scans overdue payment plan installments and dispatches notices.
   - **`BillingController`**: Automatically sends non-clinical payment confirmation receipts and failed transaction notices.

---

## 🏥 Hospital Management System (HMS) Integration

The application integrates bidirectionally with the institutional Hospital Management System (HMS / EMR) according to [`docs/hms-integration-contract.md`](docs/hms-integration-contract.md) and conflict rules defined in [`docs/hms-decisions.md`](docs/hms-decisions.md):

1. **Architecture & Contract Interfaces**:
   - **`IHmsClient`**: Authenticated client interface supporting token caching (8 hours), 401 unauthenticated automatic retry, and a 15-minute lockout guard to prevent triggering HMS account lockout (max 5 consecutive 401s).
   - **`FakeHmsClient`**: Test and offline implementation providing seeded EMR responses, deterministic event simulation, call logging, and failure injection.
   - **`HmsSyncService`**: Full bidirectional sync coordinator reconciling demographics, conditions, observations (labs), appointments, and prescription refills.
   - **`HmsSyncWorker` (`IHostedService`)**: Periodic background sync engine querying pending sync candidates (`HmsSynced = false` or `HmsLastSync < threshold`) with exponential backoff and dead-letter handling.
   - **`HmsWebhookHandler`**: Verifies and processes inbound thin webhook events.
   - **`HmsConflictService`**: Manages demographic discrepancies, stores audit records, and provides staff resolution workflows.
   - **`HmsDeadLetterService`**: Tracks failed sync and webhook events, audits PHI access, provides replay capabilities, and handles retention purging.

2. **Webhook Security & Verification**:
   - **Signature Scheme**: `X-Hms-Signature: t={timestamp},v1={hash}` using HMAC-SHA256 over `"{t}.{rawBody}"`.
   - **Replay Protection**: Timestamps exceeding 5 minutes (`|now - t| > 300s`) are rejected immediately.
   - **Timing Attack Resistance**: Cryptographic hashes are compared using `CryptographicOperations.FixedTimeEquals`.
   - **Secret Rotation**: Supports multiple comma-separated webhook secrets (`Hms:WebhookSecrets`), succeeding if any valid secret matches.
   - **Event Deduplication**: Incoming events are deduplicated by `EventId` via `HmsProcessedWebhookEvent` persistence.
   - **Thin Events**: Webhooks convey minimal PHI (`eventId`, `event`, `hmsMrn`, `timestamp`); complete clinical records are pulled via authenticated API requests.

3. **Demographic Conflict Resolution (3-Way Merge)**:
   - **Strict Identifier Linking**: Patients are matched exclusively by `HmsMrn` (never by name or Date of Birth).
   - **Immediate Contact Updates**: Contact fields (`Email`, `PhoneNumber`) are updated immediately from HMS.
   - **3-Way Merge on Identity & Address**: Local identity fields (`FirstName`, `LastName`, `DateOfBirth`) and `Address` are tracked against `LastSyncedDemographicsJson`.
     - If local values match the baseline (portal hasn't changed), HMS updates apply automatically.
     - If both local and HMS have diverged since last sync, an `HmsDemographicConflict` is recorded (`Status = Pending`).
     - Authorized staff resolve conflicts via `/api/hms/conflicts/{id}/resolve` choosing either `AcceptHms` or `KeepLocal`, with full audit logging.

4. **Clinical Data Reconciliation & Tombstoning**:
   - **Conditions & Labs**: Inbound HMS conditions and lab observations are reconciled against local records. Missing HMS conditions are marked resolved (tombstoned), while portal-native conditions and observations (`source = PORTAL`) are strictly preserved.
   - **Outbound Appointments & Refills**: New portal appointments and refill requests are pushed to HMS and updated with `HmsSynced = true`.

5. **Dead-Letter Handling (`HmsDeadLetterRecord`)**:
   - Persistent queue tracking failed syncs and webhook processing errors with retry counts and failure timestamps.
   - Payload JSON is protected as PHI with restricted RBAC access and audit-logged viewing and replay.
   - Retention policy automatically purges resolved and discarded records older than configured days (`Hms:DeadLetterRetentionDays`).

---

## 💊 Replaceable Clinical Safety Knowledge Bases & RxNorm

Clinical safety decision support (drug-drug interaction and allergy cross-reactivity screening) is decoupled through pluggable knowledge base interfaces:

1. **Architecture & Abstractions**:
   - **`IInteractionKnowledgeBase`**: Interface for drug interaction screening, providing version tracking (`Version`), provider identity (`ProviderName`), non-completeness statement (`Disclaimer`), and evaluation methods supporting both standardized `MedicationConcept` (with RxNorm codes) and string names.
   - **`IAllergyKnowledgeBase`**: Interface for patient allergy contraindication and cross-reactivity screening by RxNorm codes, ATC codes, substance names, and drug class taxonomies.
   - **`DrugInteractionLogic` & `AllergyLogic`**: Domain entry points delegating static calls to configurable default providers while supporting runtime swapping and dependency injection.

2. **Default Demo Implementation (Clearly Labeled Demo Data)**:
   - **`DemoInteractionKnowledgeBase`** (`demo-interactions-2024.1-sample`): Contains demonstration rules (Anticoagulants + NSAIDs, ACEI/ARB + Potassium, SSRI/SNRI + Tramadol, Metformin + Contrast, Statins + CYP3A4 inhibitors, Fluoroquinolones + Corticosteroids, Macrolides + QT prolongers) mapped with standard RxNorm CUIs.
   - **`DemoAllergyKnowledgeBase`** (`demo-allergies-2024.1-sample`): Contains demonstration cross-reactivity rules (Penicillins / Beta-lactams `J01CA`, Sulfonamides `J01EE`, Iodinated contrast) mapped with standard RxNorm CUIs (e.g. Amoxicillin RxCUI `723`, Ampicillin RxCUI `733`, Penicillin G RxCUI `7980`).
   - > [!IMPORTANT]
   - > **Clinical Scope Limitation**: The default knowledge bases contain sample rules solely for testing, development, and system demonstration. They do **NOT** claim clinical completeness or exhaustiveness and must never be relied upon for real-world clinical decision-making without integrating a certified knowledge base.

3. **Pluggable Licensed Drug Database Support (`LicensedRxNormKnowledgeBase`)**:
   - Designed for seamless integration with commercial clinical pharmacology databases (e.g. First Databank NDDF, Wolters Kluwer Medi-Span, Cerner Multum, or NLM RxNorm APIs).
   - Knowledge rules are indexed and evaluated primarily via standardized RxNorm Concept Unique Identifiers (RxCUIs).
   - Can be swapped cleanly into ASP.NET Core DI (`services.AddSingleton<IInteractionKnowledgeBase>(licensedKb)`) without modifying application or controller code.

4. **Compliance Audit Trail Tracking**:
   - Every safety check performed via `POST /api/clinical/prescriptions/safety-check` records an audit log entry (`CLINICAL_SAFETY_CHECK`) documenting:
     - `interactionKnowledgeBaseVersion`
     - `allergyKnowledgeBaseVersion`
     - `interactionKnowledgeBaseProvider` & `allergyKnowledgeBaseProvider`
     - Evaluated medication and RxNorm code
     - Non-completeness disclaimer
   - Overridden contraindications (`SAFETY_CONTRAINDICATION_OVERRIDDEN`) and issued prescriptions (`PRESCRIPTION_ISSUED`) also persist the knowledge base versions in their immutable audit envelope.

---

## 🌐 Multi-Instance Architecture & Cluster Safety

The medical portal is designed to scale horizontally across multiple stateless application instances behind a reverse proxy or load balancer while maintaining strict concurrency safety, real-time synchronization, and zero-spoofing security.

### 1. Cluster-Wide Distributed Locking (PostgreSQL Advisory Locks)
To prevent duplicate job execution across concurrent nodes, background services acquire cluster-wide distributed locks via [`IDistributedLockService`](file:///home/michael/projects/app/medical_portal/src/MedicalPortal.Infrastructure/DistributedLock/IDistributedLockService.cs):
- **Advisory Lock Mechanism**: [`PostgreSqlAdvisoryLockService`](file:///home/michael/projects/app/medical_portal/src/MedicalPortal.Infrastructure/DistributedLock/PostgreSqlAdvisoryLockService.cs) uses PostgreSQL session-level advisory locks (`SELECT pg_try_advisory_lock(@key)` and `SELECT pg_advisory_unlock(@key)`).
- **Deterministic 64-bit Lock Keys**: Resource lock names (e.g., `"AppointmentReminderWorker"`, `"BillingInstallmentWorker"`, `"HmsSyncWorker"`) are deterministically hashed via SHA-256 into 64-bit integers.
- **Fail-Safe Connection Lifecycles**: Dedicated database connections maintain lock leases during execution. If a cluster node crashes or drops connectivity, PostgreSQL automatically releases all session advisory locks immediately.
- **Mutual Exclusion & Zero Duplicates**:
  - `AppointmentReminderWorker`: Only one instance evaluates and dispatches upcoming appointment reminders; other nodes skip the iteration cleanly.
  - `BillingInstallmentWorker`: Only one instance evaluates installment schedules and issues overdue notifications; guarantees zero duplicate charges or notices.
  - `HmsSyncWorker`: Outbound and inbound HMS entity synchronization is strictly single-flight cluster-wide.
- **Development Fallback**: In offline local development and test environments where SQLite or in-memory databases are used, `PostgreSqlAdvisoryLockService` automatically uses in-memory async semaphores.

### 2. SignalR Real-Time Redis Backplane
For multi-instance deployments, real-time events (appointments, secure messages, prescription refill status updates, care plan modifications) must be broadcast to all connected clients regardless of which node holds their WebSocket connection:
- **Redis Provider**: Integrated with `Microsoft.AspNetCore.SignalR.StackExchangeRedis`.
- **Configuration**: Activated automatically when `REDIS_CONNECTION_STRING` or `Redis:ConnectionString` is configured.
- **Standalone Fallback**: Defaults to the in-memory SignalR bus for single-instance setups and testing when Redis is not provided.
- **Channel Isolation**: Uses a dedicated channel prefix (`MedicalPortal.SignalR`) to avoid cross-application channel collisions in shared Redis instances.

### 3. Rate Limiting Strategy (Per-Instance Quotas & Documentation)
ASP.NET Core's built-in rate limiter uses fast, lock-free in-memory token bucket and concurrency limiters on each instance:
- **Design Rationale**: In-memory partitioning avoids external Redis network roundtrips on every HTTP request, maximizing API throughput and avoiding a single point of rate-limiting failure.
- **Load Balancer Sizing**: Behind a standard round-robin or least-connections reverse proxy, per-instance quotas naturally scale with cluster size:
  - `RateLimiting:AuthPolicyPermitLimit` (default `10` req/min per instance): Protects authentication and login endpoints against brute-force attacks.
  - `RateLimiting:GeneralApiPermitLimit` (default `120` req/min per instance): General API traffic per client IP.
  - `RateLimiting:ShareRedemptionPolicyPermitLimit` (default `5` req/15min per instance): Protects proxy invite redemption endpoints.
- **Tunable Configuration**: All permit limits and refill rates can be configured per deployment tier without changing application binaries.

### 4. Zero-Trust Proxy Forwarded Headers
To protect against client IP spoofing and cache-poisoning vulnerabilities:
- **Strict Verification**: Wildcard proxy trusts (`KnownNetworks.Clear()` / `KnownProxies.Clear()`) have been completely removed.
- **Trusted Networks & Proxies**: `ForwardedHeadersOptions` only accepts `X-Forwarded-For` and `X-Forwarded-Proto` headers originating from explicitly configured trusted proxies or CIDR blocks via:
  - `FORWARDED_HEADERS_TRUSTED_PROXIES` (e.g. `10.0.0.5,172.16.0.2`)
  - `FORWARDED_HEADERS_TRUSTED_NETWORKS` (e.g. `172.16.0.0/12,10.0.0.0/8`)
- **Default Isolation**: When no proxies are explicitly configured, the app strictly trusts only loopback addresses (`127.0.0.0/8`, `::1/128`). Any external client attempting to send forged `X-Forwarded-For` headers will have them ignored by Kestrel.

---

## ⚙️ Configuration & Environment Variables

All sensitive values and environment-specific settings are configured via environment variables or ASP.NET Core configuration providers. Insecure fallback defaults have been eliminated from production pathways.

### Environment Variable Reference

| Variable | Required in Production | Default / Local Fallback | Description |
|---|:---:|---|---|
| `JWT_SECRET` (or `Jwt:Secret`) | **Yes** | None (Fails startup if missing or < 32 bytes) | Cryptographic secret key used to sign and verify HMAC-SHA256 JWT tokens. Must be at least 32 characters/bytes (256 bits). |
| `DATABASE_URL` | **Yes** (or connection string) | None in Prod (SQLite in Dev only) | PostgreSQL connection URI (e.g. `postgres://user:password@host:5432/medical_portal?sslmode=require`) or standard ADO.NET connection string. |
| `POSTGRES_CONNECTION_STRING` | Optional | Falls back to `DATABASE_URL` | Alternative PostgreSQL connection string setting. |
| `ConnectionStrings__DefaultConnection` | Optional | None in Prod (SQLite in Dev only) | Standard ASP.NET Core database connection string. In Production, must point to PostgreSQL. |
| `ACCESS_TOKEN_TTL_MINUTES` (or `Jwt:ExpirationMinutes`) | No | `15` | JWT access token lifespan in minutes. |
| `SESSION_IDLE_TIMEOUT_MINUTES` (or `Session:IdleTimeoutMinutes`) | No | `15` | Idle inactivity threshold in minutes before session is revoked. |
| `SESSION_REFRESH_TOKEN_DAYS` (or `Session:RefreshTokenExpirationDays`) | No | `7` | Refresh token lifespan in days. |
| `CORS_ALLOWED_ORIGINS` (or `Cors:AllowedOrigins`) | No | Same-origin only | Comma-delimited list of permitted CORS origins (e.g. `https://portal.clinic.org,https://ehr.clinic.org`). |
| `ASPNETCORE_ENVIRONMENT` | No | `Production` (containers) / `Development` (local) | ASP.NET Core hosting environment (`Development`, `Staging`, `Production`). |
| `ASPNETCORE_URLS` | No | `http://0.0.0.0:5000` | Listening HTTP/HTTPS host and port bindings for Kestrel. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | No | `false` (set `true` behind reverse proxy) | Enables parsing of `X-Forwarded-For` and `X-Forwarded-Proto` reverse proxy headers. |
| `DB_PATH` | No | `medical_portal.db` | Custom SQLite database file location when running in Development mode. |
| `FIELD_ENCRYPTION_KEY_V1` (or `FieldEncryption:Keys:v1`) | No (in Dev) / Recommended (in Prod) | Derived from seed | 256-bit (32 bytes) hex or Base64 key for AES-256-GCM application-level field encryption. |
| `FIELD_ENCRYPTION_ACTIVE_KEY_VERSION` | No | `v1` | Active key version used for encrypting new records and rotating older envelopes. |
| `STRIPE_SECRET_KEY` (or `Stripe:SecretKey`) | No (in Dev/Test) / **Yes** (in Prod) | None (Fake gateway used if unset) | Stripe secret API key (`sk_live_...` or `sk_test_...`) for server-side payment operations. |
| `STRIPE_PUBLISHABLE_KEY` (or `Stripe:PublishableKey`) | No (in Dev/Test) / **Yes** (in Prod) | None | Stripe publishable key (`pk_live_...` or `pk_test_...`) for client-side hosted fields (Stripe Elements). |
| `STRIPE_WEBHOOK_SECRET` (or `Stripe:WebhookSecret`) | No (in Dev/Test) / **Yes** (in Prod) | Test secret in test mode | Stripe signing secret (`whsec_...`) used to verify webhook HMAC-SHA256 signatures. |
| `STRIPE_USE_FAKE` (or `Stripe:UseFake`) | No | `false` | When `true`, uses in-memory `FakePaymentGateway` for tests and offline development. |
| `NOTIFICATION_EMAIL_PROVIDER` (or `Notifications:EmailProvider`) | No | `Fake` | Email vendor (`SendGrid`, `AwsSes`, `Smtp`, `Fake`). In production, choose a BAA-signed vendor. |
| `NOTIFICATION_SMS_PROVIDER` (or `Notifications:SmsProvider`) | No | `Fake` | SMS vendor (`Twilio`, `AwsSns`, `Fake`). In production, choose a BAA-signed vendor. |
| `SENDGRID_API_KEY` (or `SendGrid:ApiKey`) | Required if SendGrid | None | Twilio SendGrid API key for BAA-compliant transactional email. |
| `TWILIO_ACCOUNT_SID` (or `Twilio:AccountSid`) | Required if Twilio | None | Twilio Account SID for BAA-compliant transactional SMS. |
| `TWILIO_AUTH_TOKEN` (or `Twilio:AuthToken`) | Required if Twilio | None | Twilio Auth Token for BAA-compliant transactional SMS. |
| `PORTAL_BASE_URL` (or `Notifications:PortalBaseUrl`) | No | `https://portal.springfieldhealth.org` | Canonical base URL used in portal notification links. |
| `HMS_BASE_URL` (or `Hms:BaseUrl`) | No (in Dev/Test) / **Yes** (in Prod) | `https://hms-api.hospital.internal` | Canonical base URL for the institutional HMS REST API. |
| `HMS_USERNAME` (or `Hms:Username`) | No (in Dev/Test) / **Yes** (in Prod) | None | Service account username for HMS API authentication. |
| `HMS_PASSWORD` (or `Hms:Password`) | No (in Dev/Test) / **Yes** (in Prod) | None | Service account password for HMS API authentication. |
| `HMS_WEBHOOK_SECRETS` (or `Hms:WebhookSecrets`) | No (in Dev/Test) / **Yes** (in Prod) | Generated test secret in dev | Comma-separated HMAC-SHA256 signing secrets for HMS webhook verification and secret rotation. |
| `HMS_USE_FAKE` (or `Hms:UseFake`) | No | `false` | When `true`, uses `FakeHmsClient` with in-memory seeded records for offline testing and local development. |
| `HMS_POLL_INTERVAL_SECONDS` (or `Hms:PollIntervalSeconds`) | No | `300` | Background sync worker polling interval in seconds. |
| `HMS_DEAD_LETTER_RETENTION_DAYS` (or `Hms:DeadLetterRetentionDays`) | No | `30` | Retention duration in days before resolved/discarded dead-letter records are automatically purged. |
| `REDIS_CONNECTION_STRING` (or `Redis:ConnectionString`) | No (in single-node) / **Yes** (in multi-node) | None (In-memory SignalR bus used if unset) | Redis connection string (e.g. `redis:6379`) providing the cross-instance SignalR backplane. |
| `FORWARDED_HEADERS_TRUSTED_PROXIES` (or `ForwardedHeaders:TrustedProxies`) | No | None | Comma-delimited list of trusted reverse proxy IP addresses allowed to forward headers. |
| `FORWARDED_HEADERS_TRUSTED_NETWORKS` (or `ForwardedHeaders:TrustedNetworks`) | No | `127.0.0.0/8,::1/128` (Loopback only) | Comma-delimited list of trusted CIDR blocks allowed to forward headers (e.g. `172.16.0.0/12,10.0.0.0/8`). |
| `RateLimiting:AuthPolicyPermitLimit` | No | `10` | Maximum login/auth attempts permitted per client IP per minute per instance. |
| `RateLimiting:GeneralApiPermitLimit` | No | `120` | Maximum API requests permitted per client IP per minute per instance. |
| `RateLimiting:ShareRedemptionPolicyPermitLimit` | No | `5` | Maximum share invite token redemptions permitted per client IP per 15 minutes per instance. |
| `Swagger__EnabledInProduction` | No | `false` | When set to `true`, exposes Swagger / OpenAPI interactive documentation in Production. |

> [!IMPORTANT]
> **Production Hardening Rules**:
> 1. In `Production`, the application requires PostgreSQL and will immediately fail startup if no PostgreSQL connection string is provided. SQLite fallback is strictly blocked.
> 2. `JWT_SECRET` must be set to a strong random key (>= 32 bytes) in production. Application startup fails fast if the secret is missing, empty, or shorter than 32 bytes.
> 3. Never commit production secrets or passwords to source control or print them in logs.

---

## 🔐 Seed Accounts & Demo Credentials

| Role | Email | Password | MFA / TOTP | Description |
|------|-------|----------|------------|-------------|
| **Patient** | `dana@example.com` | `ChangeMe!Demo2024` | Optional | Dana Rivera (Managing self + proxy access for children Mateo and Sofia) |
| **Clinician** | `apatel@clinic.example` | `ChangeMe!Demo2024` | Required (`./scripts/dev-totp.sh`) | Dr. Amara Patel, MD (Family Medicine, full clinical dashboard access) |
| **Admin** | `admin@clinic.example` | `ChangeMe!Demo2024` | Required (`./scripts/dev-totp.sh`) | Hospital Administrator (Addis Central General Hospital admin dashboard) |

> [!NOTE]
> Run `./scripts/dev-setup.sh` once to generate a gitignored `.env` file with random `JWT_SECRET`, `HMS_WEBHOOK_SECRET`, and `DEV_TOTP_SECRET`.
> The seeder automatically associates `DEV_TOTP_SECRET` with the seeded Clinician (`apatel@clinic.example`) and Admin (`admin@clinic.example`) accounts when running in Development.
> Run `./scripts/dev-totp.sh` to read `DEV_TOTP_SECRET` from `.env` and output the current 6-digit TOTP code and seconds remaining (using `oathtool` or Python 3). Production startup guards strictly reject any `DEV_TOTP_SECRET` and dev accounts.

---

## 🐳 Docker Compose Deployment

### 1. Standalone Single-Instance (Default)
To run the standard standalone setup with **PostgreSQL 17 Alpine** in Docker:

```bash
docker compose up -d
```
Access the portal at `http://localhost:5000`.

### 2. Clustered Multi-Instance Profile (High Availability & Scale)
To spin up the multi-instance production topology consisting of:
- **`postgres`**: PostgreSQL 17 Alpine database with advisory locking support
- **`redis`**: Redis 7 Alpine SignalR backplane
- **`medical-portal-1` & `medical-portal-2`**: 2 clustered application instances running in Production mode with distributed locks enabled
- **`proxy`**: Nginx reverse proxy distributing load across both nodes on port `5000` with WebSocket upgrades and trusted forwarded headers

```bash
# Launch the clustered multi-instance environment
docker compose --profile cluster up -d

# Check running cluster services
docker compose --profile cluster ps

# Follow logs across both application nodes
docker compose --profile cluster logs -f medical-portal-1 medical-portal-2
```

### 3. Cluster Verification & Concurrency Guarantees
- **No Duplicate Reminders or Charges**: Both `medical-portal-1` and `medical-portal-2` run background timer ticks, but only the node that wins the PostgreSQL advisory lock (`pg_try_advisory_lock`) executes the batch. The other node logs `[DistributedLock] Could not acquire lock for ... - another instance is currently running this task.` and yields immediately.
- **SignalR Real-Time Fan-Out**: WebSockets connect via Nginx to either node; messages dispatched on Node 1 publish to Redis channel `MedicalPortal.SignalR` and reach connected clients on Node 2.
- **Trusted Forwarding**: The Nginx proxy forwards `X-Forwarded-For` and `X-Forwarded-Proto`, which the app instances accept because the Docker bridge network (`172.16.0.0/12`) is explicitly whitelisted in `FORWARDED_HEADERS_TRUSTED_NETWORKS`. Direct forged headers from untrusted networks are rejected.

---

## 📊 Observability, OpenTelemetry & Dependency Health Checks

### 1. OpenTelemetry Distributed Tracing & Metrics
The application integrates native OpenTelemetry instrumentation for enterprise observability:
- **Tracing (`MedicalPortal.Tracing`)**: Instruments HTTP requests, external client calls, and domain activities (`portal.appointment.book`, `portal.billing.payment`, `portal.clinical.safety_check`, `portal.messages.send`).
- **Metrics (`MedicalPortal.Metrics`)**: Exposes key health and operational meters (`medical_portal.logins.total`, `medical_portal.appointments.booked.total`, `medical_portal.payments.processed_cents`, `medical_portal.clinical.safety_checks.total`, `medical_portal.hms.sync_duration_ms`).
- **OTLP Exporter**: Activated via `OTEL_EXPORTER_OTLP_ENDPOINT` (e.g., pointing to Jaeger, OpenTelemetry Collector, Grafana Tempo, or Datadog).
- **Structured PHI Scrubbing**: All log messages and tracing spans are scrubbed by `PhiScrubber` to guarantee patient names, SSNs, and clinical secrets are never logged.

### 2. Dependency Health Probes
- **Comprehensive Dependency Diagnostic**: `GET /health` returns JSON reporting operational status, latency, and diagnostics for:
  - `database`: PostgreSQL / SQLite connection and query test
  - `redis`: Redis cluster backplane connectivity and ping latency
  - `payment_gateway`: Stripe API credential validation and gateway readiness
  - `hms`: Institutional EHR sync client configuration
  - `notifications`: BAA transactional email and SMS provider status
- **Kubernetes Liveness Probe**: `GET /health/live` (HTTP 200 when app process is healthy)
- **Kubernetes Readiness Probe**: `GET /health/ready` (HTTP 200 when critical database dependencies are available)

---

## ⚡ Load Testing with k6

A complete production k6 load test script is provided in [`tests/load/k6-portal-flow.js`](file:///home/michael/projects/app/medical_portal/tests/load/k6-portal-flow.js), modeling realistic user flows:
1. **User Authentication**: Authenticates with demo patient and clinician accounts, validating JWT session generation.
2. **Appointment Scheduling**: Queries available slots and submits booking requests under high concurrency.
3. **Secure Clinical Messaging**: Retrieves conversation threads and dispatches secure clinical communications.

### Running the Load Test
```bash
# Execute standard load test against local or clustered environment
k6 run tests/load/k6-portal-flow.js

# Custom target host or duration
BASE_URL="http://localhost:5000" k6 run tests/load/k6-portal-flow.js
```

### Performance & Reliability SLA Thresholds
- `http_req_duration`: 95th percentile < 500ms; 99th percentile < 1000ms.
- `http_req_failed`: Total error rate < 1%.
- `portal_transaction_success_rate`: > 99%.

---

## 🚀 CI/CD Pipeline (GitHub Actions)

A comprehensive automated quality and security pipeline is defined in [`.github/workflows/ci.yml`](file:///home/michael/projects/app/medical_portal/.github/workflows/ci.yml):
1. **Build & Automated Tests**: Compiles on .NET 8, executes all 259 unit and integration tests with services (PostgreSQL 17, Redis 7), and collects XPlat code coverage.
2. **Dependency Vulnerability Scanning**: Audits transitive NuGet dependencies using `dotnet list package --vulnerable` and Trivy filesystem vulnerability analysis.
3. **Container Security Scanning**: Builds the production Docker container and scans the resulting image for CVEs with Trivy.
4. **Secret Scanning**: Runs Gitleaks to enforce zero credential or private key leaks in git history.

---

## 📖 Operational Runbook (Backup, Restore & Rollback)

For detailed operational procedures, disaster recovery, and emergency protocols, see the [Production Runbook](file:///home/michael/projects/app/medical_portal/docs/runbook.md):
- **Backup Procedures**: PostgreSQL full dumps (`pg_dump -Fc`), AES-256 client-side encryption, and continuous WAL archiving for Point-In-Time Recovery (PITR).
- **Restore Procedures**: Cold dump restore and second-by-second PITR target recovery.
- **Rollback Procedures**: Zero-downtime rolling container updates, EF Core down-migration scripting, and emergency circuit-breaker toggles.
