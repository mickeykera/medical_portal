# Medical Portal ↔ HMS Integration Contract

**Status:** contract **specified** over the real HMS codebase and a live probe.

This document records exactly how the Medical Portal will talk to the HMS: the
actual endpoints, the authentication story, the failure and idempotency model,
and the entity mapping that a future HMS adapter
(`apps/api/src/integrations/hms/`) must implement.

The architecture it plugs into is `docs/hms-integration-boundary.md`. Nothing
here changes that boundary: the portal stays independent and source-agnostic,
HMS rows imported into the portal are `source = 'HMS'`, and the HMS is reached
only through the adapter. This document is about **how**, not **whether**.

Nothing in the current phase is implemented. This contract is the specification
an HMS client, worker, scheduler, webhook, and outbound queue will be built
from in a later phase.

---

## 0. How this contract was established (evidence)

The HMS application lives at `/home/michael/projects/app/hospital_management`
(a separate repo). It is an Express + SQLite application (Node `node:sqlite`
`DatabaseSync`, schema in `src/config/schema.sql`, migrations in
`src/config/migrations/*.sql`).

The contract was built from two things:

1. **Code inspection** of `src/server.js`, every module router under
   `src/modules/*/routes.js`, the schema, the models layer, and the middleware.
2. **A live probe**: a throwaway demo instance was booted on port `3210` with
   `DEPLOYMENT_MODE=demo SEED_DEMO_DATA=true` and a database + upload dir under
   `/tmp/opencode/hms-contract/`. One synthetic patient, one visit, one signed
   consultation, one prescription, one lab order, one lab result, and one
   appointment were created through the real API to capture exact request /
   response DTOs, error shapes, and the login flow. **The real HMS repo and its
   `hospital.db` were never touched.** The probe instance and its DB are
   disposable and are removed after this document is written.

Reproduce the probe (any throwaway directory):

```bash
cd /home/michael/projects/app/hospital_management
rm -rf /tmp/hms-probe && mkdir -p /tmp/hms-probe/uploads
DEPLOYMENT_MODE=demo SEED_DEMO_DATA=true \
  PORT=3210 DB_PATH=/tmp/hms-probe/hms.db \
  JWT_SECRET=probe-secret UPLOAD_DIR=/tmp/hms-probe/uploads \
  node src/server.js
# then:
curl -s -X POST http://127.0.0.1:3210/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin"}'
```

Demo credentials are `username` / Capitalised-`Username` **only outside
`NODE_ENV=production`**; in production the seeder refuses predictable passwords
and requires `SEED_DEMO_PASSWORD`.

---

## 1. Reference boundary (do not re-litigate)

From `docs/hms-integration-boundary.md`, the invariants this contract operates
inside:

- The portal owns its own Postgres. The HMS grants nothing to the portal; the
  portal borrows nothing from the HMS.
- Imported rows are `source = 'HMS'` and never reconciled onto portal-native
  rows. Portal-native rows are `source = 'PORTAL'` and never pushed unless the
  write-back decision at §10 says otherwise (it currently says no).
- The HMS is never the portal's source of truth; the portal is its own.
- Portal PKs are portal-owned `cuid`. HMS identifiers live in the nullable,
  unique `hmsId` / `hmsPatientId` columns described in §7.
- The integration lives entirely under `apps/api/src/integrations/hms/` — a
  client, DTO types, auth handling, external-id registry, mappers, and sync
  logic — plus the `hmsFetchedAt` / `source` columns already on the schema.

---

## 2. HMS deployment profile & networking

Facts gathered from the HMS repo (`src/server.js`, `docker-compose.yml`,
`render.yaml`, `.env`):

| Fact | Value |
|---|---|
| Runtime | Node (ESM), Express 5, SQLite via `node:sqlite` |
| Listener | `PORT` env, default `3000`, no path prefix |
| API root | `/api/*`; Swagger UI at `/api-docs` |
| Database file | `DB_PATH` env, default `<repo>/hospital.db`; single SQLite file |
| Schema | `src/config/schema.sql` run at import + column backfills + idempotent `runMigrations()` on boot |
| Public routes | `GET /api/health`, `POST/GET /api/setup-demo` (demo only), `POST /api/setup/bootstrap-admin` (non-demo, first-boot only), `POST /api/auth/login`, `POST /api/auth/change-password`, `POST /api/auth/demo-login` (demo + flag), `GET /api/config/public` |
| Everything else | behind `authenticate` (JWT) at `src/server.js:453` |
| CORS | env `CORS_ORIGINS` allow-list. **Irrelevant for server-to-server** (no browser Origin is sent); the portal adapter needs no CORS consideration |
| Trust proxy | `TRUST_PROXY` resolved at boot; already deployment-correct on-prem (unset ⇒ peer IP). The portal's calls simply appear as the portal's own IP in audit/rate-limit records |

**Network contract for the portal:** one base URL — either the on-prem host
`http://<hms-host>:3000` on the hospital LAN, or the HTTPS front (Caddy/nginx)
in front of it — plus credentials (§3). Nothing else about the HMS network
profile matters to the portal.

---

## 3. Authentication & the service principal

The HMS has **no API keys, no refresh tokens, no long-lived machine tokens, and
no OAuth**. The only credential is a username/password that mints a short-lived
JWT.

**Login** — `POST /api/auth/login` (body `{ username, password }`, zod
`validate('login')`):

- Success `200`: `{ token, user: { id, username, full_name, role,
  department, must_change_password, permissions: [] } }`.
- JWT payload `{ id, username, role }`, signed with `JWT_SECRET`, expiry
  **`8h`** (`expiresIn: '8h'`, verified live: `exp` claim ≈ now + 8h). Issuer
  is `jsonwebtoken`, HS256.
- Every failure — unknown user, wrong password, or locked account — returns the
  identical `401` body: `{"error":"Invalid credentials","code":"INVALID_CREDENTIALS","details":null}`.
- **Lockout:** 5 consecutive failures lock the account for **15 minutes**
  (`LOCKOUT_THRESHOLD = 5`, `LOCKOUT_WINDOW_MS = 15*60*1000`, persisted in
  SQLite). Only SuperAdmin can clear it. A service account that fails login
  repeatedly is thereby frozen out — the adapter must treat 401s as actionable
  and stop retrying credentials immediately.

**Token verification** — global `authenticate` middleware after line 453
(public routes above). Failure responses have *no* `code`:

- No header: `401 {"error":"Missing authorization header"}`
- Bad/expired token: `401 {"error":"Invalid or expired token"}`

**Role authorization** — `authorize([...roles])` after `authenticate`. Failure:
`403 {"error":"Role '<X>' not authorized. Required: <Y>"}` (verified live).

**Forced password change** — a user with `must_change_password = 1` cannot reach
*any* module endpoint until they change their password via
`POST /api/auth/change-password`. The service account must therefore be
provisioned with this flag clear.

### Service principal required by the portal

The portal needs one dedicated HMS account with:

- `role = 'Admin'` (the only role with unscoped read access to every per-patient
  clinical endpoint the portal needs; `Doctor` is usable but semantically odd
  for a machine, and `Patient` is strictly self-scoped — verified:
  `GET /api/patient/me` as Admin returns 403).
- `active = 1`, `must_change_password = 0`.
- **No shared human use** (and hence no login-lockout exposure from humans).

Provisioning is an **operational prerequisite**, not part of this phase: create
via the HMS admin UI or `bootstrapAdmin`/admin routes, store username+password
in portal secrets, never in code or the portal DB.

### Token lifecycle

- `8h` expiry ⇒ the adapter must either hold the token in memory and re-login
  before expiry, or simply re-login on a cadence (< 8h, e.g. hourly) and on
  every 401. There is no refresh path; a mid-flight 401 is the trigger to
  re-authenticate and retry once.
- Keep the portal node's clock NTP-synced; `exp` is a unix second and `verify`
  compares against local time.
- The portal never stores or learns the HMS `JWT_SECRET`; it only exchanges
  bearer tokens.

---

## 4. API inventory — READ

Everything the portal needs to populate the five `source = 'HMS'` clinical
entities is a **per-patient** read keyed by the HMS integer `patient_id`. HMS
patient records are addressed by integer id everywhere (`/clinical/:patientId`,
`/lab/patient/:patientId`); the *business* key `global_id` is used only for
search/identity (§7).

### Patient demographics

| Endpoint | Roles | Returns |
|---|---|---|
| `GET /api/clinical/:patientId/emr` | Doctor, Admin | `{ patient, medical_history[], consultations[], prescriptions[] }` — the aggregate the adapter will use |
| `GET /api/clinical/:patientId` | Doctor, Nurse, Admin, LabTech | `{ patient, medical_history[], consultations[] }` |
| `GET /api/reception/search?q=` | (authed, role-guarded) | `{ patients[] }` — full patient rows (verified live: search by `global_id`) |
| `GET /api/clinical/doctors` | Receptionist, Admin, Doctor, Nurse | `{ doctors[] }` (id, username, full_name, role, department) |

`patient` row columns (verified live): `id, global_id, first_name, last_name,
date_of_birth, gender (Male|Female|Other|PreferNotToSay), blood_type, email,
phone, address, emergency_contact_name, emergency_contact_phone,
insurance_provider, insurance_id, insurance_validity, medical_history_summary,
created_at, updated_at`.

### Condition (portal `Condition`, HMS `medical_history`)

| Endpoint | Roles | Shape |
|---|---|---|
| `GET /api/clinical/:patientId` and `…/emr` | above | `medical_history[]` rows incl. `id, patient_id, condition, diagnosis_date, treatment, status, notes, created_at` |

**Note:** `POST /api/clinical/:patientId/history` is currently broken unless
every optional field is supplied — see the defects list §13. Reads are fine.

### Consultation / SOAP note (portal *mapper to* `Condition`/interaction context)

| Endpoint | Roles | Shape |
|---|---|---|
| `GET /api/clinical/:patientId/emr` | above | `consultations[]` incl. `id, visit_id, patient_id, doctor_id, chief_complaint, diagnosis, treatment_plan, notes, subjective, objective, assessment, plan, status (Draft|Signed), signed_at, updated_at, consultation_date` |

Portal's `Condition` mapping target is `medical_history` (a clean condition
list). `consultations.diagnosis` is a free-text note, signed at
`signed_at` — worth importing only if a Condition row is also wanted for
encounter diagnoses. Default mapping (§8) uses `medical_history` only.

### Prescription (portal `source='HMS'` set where supported)

| Endpoint | Roles | Shape |
|---|---|---|
| `GET /api/clinical/:patientId/prescriptions` | Doctor, Nurse, Admin, Pharmacy, Billing | `{ prescriptions[] }` — **no `dispensed` filter; complete set** |
| `GET /api/pharmacy/prescriptions/:patientId` | Pharmacy, Admin, Doctor, Billing | `{ prescriptions[] }` — **filters `dispensed = 0`**; use only for open items |
| `GET /api/pharmacy/dispensing-history/:patientId` | Pharmacy, Admin, Doctor, Billing | `{ history[] }` — dispense events (qty, staff, at) |

Prescription columns: `id, consultation_id, patient_id, medication_name, dosage,
frequency, duration_days, instructions, prescribing_doctor_id, status
(ORDERED|REVIEW_PENDING|APPROVED|DISPENSING|DISPENSED|REJECTED),
rejection_reason, dispensed (0/1), dispensed_at, dispensed_by, created_at`.

### Lab — portal `Observation` with `category = LAB`

| Endpoint | Roles | Shape |
|---|---|---|
| `GET /api/lab/patient/:patientId` **and** `GET /api/lab/:patientId` | LabTech, Doctor, Admin, Nurse | `{ tests: [ { test, results[] } ] }` |

`test`: `id, patient_id, order_doctor_id, test_name, test_type, status
(Ordered|CollectionPending|Collected|InProgress|Verified|Released|Cancelled),
sample_id, ordered_at, completed_at`. Lab statuses are **not** a strict DAG in
code (any member is assignable via `PUT /:testId/status`; only the portal's
mapping cares).

`result`: `id, lab_test_id, result_data, reference_range, flagged (0/1),
lab_tech_id, entered_at`.

### Appointment (portal `Appointment`)

| Endpoint | Roles | Shape / filters |
|---|---|---|
| `GET /api/appointments?doctor_id&patient_id&date&status` | Receptionist, Admin, Doctor, Nurse | `{ appointments[] }` with joined `patient_first/last`, `doctor_first/last`. Filters are exact-equality (no range/since) |
| `GET /api/appointments/:id` | same | `{ appointment }` + `patient_global_id` |

Appointment columns: `id, patient_id, doctor_id, scheduled_date (YYYY-MM-DD
HH:MM:SS), duration_minutes, appointment_type (Consultation|FollowUp|Procedure|Checkup),
status (Scheduled|Confirmed|InProgress|Completed|Cancelled|NoShow), notes,
created_at, updated_at`. Status transitions are enforced on write
(`PUT /:id/status`), not on read.

### What is deliberately NOT in the portal's read set

- Radiology (`imaging_orders`/`imaging_reports`), ward/admissions, billing,
  iot telemetry, HR/procurement, messaging, notifications: out of the portal's
  five-entity clinical scope. The adapter ignores these even though they are
  reachable.
- **Vitals**: HMS stores vital signs only as JSON text in `nursing_notes.vital_signs`
  and `iot_telemetry.telemetry_data` — no normalized vital/observation table, no
  nounit. Portal `Observation` with `category=LAB`|`IMMUNIZATION`|`VITAL`
  (schema.prisma `ObservationCategory`): only `LAB` has a clean HMS source.
  `VITAL` and `IMMUNIZATION` have **no** native HMS counterpart (see §11).

---

## 5. API inventory — WRITE + full surface

The HMS has no integration-facing endpoints. Everything it exposes is the
hospital app's own surface. The exhaustive module list mounted in
`server.js` (dedup): reception, clinical, billing, ward, iot, or, appointments,
pharmacy, radiology, procurement, hr, emergency, personnel, rbac, departments,
requests, documents, wards, admin, notifications, messages, patient,
dashboards, audit, navigation.

Endpoints that already exist and are the *only* realistic write verbs today
(no client-generated ids anywhere):

| Endpoint | Roles | Notes |
|---|---|---|
| `POST /api/reception/register` | authed | Creates a patient; `201 { success, global_id, patient }`. **Always creates** — no upsert/dedupe key except the portal's own bookkeeping |
| `POST /api/appointments` | Receptionist, Admin, Doctor | 409 only on a doctor-time-window conflict. Re-POST duplicates an appointment |
| `PUT /api/appointments/:id`, `PUT /api/appointments/:id/status` | Receptionist, Admin, Doctor | Current-state write; status PUT has strict transitions |
| `PUT /api/lab/:testId/status`, `POST /api/lab/:testId/result` | LabTech, Admin | Status advance / result entry |
| `POST /api/clinical/consult`, `PUT /api/clinical/consultations/:id/soap`, `POST /api/clinical/consultations/:id/sign` | Doctor, Admin | SOAP create / draft / sign |
| `POST /api/clinical/:patientId/prescription` | Doctor, Admin | Always `status` `ORDERED`, pharmacist-notified |
| `PUT /api/pharmacy/:prescriptionId/status`, `POST /api/pharmacy/dispense` | Pharmacy, Admin | Prescription workflow (ALLOWED_TRANSITIONS enforced) |
| `POST /api/radiology/order` | Doctor, Admin | Requires `patient_id, modality, body_part` |
| `POST /api/clinical/:patientId/history` | Doctor, Admin | **Defective — see §13** |

**Idempotency (the decisive fact):** there are **no idempotency keys /
client-generated IDs / ETags / conditional writes anywhere.** A retried POST
creates a second row (except the doctor-conflict 409 for its 30-minute window).
§10 builds on this.

---

## 6. Error, failure & idempotency model

### Error envelope

All handled 4xx/5xx go through one `errorHandler`:
`{ error: string, code?: string | null, details?: any }`.

- Validation: `400 {"error":"Validation failed","code":"VALIDATION_ERROR","details":{...fieldErrors}}`
- AuthN: `401 {"error":"Invalid credentials","code":"INVALID_CREDENTIALS"}` / `401 {"error":"Missing authorization header"}` / `401 {"error":"Invalid or expired token"}`
- AuthZ: `403 {"error":"Role 'Admin' not authorized. Required: Patient"}` (verified live)
- Missing row: `404 {"error":"Patient not found"}` etc. (some handlers write `code:"NOT_FOUND"`; most write only `error`)
- Conflict: `409` for UNIQUE and business conflicts (appointment window, signed-note edits, bootstrap-done)
- Bad route/method: `404 {"error":"Route <METHOD> <path> not found","code":"ROUTE_NOT_FOUND"}` (verified live)
- Unexpected: `500 {"error":"Internal Server Error","code":"INTERNAL_ERROR"}` (verified live)

Status codes are meaningful and, together with the Sqlite-backed cause, are the
adapter's retry signal:

| Class | Meaning for the adapter |
|---|---|
| `401` | Credential problem: **stop everything**, re-login, retry once, then surface as config/credential failure (never blind-retry credentials: 5 failures ⇒ 15-min lockout) |
| `403` | Role/identity mismatch: config error, surface as such |
| `404` | Resource deleted or route wrong: for reads, treat as "gone"; for writes, do not retry |
| `409` | Business conflict: re-fetch and reconcile, do not retry blind |
| `400`/`422`-ish validation | Our payload violated the schema or a business rule: surface as a mapping bug |
| `5xx`/network | Transient: retry with exponential backoff + jitter; then surface |

Requests have an `8h`-token worst case; all reads are tiny and idempotent.

### Inbound retry rules (read path)

- GETs are safe to retry.
- The adapter must compare snapshots, **not** trust timestamps as the only
  change oracle (§8). SQLite timestamps are **second-resolution**:
  a change in the same second as a fetch is invisible to a time-based cursor,
  which is why the contract is diff-based.
- On any 4xx `404` for a patient the portal already holds: mark the portal row's
  HMS linkage as unresolved; keep the portal data (portal is authoritative).

---

## 7. Patient identity & matching

The HMS has **two** ids for a person:

- `patients.id` — integer SQLite PK, the id used by every per-patient endpoint.
- `patients.global_id` — text business key of the form `HMS-XXXXXXXX-XXXXXX`
  (verified live: `HMS-MUWMY8K3-K0GD6G`), generated at registration,
  `UNIQUE NOT NULL`, indexed, searchable via `GET /api/reception/search?q=`.

`users.patient_id` links a patient-portal login to `patients.id` (nullable).

**Mapping decision:**

| Portal column | HMS value |
|---|---|
| `Patient.hmsPatientId` | `patients.global_id` (the stable business key, unique today and searchable) |
| `Observation.hmsId`, `Prescription.hmsId`, `Condition.hmsId`, `Appointment.hmsId` | the HMS **integer** row id of the source row (`medical_history.id`, `lab_tests.id`, `lab_results… see §8`, `prescriptions.id`, `appointments.id`) |

Rationale:
- `global_id` is the only HMS patient identifier that is both stable by design
  and usable for matching, so it is what `hmsPatientId` stores. The integer
  `id` is a private implementation detail of the HMS API calls and lives only
  inside the adapter's per-patient context.
- Clinical rows have no `global_*` columns; their integer `id` is the only
  stable handle, so `hmsId` stores it. A version/revision string does not exist.

**Matching an incoming patient:**

1. A portal user records an HMS patient for the first time by *searching* the
   HMS by name/phone/`global_id` (`GET /api/reception/search?q=`) and linking
   the chosen result → the portal stores `hmsPatientId = global_id` +
   `source = 'HMS'` and kicks off a full snapshot (§9).
2. Duplicate registrations in the HMS cannot be detected natively (no
   probabilistic matching API) — dedupe is a portal-side concern, and the
   portal already owns `Person` as canonical identity (boundary doc §ownership).
3. There is no "list all patients" endpoint, so the adapter can never discover
   HMS patients it isn't told about. Discovery is appointment/encounter-driven
   (§9).

---

## 8. Entity data mapping (portal → HMS) & change detection

Naming: *portal* models are from `apps/api/prisma/schema.prisma`; *HMS* tables
are from `src/config/schema.sql`.

| Portal entity (`source='HMS'`) | HMS source table(s) | HMS id → portal column | Primary read |
|---|---|---|---|
| `Patient` (via `Person` demographics) | `patients` | `global_id` → `Patient.hmsPatientId` | `GET /clinical/:id/emr` → `.patient` |
| `Condition` | `medical_history` | `medical_history.id` → `Condition.hmsId` | `…/emr` → `.medical_history[]` |
| `Observation` (LAB) | `lab_tests` + `lab_results` | `lab_tests.id` → `Observation.hmsId`; results as `Observation.value`/notes + `dateSeen` from `entered_at` | `GET /lab/patient/:id` |
| `Prescription` | `prescriptions` (+ `pharmacy_dispensing` for the dispense event) | `prescriptions.id` → `Prescription.hmsId` | `GET /clinical/:id/prescriptions` |
| `Appointment` | `appointments` | `appointments.id` → `Appointment.hmsId` | `GET /appointments?patient_id=` |
| `Observation` (VITAL) | `nursing_notes.vital_signs` JSON text, `iot_telemetry` | n/a — **no normalized source** | (out of v1 scope) |
| `Observation` (IMMUNIZATION) | — | n/a — **no HMS source at all** | (portal-native only) |
| `Allergy` | — | n/a — **no HMS table for allergies** | (portal-native only) |

Portal `Person.sexAtBirth` is free text; HMS `patients.gender` is
`Male|Female|Other|PreferNotToSay` — map directly.

### Change detection per source, honestly

There is no global "since" cursor and **per-entity modified timestamps are
partial and inconsistent**:

| Source | Creation stamp | Update stamp | Verdict |
|---|---|---|---|
| `patients` | `created_at` | `updated_at` (set by `updatePatient`) | good |
| `appointments` | `created_at` | `updated_at` — **but only the `/status` PUT bumps it; a reschedule (`PUT /:id`) does not** | diffs required |
| `consultations` | `consultation_date` (no `created_at`) | `updated_at` nullable, set on soap-edit / sign; `signed_at` on sign | use `COALESCE(updated_at, consultation_date)` as a *hint only* |
| `medical_history` | `created_at` | none (rows are never updated) | creation-only |
| `prescriptions` | `created_at` | **none**; status changes are unstamped (only `dispensed_at` exists) | diffs required |
| `lab_tests` | `ordered_at` | `completed_at` — and the lab route only stamps it at `Verified`, **not** at `Released` (§13 #2) | diffs required |
| `lab_results` | `entered_at` | none | creation-only |

**Therefore:** the adapter must fetch a patient's full snapshot for a resource
type and compute a *same-patient set diff* keyed by `hmsId`, comparing the
fields it cares about, instead of trusting a database timestamp. `hmsFetchedAt`
is bumped only when a whole snapshot fetch + reconcile completes successfully.
This is exactly what the boundary document's snapshot semantics anticipated.

Because HMS timestamps are UTC `DATETIME DEFAULT CURRENT_TIMESTAMP`
(second-resolution, no TZ marker), the adapter normalizes every HMS datetime to
JS `Date`/ISO (UTC) for comparison with portal fields.

---

## 9. Sync strategy (recommendation)

Fitted to the real API, which has **no webhooks, no outbox, no modified-since
filter, no pagination on clinical reads, and no global patient list**:

1. **Pull-only, per-patient, full-snapshot.** For a linked
   (`hmsPatientId`) patient, fetch `emr` + lab + appointments and reconcile by
   diff (§8). No bulk/global sync is expressible against this API.
2. **Discovery is encounter-driven.** A patient enters the sync set when a
   portal user links them from an HMS search, or when the portal already holds
   the reference. HMS-side new registrations reach the portal only when a human
   links them (the portal owns its own patient population and may also create
   HMS patients through the portal's own `POST /api/reception/register` client,
   which then returns the fresh `global_id`).
3. **Refresh triggers:** a portal-side appointment/encounter reference touching
   the patient, on-demand "refresh", and a modest staleness sweep
   (`hmsFetchedAt` older than a threshold). No scheduler dependency to boot.
4. **Reconcile exactly once per row:** insert new `hmsId`s, update rows whose
   diff changed, **delete nothing** (portal keeps its own row; the HMS has no
   hard deletes — see §6/§8 — so absence of a row in a later snapshot means the
   HMS id is gone or the endpoint scoping changed, and that is surfaced rather
   than silently removed).
5. **Stamps:** set `hmsFetchedAt` only after the whole per-entity fetch
   succeeded. `source` stays `'HMS'`.
6. **Ordering:** labs/prescriptions depend only on patient; consultations on
   visit. Per-patient parallelism across entities is safe (independent rows),
   but a single patient snapshot is small — serialize per patient.

### 9.1 Webhook Invalidation Contract (HMS → Portal)

While polling + TTL bounds worst-case staleness, an inbound webhook receiver at
`POST /api/integrations/hms/webhook` accelerates synchronization when events occur in HMS:

1. **Authentication & Signature Scheme**:
   - Header: `X-Hms-Signature: t={unix_timestamp},v1={hex_hmac_sha256}`
   - The signature is calculated as `HMAC-SHA256("{t}.{rawBody}", secret)` where `{t}`
     is the integer unix timestamp and `{rawBody}` is the verbatim UTF-8 request payload.
   - **Replay Protection**: Timestamps older than 300 seconds (5 minutes) or in the future (> 60s)
     are rejected with HTTP 401.
   - **Constant-Time Verification**: Verification must execute in constant time (`FixedTimeEquals`)
     to prevent timing side-channels.
   - **Key Rotation**: The receiver supports multiple active secrets (comma-separated or configured list).
     A signature matching any active secret is accepted.
2. **Thin Events & Minimal PHI**:
   - Webhook bodies carry **no clinical details** (no diagnoses, vitals, or med details) to limit
     PHI exposure over webhook transports.
   - Payloads contain strictly identity and event routing tokens:
     ```json
     {
       "eventId": "evt_01HXYZ1234567890",
       "event": "patient.updated",
       "hmsMrn": "HMS-MUWMY8K3-K0GD6G",
       "timestamp": "2026-10-23T22:00:00Z"
     }
     ```
3. **Idempotency & Processing**:
   - Deduplication: `eventId` is persisted in `HmsProcessedWebhookEvents`. Repeated delivery
     of the same `eventId` is a safe no-op (200 OK returned).
   - Action: A valid event marks the patient as dirty / schedules immediate priority sync,
     which pulls fresh data via the authenticated `IHmsClient`.

Missing HMS counterpart for `Allergy` / `IMMUNIZATION` / `VITAL` is **not a sync
hole**: those stay portal-native, and the portal's own UI keeps being the
creator. The portal must never try to load them from the HMS.

---

## 10. Outbound (write-back) strategy

**Decision: no outbound in v1.** Rationale, against the real API:

- The portal's own clinical records are `source = 'PORTAL'` and are created by
  the portal's own domain logic; HMS never needs them today.
- **The HMS has no idempotency keys.** Any retried create duplicates (appointments
  except for the 30-minute doctor-window conflict). There is no client-id
  column, no dedupe header, no upsert endpoint (registration excepted, and it
  only creates).
- If outbound were adopted later, the only sound shape is: portal-owned
  `ExternalIdentity`-style mapping (portal row → HMS `global_id` for patients,
  or → verified-result status moves), current-state `PUT`/status endpoints only,
  ordering via an outbox, and reconciliation by re-read §8. This remains a
  decision for a future phase and is exactly what M1 in the boundary
  doc's migration sequence guards.

The portal's only sanctioned HMS write today is registering genuinely-new
patient records (`POST /api/reception/register`) when a portal user creates a
patient that must exist in HMS — done through the adapter, recording the
returned `global_id`, never retried blind (retry = duplicate patient in HMS;
retries must be gated on a prior successful read).

---

## 11. FHIR assessment

- `rg -il 'fhir|smart|hl7'` over the HMS `src`, `docs`, and README: **no
  matches**. There is no FHIR, SMART-on-FHIR, HL7, or any standards-based
  interoperability surface in the HMS.
- The portal therefore owns all terminology/value mapping itself (the §8 table
  IS that mapping). If the portal ever needs to talk to an EHR network, it
  builds FHIR at the portal's own boundary, not by reaching through the HMS.

---

## 12. Security boundary & operational prerequisites

- **No shared secrets:** the portal holds an HMS *username* + *password* (env /
  secrets manager, never in the portal DB or repo) and transient bearer tokens.
  It never holds the HMS `JWT_SECRET` or DB connection.
- **Attempt hygiene:** 5 failed logins lock the service account for 15 min.
  The adapter must back off credentials hard after any `401`; it must schedule
  re-login *before* token expiry (≤ 8h cadence) so it never thrashes the
  lockout counter.
- **Operator prerequisites (out of this phase, but must exist before the
  client runs):**
  1. HMS reachable from the portal's network at a configured `HMS_BASE_URL`.
  2. An HMS `Admin` account provisioned: `active=1`, `must_change_password=0`,
     not shared with humans.
  3. Portal env/secrets: `HMS_BASE_URL`, `HMS_USERNAME`, `HMS_PASSWORD`
     (names finalised in the implementation phase).
  4. NTP on the portal node (JWT `exp` verification is against local time).
- **No schema change is required on the HMS side** — the portal needs nothing
  from the HMS but its HTTP surface. The only portal-schema prerequisite is
  already shipped (the `hmsId`/`hmsPatientId`/`source`/`hmsFetchedAt` columns).

---

## 13. Known HMS defects & gaps the adapter must design around

These were observed in code and, where noted, reproduced live. They are HMS
bugs, not portal bugs; the adapter must not trip on them and must log them when
hit.

1. **`POST /api/clinical/:patientId/history` 500s unless every optional field
   is supplied.** `createMedicalHistory` (models/index.js:188) binds
   `entry.treatment` etc. raw; zod-omitted optionals bind as `undefined` and
   `node:sqlite` throws `Provided value cannot be bound to SQLite parameter 4`
   → `500 INTERNAL_ERROR`. Reproduced live; worked when `treatment` was
   provided. Adapter rule: always send every history field, empty-string for
   blanks, or treat a `500` on history-write as "wait and use a different path".
2. **`lab_tests.completed_at` is set at `Verified`, not `Released`** —
   `updateLabTestStatus` compares against `'Completed'`/`'Verified'`, and the
   lab routes' status list never uses `'Completed'` (CHECK allows it; code
   doesn't). Do not infer "result available" from `completed_at`; use
   `status = 'Released'` **and** presence of `lab_results`.
3. **`PUT /api/appointments/:id` (reschedule/edit) does not bump
   `updated_at`** — only `PUT /api/appointments/:id/status` does. Change
   detection must diff the appointment row (§8), not trust the stamp.
4. **`GET /api/pharmacy/prescriptions/:patientId` filters `dispensed = 0`** and
   JOINs `consultations`; dispensed items vanish, and orphaned
   `consultation_id` (impossible today: FK NOT NULL) would 404 the endpoint.
   Use `GET /api/clinical/:patientId/prescriptions` for the full set.
5. **Second-resolution `CURRENT_TIMESTAMP` timestamps:** two different HMS state
   transitions within one wall-clock second are indistinguishable by timestamp.
   This is the core reason the sync strategy is diff-based (§8/§9).
6. **No pagination** on any patient clinical read. Large histories grow one
   response; the adapter fetches whole snapshots (fine for per-patient data;
   remember this when sizing backoff).
7. **No global patient listing** (`/reception/search` is the only discovery),
   and duplicate HMS registrations are undetectable natively (§7).
8. Cosmetic: `/api/reception` is mounted twice in `server.js` — benign.

---

## Appendix A — Verified live DTO examples (throwaway probe)

Login (`admin`/`Admin`):

```json
{ "token": "<jwt exp≈+8h>",
  "user": { "id": 2, "username": "admin", "full_name": "Admin User",
            "role": "Admin", "department": "Administration",
            "must_change_password": false, "permissions": [ "PATIENT_VIEW",
            "PATIENT_CREATE", "MEDICAL_RECORD_VIEW", "APPOINTMENT_CREATE", "…" ] } }
```

Register:

```json
{ "success": true, "global_id": "HMS-MUWMY8K3-K0GD6G",
  "patient": { "id": 32, "global_id": "HMS-MUWMY8K3-K0GD6G",
    "first_name": "Jordan", "last_name": "Blake", "date_of_birth": "1992-04-18",
    "gender": "Male", "blood_type": "O+", "email": "j.blake@example.com",
    "phone": "+1555000111", "address": "12 Main St", "created_at": "2026-10-06 12:09:06",
    "updated_at": "2026-10-06 12:09:06" } }
```

Signed consultation:

```json
{ "success": true, "consultation": { "id": 10, "visit_id": 1, "patient_id": 32,
  "doctor_id": 4, "chief_complaint": "Epigastric pain", "diagnosis": "Gastritis",
  "subjective": "2 wks burning pain", "objective": null, "assessment": "Gastritis",
  "plan": "PPI", "status": "Signed", "signed_at": "2026-10-06 12:09:52",
  "updated_at": "2026-10-06 12:09:52", "consultation_date": "2026-10-06 12:09:52" } }
```

Released lab:

```json
{ "tests": [ { "test": { "id": 13, "patient_id": 32, "order_doctor_id": 2,
  "test_name": "CBC", "test_type": "Hematology", "status": "Released",
  "sample_id": "S-1001", "ordered_at": "2026-10-06 12:09:17",
  "completed_at": "2026-10-06 12:10:44" },
  "results": [ { "id": 5, "lab_test_id": 13, "result_data": "WBC 7.2, Hb 14.1",
    "reference_range": "WBC 4-11", "flagged": 0, "lab_tech_id": 2,
    "entered_at": "2026-10-06 12:10:44" } ] } ] }
```

## Appendix B — Deferred in this phase (explicitly NOT implemented)

No code was written in this phase. Not built, because the user directive kept
them out: HMS HTTP client, DTO types, `apps/api/src/integrations/hms/`, worker /
scheduler / webhook, outbound queue, `ExternalIdentity` table, any portal schema
change, bulk sync, FHIR forwarding, and the portal env additions in §12.