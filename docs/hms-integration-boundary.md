# Medical Portal ↔ HMS Integration Boundary

**Status:** architecture **decided**. Supersedes the ownership framing in
`docs/hms-decisions.md` and `docs/local-first-hms-design.md`, which described the
portal as "a cache" of an HMS that "owns clinical data". That framing is wrong. It
made the HMS look like the portal's domain model. This document states the correct
one.

---

## 0. The one-sentence model

> The Medical Portal and the HMS are two independent systems. Each has its own
> database, domain model, users, business rules, migrations, lifecycle, and API.
> They communicate only through an explicit integration boundary, and removing the
> HMS must leave the portal fully functional.

```
HOSPITAL MANAGEMENT SYSTEM
        │
        │  explicit integration/API boundary (never a shared database)
        ▼
HMS Integration Adapter        ← apps/api/src/integrations/hms/ (client, DTOs,
        │                         auth, external ids, mappers, sync logic)
        ▼
MEDICAL PORTAL API
        ▼
MEDICAL PORTAL DATABASE        ← the portal's own Postgres; authoritative for
                                   everything the portal stores
```

**This document exists to make it impossible for a future developer to accidentally
turn the HMS database into the portal's domain model.**

---

## 1. Independent system architecture

- The portal API (`apps/api`) talks to **its own Postgres**, defined entirely by
  `apps/api/prisma/schema.prisma`. It never issues SQL against an HMS database and
  has no HMS tables or views in its schema.
- The HMS is reached only through an HTTP/API boundary implemented by an adapter
  inside the portal (`apps/api/src/integrations/hms/`, described in §3). No
  portal route, service, or mapper imports or depends on HMS tables.
- Portal startup has **no HMS dependency**: there is no HMS connection, no HMS
  credential, no HMS health check, and no HMS configuration required to boot. `env.ts`
  today requires nothing HMS-related, and the app registers no HMS routes.
- Deployment option exists today: **Medical Portal API + Medical Portal database,
  nothing else.** That deployment is the default, not a degraded mode.

## 2. Medical Portal database ownership

The portal owns every row in its own database. "Owned" means: the portal decides
its representation, its lifecycle, who may create/edit it, and whether it may be
removed. Nothing is owned by the HMS merely because the HMS also knows about a
real-world counterpart.

| Entity | Kind | Native to portal | May reference HMS | Can exist without HMS | Editable in the portal |
|---|---|---|---|---|---|
| `UserAccount` | identity/auth | yes | – | yes | yes (portal credentials) |
| `Person` | identity | yes | partial demographic mirror (§6) | yes | yes |
| `Patient` | record of care | yes | `hmsPatientId` external ref | yes | yes |
| `Provider` / `Location` / `PatientCareTeam` | care organisation | yes | – | yes | yes |
| `Observation` | clinical | yes | `hmsId` external ref | yes | PORTAL rows yes; HMS-imported rows reviewed via action, not edited in place |
| `Condition` | clinical | yes | `hmsId` | yes | same as Observation |
| `Prescription` | clinical | yes | `hmsId` | yes | same as Observation |
| `Allergy` | clinical (prescribing safety) | yes | `hmsId` | yes | same as Observation |
| `Appointment` | scheduling | yes | `hmsId` | yes | same as Observation |
| `Medication` | formulary | yes | – | yes | yes |
| `RefillRequest` | workflow | yes | – | yes | yes |
| `HealthSnapshot` | read model | yes | derived from portal rows | yes | generated |
| `MessageThread` / `Message` / `MessageAttachment` | messaging | yes | – | yes | yes |
| `Statement` / `Payment` / `PaymentPlan` / `PaymentInstallment` | billing | yes | – | yes | yes |
| `RecordShare` / `RecordShareAccess` | consent/sharing | yes | – | yes | yes |
| `ProxyGrant` / `ProxyInvite` | consent/RBAC | yes | – | yes | yes |
| `FormDefinition` / `FormResponse` | forms | yes | – | yes | yes |
| `NotificationPreference` | preferences | yes | – | yes | yes |
| `AuditLog` | audit | yes | – | yes | append-only |
| `DemographicChangeRequest` | review/workflow | yes | – | yes | staff review |
| `OutboundActionQueue` | integration boundary | yes | carries HMS-shaped payload | yes | staged delivery | 
| `HmsSyncProgress` | integration boundary | yes | sync outcome ledger | yes | worker |

Rules that fall out of the table:

- **`sources = PORTAL`** means the portal (its clinic staff or intake) authored the
  row. The portal owns it fully: it may create, edit, annotate, and deprecate it.
  Reconciliation never touches it.
- **`source = HMS`** is a portal-owned **imported representation** of a fact that
  exists in the HMS. The portal owns the copy and what it displays; the HMS owns
  the original. §6 defines how such copies behave.
- Integration tables (`OutboundActionQueue`, `HmsSyncProgress`) are portal tables
  about the integration, not HMS tables. They fail into the portal's normal backup,
  audit, and lifecycle story.

## 3. HMS integration boundary

All HMS contact lives behind one adapter, at the repository equivalent of
`lib/integrations/hms/`: **`apps/api/src/integrations/hms/`**. Nothing outside it
may:

- open a connection to the HMS,
- parse an HMS response, or
- name an HMS concept (`HmsPatient`, `HmsObservation`, fields like `global_id`) in
  portal domain code.

The adapter owns:

- the **HMS client** (transport, timeouts, retries, typed error mapping),
- **HMS authentication** (its own credentials; not portal user credentials),
- **HMS DTOs** (shapes as the HMS defines them),
- **external identity** (which portal `Patient.id` ↔ which HMS patient id),
- **mapping** between HMS DTOs and portal domain models, and
- **synchronization logic** (the pull worker, idempotent upserts, reconciliation,
  sync-state bookkeeping).

Portal domain code sees only portal entities. A clinical row that came from the
HMS is still just a portal `Observation` with `source = 'HMS'`, an `hmsId`, and
sync metadata — never an `HmsObservation`.

## 4. External identity mapping

"Which portal record is this HMS record?" is answered by a **mapping between portal
ids and external ids**, never by making an HMS id the portal's key.

- Portal entity primary keys are **always portal-owned `cuid()` values**
  (`Patient.id`, `Observation.id`, …).
- An HMS identifier appears only as a **nullable, unique external reference**
  column — `Patient.hmsPatientId`, and `hmsId` on the five clinical tables — with
  the meaning: *if set, this portal record corresponds to that external record in
  the HMS*. NULL means "no external counterpart", which is the normal state for
  portal-authored rows. One external id maps to at most one portal row (unique),
  so a re-import cannot duplicate.
- The conceptual model is:

```
Portal Patient          External identity
  id = portal cuid  ←→  source = HMS
                        externalId = HMS patient id
```

`hmsPatientId` / `hmsId` *are* that mapping, expressed as columns because the join
is one-to-one and the read is hot. If the portal later integrates a second external
system (or needs richer metadata — last confirmed external status, system of
origin), these columns migrate into a dedicated `ExternalIdentity` table rather
than growing more `hms*` columns on domain entities. **Do not add `hms*` columns
for new integrations.** See §12, migration M1.

## 5. Source-of-truth rules

There is **no blanket "HMS is the source of truth" rule.** Ownership is defined per
concept:

| Domain concept | Source of truth |
|---|---|
| Portal account, credentials, MFA, sessions, lockout | Medical Portal |
| Portal preferences, notifications | Medical Portal |
| Portal messaging | Medical Portal |
| Portal consent and sharing grants | Medical Portal |
| Portal review / workflow state | Medical Portal |
| Portal billing and payments | Medical Portal |
| Portal audit log | Medical Portal |
| Portal-authored clinical rows (`source = PORTAL`) | Medical Portal |
| Portal patient identity (Person, date of birth) | Medical Portal; HMS values are imported through the integration contract (§6), never by privilege |
| Hospital record held by the HMS | HMS |
| Clinical rows imported from the HMS (`source = HMS`) | The **portal's copy** is what the portal serves; its meaning traces to the HMS via `hmsId` + sync metadata |

A consequence the portal tests rely on: creating and reading portal-native records
never requires the HMS to be online, correct, or even configured.

## 6. Imported data rules

Clinical information from the HMS is represented as **portal-owned synchronized
representations** (`source = 'HMS'`), stored in the portal's own tables. It is not
a live fetch per page view and not a wholesale copy of the HMS schema.

Every imported row carries enough metadata to answer: where did it originate
(`source`); which external record it corresponds to (`hmsId`); when it was last
synchronized (`hmsFetchedAt`); whether it is current or observed-absent
(`hmsTombstonedAt`); and whether the portal may edit it in place (it may not for
HMS-imported rows — changes are staged, reviewed, and delivered outbound, §10).

Rules:

- **Portal autonomy.** A synchronizer may refresh, add, annotate, and tombstone
  the portal's *own* imported rows. It may never overwrite `source = PORTAL` rows —
  reconciliation is strictly scoped to rows the portal imported.
- **Freshness is metadata, not power.** `hmsFetchedAt` dates the last
  provably-successful pull. NULL is "never successfully fetched" and is treated as
  too stale to clear a safety check, never as "not yet". When the HMS is offline
  the portal keeps serving; stale-but-labelled beats absent, and beyond the hard
  ceiling per resource the portal refuses rather than guessing (see §7).
- **Empty means none only when the fetch succeeded.** An empty response from a
  provably-successful pull is authoritative ("this patient has no allergies"). A
  failed or ambiguous response must leave the cache untouched.
- **Deletions tombstone.** Absence on a successful pull sets `hmsTombstonedAt`
  rather than deleting the row, so a past clinical decision stays explainable.
- **Demographics import maps into `Person`** (portal-owned), mirroring
  `dateOfBirth`/contact fields per the integration contract. The portal's Person
  model decides where the value lives; the HMS supplies a value, never a table.
  `Person.dateOfBirth` is and stays the canonical portal DOB
  (`Patient.dateOfBirth` was removed by `20261201000000_remove_duplicate_dob`).

## 7. Failure behaviour

The portal must not collapse because the HMS is down. Defined behaviours:

| Situation | Behaviour |
|---|---|
| HMS unavailable / network timeout / auth failure / 5xx | Imported rows keep serving, labelled `hmsFetchedAt`; the next pull records `HmsSyncStatus.ERROR` and advances nothing. Portal-native features (auth, messaging, billing, portal-authored clinical data, review, sharing) are unaffected. |
| Synchronization failure | `HmsSyncProgress` records the failure (`ERROR` + `lastError`); cache untouched; `lastFetchedAt` advances only on success. |
| HMS returns incomplete/truncated data | Treated as not authoritative (§6): cache untouched, error recorded. |
| HMS returns stale data | The portal bounds **its own** staleness (refresh TTL + hard ceiling per resource) and labels served age. Beyond the hard ceiling the portal fails closed for that resource rather than rendering possibly-wrong data as current. The portal cannot detect HMS-internal staleness. |
| HMS record disappears | Tombstoned on a successful pull; stops being served immediately, retained for explanation. |
| Duplicate HMS record / duplicated import | The unique `hmsId` (and unique `hmsPatientId`) reject the duplicate; re-import becomes an in-place update, never a second portal row. |
| Portal writes | Never wait on, or fail because of, the HMS. Portal-native writes are portal-database transactions. Outbound delivery to the HMS is a staged queue (§10) that heals later. |

## 8. Synchronisation rules

Sync is an integration feature, **not** the portal's storage model. Current plan is
**pull-only** (HMS → portal). Bidirectional sync is *not* implemented and must not
be built prematurely (§10).

- The synchronizer works per patient × resource, upserting portal rows by `hmsId`
  and reconciling against the full, provably-successful response.
- Reconciliation only ever touches `source = 'HMS'` rows for that patient.
- Outcomes (success/empty-success/error) are recorded in `HmsSyncProgress`
  (§ F1 in the report below), because an empty successful pull writes no rows and
  "none" must still be a recorded fact.
- The pull worker, the HMS client, webhooks, and FHIR forwarding are **not yet
  implemented.** No code depends on the sync columns yet — they are dormant
  integration metadata with migration/tests guarding their behaviour.

## 9. FHIR boundary

FHIR is an interoperability **boundary**, not a third source of truth and not the
portal's internal schema.

```
Portal domain  ──FHIR mapper──►  FHIR representation   (read: /fhir/…)
HMS response   ──HMS mapper──►  Portal domain          (import: inside the adapter)
Portal domain  ──FHIR mapper──►  FHIR representation   (writes, clinician-only)
```

- Owned: FHIR **reads** map portal rows to FHIR R4; FHIR **writes** are
  clinician-only and currently persist portal-native rows to the portal database.
- Future **forwarding** of clinical writes to the HMS is a transport decision made
  inside the integration boundary, decided separately, and does not redesign the
  portal schema around FHIR resources.
- FHIR identifiers are portal ids; external (HMS) ids appear only where the mapping
  table says they do (§4).

## 10. Outbound actions

`OutboundActionQueue` exists because some portal actions are *intents* that the
portal's own database should persist and the HMS must ultimately apply
(appointment requests, questionnaire submissions, secure-message delivery,
approved demographic changes). The queue is a **portal table** — it lives in the
portal's Postgres, is backed up with it, and is audited with it. It is the portal's
integration boundary, not the HMS's persistence layer.

```
Portal action ──► Portal-owned outbound action (OutboundActionQueue)
                     │
                     ▼
                 HMS integration adapter ──► HMS
```

Delivery is transactional and idempotent (client-generated `idempotencyKey`), so a
retry after a network failure cannot double-book. **Until the HMS exposes an
idempotent write API, no delivery worker runs.** If bidirectional sync proves
unnecessary, the queue stays as a receipts/staging table or is removed — nothing in
the portal's core depends on it today.

## 11. Security / privacy boundary

- **No shared credentials.** Portal users are portal users. The adapter holds its
  own HMS credential and never exposes it or a portal session to the HMS.
- **Audit follows the data.** Portal reads and writes are logged in `AuditLog`.
  Imported rows carry the sync metadata that makes their provenance answerable.
- **Consent/review state is portal-owned** (`RecordShare`, `ProxyGrant`,
  `DemographicChangeRequest`) and never modelled as HMS data.
- **Least privilege per direction.** The portal imports only what it needs to
  serve; it exports only staged, reviewed, idempotent outbound actions.
- HMS-supplied values cross the boundary **through the integration contract**, are
  validated by portal rules, and land in portal-owned fields. Nothing from the HMS
  is accepted as a portal identity column or primary key.

---

## Audit findings (what this document corrects)

Status: the correction is **conceptual + structural scoping, not a schema rewrite**.
As of this writing:

- **Zero application code paths** reference `hmsFetchedAt`, `hmsId`,
  `hmsPatientId`, `hmsTombstonedAt`, `ClinicalSource`, or `HmsSyncProgress`. The
  only non-schema uses are migration/behaviour tests. There is no HMS client, no
  worker, and no HMS configuration in `env.ts`.
- The portal is **already runtime-independent**: it boots and passes its full
  suite against only its own schema. 474 API tests + 9 web tests pass with no HMS
  anywhere.
- The pre-existing **coupling is therefore (a) schema columns** — external refs and
  sync metadata embedded in domain tables — and **(b) documentation framing** that
  called the portal a cache of an HMS-owned model. Both are corrected here: this
  document redefines ownership; the hms* columns are explicitly integration
  metadata (§2 header note), not domain identity.
- `Person.dateOfBirth` as canonical portal DOB is confirmed correct for portal
  reasons: Person is the portal identity record; `Patient` is a record-of-care that
  references a person. The guard-then-drop migration is preserved.

## Required database changes

**None required for correctness this phase.** The schema already satisfies the hard
invariants (portal-owned PKs, nullable unique external ids, provenance defaulting
to PORTAL, no HMS PKs, no shared database). Do **not** drop the hms* columns: they
are the agreed integration metadata and their behaviour is covered by
`apps/api/src/hms/migrations.test.ts`.

**Proposed migration sequence** (for the integration bring-up, each after a
decision; none implemented yet):

- **M1 — External identity table (optional, before multi-system).** If the portal
  integrates more external systems, add `ExternalIdentity`/`ExternalReference`
  and migrate `hmsPatientId`/`hmsId` data into it; leave the domain tables clean.
  Until then the existing columns *are* the mapping (§4).
- **M2 — Decision: keep sync metadata on clinical tables.** `hmsFetchedAt` /
  `hmsTombstonedAt` stay on the tables they describe (query simplicity, per-table
  staleness gate). They are documented integration metadata, not domain fields. If
  later evidence shows they should move, prefer a dedicated sync-metadata table in
  the integration layer.
- **M3 — Adapter + worker (deferred, blocked).** Build
  `apps/api/src/integrations/hms/` and the pull worker only after the HMS exposes
  a credential mechanism and a paged patient read API. This is the "HMS SECOND"
  half of the priority.
- **M4 — Staleness enforcement, FHIR forwarding, outbound delivery, webhooks**
  follow in that order, each gated on its own decision and the HMS capabilities.

**Do not** (and will not be done without approval): rewrite existing migrations,
drop the hms* columns, rename the DOB migration, make the HMS a deployment
requirement, or build the worker/webhooks/FHIR-forwarding/review workflows yet.

## Shipping notes

- Portal deployment = portal API + portal database only. HMS must not appear in
  the portal compose file or env file.
- Add to any future developer on-boarding: "Read this document before adding a
  column or an endpoint that mentions HMS."