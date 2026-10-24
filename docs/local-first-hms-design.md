# Local-First + HMS integration — design review

**Status:** architecture **decided** 2026-10-01. Schema pending one open question.

## Decision record

Two answers were given that resolve the original request's central premise:

1. **Offline is not a hard requirement.**
2. **HMS is read-only to the portal for clinical data.** Writes are clinician-only
   in the hospital system. Patient interaction is confined to discrete staged or
   transactional writes that never mutate the chart in place.

### Consequence: ElectricSQL is dropped from this design

The original request asked for PGlite-in-browser synced via ElectricSQL, with
`created_at`/`updated_at` "for CRDT conflict resolution." That entire mechanism was
only ever justified by **bidirectional** sync of clinical records.

With clinical data pull-only and patient writes transactional:

- No CRDT is needed for clinical data — the portal never writes it, so there is
  nothing to merge.
- No conflict resolution is needed for patient actions — they are queued
  transactions with server-side validation, not merged rows.
- `created_at`/`updated_at` remain ordinary audit columns. They were never the
  sync mechanism (§2.2).

What replaces it is **much smaller**: a server-side cache with a refresh strategy, a
webhook/SSE invalidation path, and an outbound queue with idempotency keys.

This removes the single most expensive decision in the original request — a second
query layer in the browser, because Prisma has no PGlite adapter (§2.4).

---

## 1. What this repository actually is

Verified, not assumed:

| Premise in the request | Reality here |
| --- | --- |
| "our project's ORM" | ✅ Prisma 6.19 (`@prisma/client`, `@prisma/adapter-pg`) |
| "PGlite running in the browser" | ❌ PGlite is **server-side only** — `src/localdb.ts` (dev), `src/testing/database.ts` (tests). Zero references in `apps/web/`. |
| "synced via ElectricSQL" | ❌ No sync layer. Only `@electric-sql/pglite` (Postgres-in-WASM) and `@electric-sql/pglite-socket` (wire shim so Prisma can reach it). |
| external HMS | ❌ None. README: *"the clinic's system of record … with no external EHR integration at this stage."* Only external calls are Google Places and OSM/Nominatim geocoding. |
| UUID primary keys | ❌ All 34 models use `cuid()`. |
| `created_at`/`updated_at` for CRDT | ⚠️ Columns exist on most models, but they drive **no** CRDT — see §2.2. |

The `@electric-sql/*` namespace is the likely source of confusion: PGlite is
*published by ElectricSQL*, so those package names read as sync. They are not.
PGlite is a database runtime; ElectricSQL sync is a separate service that is not
installed here.

Production is PostgreSQL 16 (`docker-compose.yml`), which is the right substrate
for ElectricSQL if that path is chosen.

## 2. Findings that change the plan

### 2.1 Three of the four entities already exist

| Requested | Exists as | Assessment |
| --- | --- | --- |
| `PortalUsers` | `UserAccount` — `email`, `passwordHash`, `mfaEnabled`, plus `role`, lockout, `sessionTimeoutMinutes` | Superset. Reuse. |
| `PatientProfiles` | `Person` (demographics) + `Patient` (`mrn`, `dateOfBirth`) | Split is already cleaner than one table: `Person` is patient-editable, `Patient` is clinical identity. |
| `ProxyAccess` | `ProxyGrant` — 7 boolean permissions, `consentGivenAt`, `revokedAt` | **Richer than proposed.** See §2.3. |
| `CachedAppointments` | `Appointment` (portal-native) | HMS concept is genuinely new. See §4.2. |

Creating the four as new tables would produce two competing representations of
users, patients, proxy access and appointments in one database.

### 2.2 `created_at`/`updated_at` do not give you CRDT conflict resolution

This is the most important correction in this document.

ElectricSQL does **not** use your timestamps to resolve conflicts. It maintains its
own metadata — logical position / last-modified markers — derived from Postgres
logical replication. `created_at`/`updated_at` are ordinary business columns used
for audit and ordering.

Two consequences:

1. Adding them "for CRDT conflict resolution" does nothing. Sync correctness does
   not depend on them.
2. Conversely, a table can be perfectly synced while `updated_at` is stale or
   identical across replicas. Timestamp presence is **not** evidence of sync safety.

### 2.3 A 3-level `access_level` enum would be a regression

Proposed: `full | read-only | billing-only`.

The existing `ProxyGrant` uses independent booleans (`canViewRecords`,
`canManageAppointments`, `canRequestRefills`, `canPayBills`, `canMessage`,
`canManageSharing`). An enum forces one level, which loses combinations like
"can view records and request refills, but not pay bills."

That is not hypothetical. Phases 0–1 of this codebase fixed a billing disclosure
where a records-only caregiver could read a balance, and `Statement` → `payBills`
in the FHIR policy table exists specifically to keep billing separate from
record-read. Collapsing to three levels re-opens that class of bug.

**Recommendation: keep the boolean permissions.** If HMS needs to see proxy
access, expose it through an API, not by relaxing the model.

### 2.4 Prisma has no browser/PGlite driver adapter

This is the largest engineering cost and it is not obvious.

The entire stack is Prisma. ElectricSQL's browser story assumes PGlite driven by
Drizzle or the raw PGlite client. Prisma 6 ships no PGlite adapter, so a
local-first web client cannot use the ORM this project uses.

Options:

| Option | Cost |
| --- | --- |
| A. Second query layer in the browser (Drizzle/Kysely) | Two ORMs, two schema definitions, drift risk. Browser must run a **subset** of the schema. |
| B. Browser reads through Electric's client, Prisma stays server-only | Loses "run the same SQL both sides." Browser becomes a read cache, not an app. |
| C. Server-mediated (current architecture) | No offline. |

Whichever is chosen, the **browser schema must be a strict subset** of the server
schema, and that subset is a security boundary — see §3.2.
## 3. Architecture: two tracks, one boundary

The decision splits portal interaction into two tracks that never mix. The boundary
between them is the whole design.

```
   ┌─ TRACK 1: CLINICAL DATA ─ read-only ──────────────────────────┐
   │  HMS  ──pull──►  Portal API  ──read──►  Browser              │
   │  (owner)        (server cache)          (fetches per session) │
   └─────────────────────────────────────────────────────────────┘

   ┌─ TRACK 2: PATIENT ACTIONS ─ staged / transactional ──────────┐
   │  Browser ──► Portal API ──queue──► HMS (validate, apply)     │
   └─────────────────────────────────────────────────────────────┘
```

### 3.1 Track 1 — clinical data is a cache, not a replica

Observation, Condition, MedicationRequest, DiagnosticReport are pulled from the
HMS and cached server-side. The portal **never writes them**, so:

- there is nothing to merge, and no CRDT is involved
- the cache is authoritative-nothing: stale data is a **freshness** problem, handled
  by TTL + webhook/SSE invalidation, not by conflict resolution
- a cache diverging from its source is fixed by re-fetching, not by a merge policy

This is the property that makes the whole thing tractable. A cache you can always
rebuild is fundamentally safer than a replica you have to reconcile.

**Browser storage is not required for this track.** Since offline is not a hard
requirement, the browser can simply fetch from the Portal API on session start. If
responsiveness later proves insufficient, HTTP caching or a service worker buys most
of the benefit at a fraction of the cost of a browser database — and without the
Prisma-adapter problem in §2.4.

### 3.2 Track 2 — patient actions are submissions, not mutations

Every patient action is an **immutable submission** or a **staged change request**.
None mutates a chart field in place:

| Action | Shape | HMS effect |
| --- | --- | --- |
| Appointment request | New Appointment, `status: proposed` | Triage/booking decision by staff |
| Appointment cancel | Status transition to `cancelled` | Status change, not a field edit |
| Intake / PROMs | Immutable `QuestionnaireResponse` | Attached to encounter for review |
| Secure message | `Communication` payload | Routed to clinical triage queue |
| Demographic change | Row in a **staging** table | Admin review, then MPI update |

This is the correct pattern for medical records generally, and it has a property
worth stating: **the patient's contribution is auditable and attributable** without
ever being mistaken for clinician-authored content. Nothing a patient submits can
silently alter a diagnosis, because it structurally cannot reach a chart field.

### 3.3 What is deliberately still server-only

Unchanged from the earlier analysis, and now more strongly so — there is no sync
layer at all, so nothing can leak out of the server by accident:

- **`UserAccount`** — credentials, `passwordHash`, lockout counters.
- **`ProxyGrant`** — RBAC. Still the one place where an automatic merge would be
  genuinely dangerous (§2.3): a concurrent revoke/grant resolved by last-write-wins
  can resurrect a revoked grant.
- **`AuditLog`** — append-only, needs a trustworthy total order that a merge cannot
  provide.
- **`Billing`** — portal-owned; the HMS never sees it.
## 4. HMS integration boundary

The portal changes role: from system of record to patient-facing view over an HMS.

### 4.1 Authority split

| HMS owns (portal caches) | Portal owns (HMS never sees) |
| --- | --- |
| Patient master index / demographics | Portal identity + credentials |
| Appointments | Proxy / family access grants |
| Labs, immunizations, conditions, medications, allergies | Messaging |
| Provider identity | Billing and payments |
| | Record-share consent, preferences, notifications |

The right-hand column must never sync to a browser.

### 4.2 Two concepts, and they are not the same thing

The requested `CachedAppointments` merges two concerns with different correct
designs.

**(a) The cache — read-only, server-side, no sync.**
HMS data is *derived*, so it should never be a CRDT replica: two replicas merging
a cache produces divergence that no conflict rule repairs. Model it as a server-side
cache refreshed from the HMS. Where those rows physically live is the open
question in §5.2 — the recommended option extends the existing `Appointment`,
`Observation`, `Condition` and `Prescription` tables with a provenance pair rather
than adding parallel `hms_*` tables.

**(b) The action queue — where sync status belongs.**
`sync status` describes *outbound* intents: things the patient asked for that the
portal must deliver. That is a queue, not a cache, and the states are delivery
outcomes rather than merge outcomes:

```
PENDING ──► SENDING ──┬─► DELIVERED
                      ├─► REJECTED          (terminal — needs human attention)
                      └─► RETRYABLE_FAILURE (same idempotency key, safe to re-send)
```

The split between `REJECTED` and `RETRYABLE_FAILURE` is load-bearing. Conflating
them is how a queue either silently drops patient requests or floods an HMS with
duplicates.

Every entry carries a client-generated **idempotency key**, so a retry after a
network failure cannot double-book. Schedule conflict is real and expected: the
patient requests a 3pm slot while the clinic books it moments later.

**Recommendation:** modelled in §5.3 as `OutboundActionQueue`, with `sync_status` on
the queue and never on a cache.

### 4.3 Linking a portal patient to an HMS patient

`Patient.hmsPatientId String? @unique` is the right shape. Three constraints:

1. **Nullable.** Not every portal user is an HMS patient — a caregiver or a payer
   may have portal access and no chart. A non-null FK would force fake records.
2. **Unique when present.** One HMS patient ↔ one portal patient.
3. **Resolved during onboarding, never at read time.**

Point 3 matters most. Matching a signup to an HMS chart on name + date of birth
will eventually link the wrong patient's chart — the worst failure mode available
in this system, and hard to detect afterwards. Resolution should be explicit: MRN
entered by the patient, or a staff-verified match queue. A heuristic may *propose*
a match; it must not silently *make* one.

### 4.4 Primary keys: keep `cuid`

Changing all 34 models from `cuid()` to UUID is a full data migration across every
foreign key, on tables holding real patient data, for no functional gain — `cuid`
is already unique, non-guessable, sortable, and valid as a client-generated
offline ID.

Where UUIDs are genuinely required is **HMS external references**, if the HMS
mandates its own ID format. That is a column-type choice on `hms_patient_id`, not a
reason to rewrite internal primary keys.

**Recommendation: keep `cuid`.** If UUID is a hard HMS mandate, scope it to the
external-reference columns.
         │      ← authority: auth, RBAC, audit, billing, sync
         │
         ├── optional, opt-in: read-only local cache in browser
         │     · scoped per-patient, encrypted, wipe-on-logout
         │     · one-way: server → client only
         ▼
   Browser (existing web app)
```

- Server authoritative for credentials, proxy access, audit, billing, sync.
- Browser may hold a **read-only, opt-in** cache for offline read.
## 5. Schema recommendation

Additive only. Nothing removed, no primary keys changed, no existing column altered.

### 5.1 One consequence of the decision that needs flagging

**The FHIR write surface built in Phase 4 now has a different meaning.**

`POST /fhir/MedicationRequest` and `POST /fhir/Observation` currently persist to the
portal's own tables, clinician-only, with a server-side allergy check. If the HMS
owns clinical data, those writes must **forward to the HMS**, not land in Postgres.

This is a benefit, not a problem: the server-side `mayPrescribe` check becomes a
*pre-flight* check before a prescription reaches the HMS. The portal can refuse a
clinician's order locally on allergy grounds before it is ever submitted — and the
HMS's refusal still has to be handled as a reconciled failure, not assumed away.

It does mean the Phase 4 tests, which assert rows land in the portal database, will
need to assert against the forwarding path instead. Flagging rather than discovering
it mid-migration.

### 5.2 Open question: where HMS rows live

Before generating the migration, one decision is needed.

**Option A (recommended) — extend the existing tables.**
Add `hmsId String? @unique` and `source ClinicalSource @default(PORTAL)` to
`Observation`, `Condition`, `Prescription`, `Allergy`, `Appointment`. The pull job
writes HMS rows in place.

- Zero read-path changes; every existing screen and the 442 tests keep working
- Conflates portal-authored and HMS-authored rows in one table
- Needs a rule so `source = PORTAL` rows are never overwritten by a pull

**Option B — dedicated `hms_*` cache tables.**
Clean provenance separation, but every clinical read becomes a two-source merge.
Materially more work and more risk for the same result.

Recommend **A** for phase 1, with `source` as the audit answer to "who wrote this
row?", which will be asked.

### 5.3 New tables

```prisma
/// Where a clinical row came from. Portal-authored rows predate the HMS
/// integration and are still created by clinic staff through the portal.
enum ClinicalSource {
  PORTAL
  HMS
}

model Patient {
  // ...existing fields...

  /// The HMS's identifier for this patient.
  ///
  /// Nullable: caregivers and payers may hold portal access with no HMS chart,
  /// and a non-null FK would force placeholder records. Unique when present.
  ///
  /// Resolved once at onboarding (MRN entry or staff-verified match), never
  /// inferred at read time — a wrong link exposes the wrong chart, which is the
  /// worst failure available in this system.
  hmsPatientId String? @unique
}
```

```prisma
/// A patient's requested demographic change, awaiting administrative review.
///
/// Not an edit. The patient states what they believe is wrong; staff apply it to
/// the HMS Master Patient Index after review. Direct overwrite of the MPI by a
/// patient is the thing this table exists to prevent.
model DemographicChangeRequest {
  id        String @id @default(cuid())
  patientId String
  patient   Patient @relation(fields: [patientId], references: [id], onDelete: Cascade)

  /// The field being contested, e.g. "addressLine1".
  field String
  currentValue String?
  proposedValue String
  reason String?

  status ChangeRequestStatus @default(PENDING)

  /// Set when the portal pushes the approved value to the HMS.
  appliedAt     DateTime?
  appliedById   String?
  rejectionNote String?

  createdAt DateTime @default(now())
  updatedAt DateTime @updatedAt

  @@index([patientId, status])
  @@map("demographic_change_requests")
}

enum ChangeRequestStatus {
  PENDING
  APPROVED
  REJECTED
  APPLIED
  FAILED
}
```

```prisma
/// Patient actions awaiting delivery to the HMS.
///
/// This is where sync status belongs. These are *intents the patient expressed* —
/// an appointment request, a questionnaire submission, a message — not rows in a
/// merged replica. Delivery is transactional and idempotent; there is no merge.
model OutboundActionQueue {
  id        String @id @default(cuid())
  patientId String

  /// APPOINTMENT_REQUEST | APPOINTMENT_CANCEL | QUESTIONNAIRE | MESSAGE
  kind    String
  payload Json

  /// Client-generated. A retry after a network failure must not double-book.
  idempotencyKey String @unique

  status    ActionStatus @default(PENDING)
  attempts  Int         @default(0)
  lastError String?
  resolvedAt DateTime?

  createdAt DateTime @default(now())
  updatedAt DateTime @updatedAt

  @@index([status, createdAt])
  @@map("outbound_action_queue")
}

enum ActionStatus {
  PENDING
  SENDING
  DELIVERED
  /// HMS rejected it — slot taken, validation failed, patient not found.
  REJECTED
  /// Transient failure; safe to retry with the same idempotency key.
  RETRYABLE_FAILURE
}
```

Note the split between `REJECTED` (terminal, needs human attention) and
`RETRYABLE_FAILURE` (safe to re-send). Conflating them is how a queue either loses
messages or floods an HMS with duplicates.

### 5.4 Explicitly not built

| Original request | Verdict |
| --- | --- |
| ElectricSQL sync layer | **No.** No bidirectional sync means no CRDT (§3). |
| Browser PGlite database | **No.** Offline is not required; no Prisma adapter exists (§2.4). |
| `PortalUsers` table | **No.** Reuse `UserAccount` — a superset. |
| `PatientProfiles` table | **No.** `Person` + `Patient` already split this better. |
| `ProxyAccess` 3-level enum | **No.** Regresses the permissions that keep billing separate from record-read (§2.3). |
| UUID primary keys | **No.** Full data migration, no gain (§4.4). |
| `created_at`/`updated_at` for CRDT | Already present as audit columns; never were the sync mechanism (§2.2). |

### 5.5 Migration shape

Two migrations, so provenance can be reasoned about separately from the queue:

```
2026XXXXXX000000_hms_integration
  + Patient.hmsPatientId
  + enum ClinicalSource
  + hmsId / source on Observation, Condition, Prescription, Allergy, Appointment

2026XXXXXX000001_hms_patient_actions
  + create table demographic_change_requests
  + create table outbound_action_queue
  + enums ChangeRequestStatus, ActionStatus
```

Additive and revertible by dropping objects; no backfill; the 442 existing tests
keep passing because no read path changes until code reads the new fields.
## 6. Decision status

### Answered

| Question | Answer | Consequence |
| --- | --- | --- |
| Is offline a hard requirement? | No | No browser database, no ElectricSQL. §3 |
| Is the HMS writable for clinical data? | No — clinician-only in the HMS | Portal caches; patient actions are staged. §3 |
| Where does clinical data live? | HMS is system of record | Portal becomes a view, not a record. §4.1 |

### Still open — one blocks the migration

**Where do HMS rows live? (§5.2)** Option A (extend existing tables with `hmsId` +
`source`) or Option B (dedicated `hms_*` tables). **Recommend A.** This is the only
question standing between the current design and a runnable migration.

### Still open — do not block the migration

- **Does the HMS expose webhooks, or only polling?** Affects cache freshness
  strategy, not schema. Poll with a short TTL is a safe default.
- **Is `hmsPatientId` format-constrained?** Only matters if the HMS mandates UUID,
  which is a column-type choice, not a PK migration (§4.4).
- **What is the read SLA for cache staleness?** A product question. Default to a
  short TTL plus explicit `fetchedAt` surfaced to the UI.
- **Which demographics are patient-editable today?** `Person` currently allows the
  patient to edit demographics directly. If the HMS owns the MPI, those writes must
  become `DemographicChangeRequest` rows instead (§5.3). This changes an existing
  feature and needs a product decision on which fields move to review.

### Suggested sequencing

1. **Answer §5.2.** Generate both migrations.
2. **Link + provenance** — `hmsPatientId`, `hmsId`/`source` columns. Read paths
   untouched; nothing depends on the HMS being reachable.
3. **Pull job** — HMS → portal cache, with `fetchedAt` and TTL. This is the track-1
   deliverable and it is independently useful.
4. **Patient action queue** — `outbound_action_queue` + the endpoints that enqueue.
   Deliverable without any HMS write access; it just sits `PENDING`.
5. **Forwarding** — make the Phase 4 FHIR write path forward rather than persist.
   Depends on HMS write API access for clinicians (§5.1).
6. **Demographic change requests** — only after the MPI field list is agreed.

Steps 1–4 are useful independently and each is verifiable before the next begins.
Step 5 is the one that genuinely needs the HMS to exist first.