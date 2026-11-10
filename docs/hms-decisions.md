# HMS integration — design decisions

**Status:** proposal for approval. No code changed.
**Date:** 2026-10-01

Resolves the three open decisions from `docs/local-first-hms-design.md` §6. Grounded
in an inspection of the current `Person` model, `PATCH /api/profile`, and the web
Settings form.

---

## 0. A blocking gap, found before the decisions

**Option A as generated cannot implement a TTL policy at all.** There is nowhere to
record when a row was last fetched from the HMS.

Verified against `schema.prisma` and the two generated migrations: the five clinical
tables gained `hmsId` and `source`, and nothing else.

- `createdAt` — when the portal row was created. Meaningless for freshness; a
  row pulled from the HMS today and last pulled last month both say "created at
  the original pull".
- `updatedAt` — changes on *any* write, including portal-local ones. It cannot
  distinguish "the HMS told us something new" from "something else touched this row".

Without a fetch timestamp, the portal cannot tell a 3-second-old cache entry from a
3-day-old one, so it cannot serve one and refuse the other. Staleness handling is
therefore **not implementable** on the schema as it stands, and every decision in §2
depends on it.

**This needs a third migration** — `hmsFetchedAt DateTime?` on the five clinical
tables. Nullable, like `hmsId`, because portal-authored rows were never fetched.
Indexed, because the staleness check runs per patient per request.

Approving this is decision **D7** in §6. I have not written it.

## 1. Demographic field ownership

### 1.1 What exists today

Verified from `schema.prisma`, `src/routes/profile.ts` and `apps/web/src/components/Settings.tsx`.

`PATCH /api/profile` accepts eleven fields. `email` and `dateOfBirth` are excluded
by an explicit comment — *"identity anchors tied to the account and the medical
record, and changing them needs verification this endpoint does not perform."*

The Settings form submits only **nine**: phone, addressLine1, addressLine2, city,
state, postalCode, preferredPharmacy, emergencyContactName, emergencyContactPhone.

**First and last name are accepted by the API but not exposed in the UI.** Name
editing is therefore already effectively unavailable to patients, which materially
reduces the cost of the proposal below.

Three facts about the current model that shape the answer:

1. **Two separate emails.** `UserAccount.email` (unique, the login identity) and
   `Person.email` (nullable, contact detail). Both are currently non-updatable
   through this endpoint — the code treats them as one concern, but they are not.
2. **`dateOfBirth` exists twice** — on `Person` and again on `Patient`. Neither is
   marked authoritative. Under an MPI this must be resolved.
3. **`mrn` is on `Patient`**, is MPI-assigned, and is read-only today.

### 1.2 The classification that drives the split

Fields differ by *what goes wrong when the portal and the HMS disagree*. Three
classes, not one rule:

- **Contact** — divergence annoys nobody and affects no clinical decision.
- **Identity** — divergence means a chart that does not match the person it is
  filed under. The failure is silent and hard to detect downstream.
- **Delivery** — the HMS acts on this value on the patient's behalf (mails
  results, sends statements). Divergence causes lost or misdirected mail.

### 1.3 Proposed split

| Field | Today (API) | Today (UI) | Proposed | Class | Why |
|---|---|---|---| --- | --- |
| `phone` | immediate | editable | **immediate** | Contact | No identity or delivery impact |
| `preferredPharmacy` | immediate | editable | **immediate** | Contact | A portal preference; the HMS has no use for it |
| `emergencyContactName` | immediate | editable | **immediate** | Contact | Who to call; not part of the patient's identity |
| `emergencyContactPhone` | immediate | editable | **immediate** | Contact | as above |
| `firstName` | immediate | **not exposed** | change request | Identity | Primary patient identifier; MPI matching depends on it |
| `lastName` | immediate | **not exposed** | change request | Identity | as above |
| `addressLine1/2` | immediate | editable | change request | Delivery | HMS mails results and statements here |
| `city` | immediate | editable | change request | Delivery | as above |
| `state` | immediate | editable | change request | Delivery | as above |
| `postalCode` | immediate | editable | change request | Delivery | as above |
| `dateOfBirth` | blocked | not exposed | change request + **ID verification** | Identity | Also a patient identifier; unverifiable changes break record matching |
| `sexAtBirth` | blocked | not exposed | change request, **clinician** review | Identity | Clinical datum; a correction must be clinician-asserted, not admin-typed |
| `Person.email` (contact) | blocked | not exposed | change request | Delivery | Notification destination |
| `UserAccount.email` (login) | n/a | n/a | **immediate + re-verification** | Portal | Login identity; the HMS has no say in it |
| `mrn` | read-only | read-only | read-only, HMS-assigned | — | Never patient-editable anywhere |

### 1.4 What this changes for the user

- **Six fields get easier, not harder**: phone, preferred pharmacy, and both
  emergency-contact fields were already editable and stay so. Contact email moves
  *into* editability-with-review, but is currently not editable at all.
- **Five fields get slower**: name and address. This is a real UX regression —
  today a patient who moves changes their address in one step.
- **Name editing is unaffected in practice** — it is not exposed today.
- **Login email becomes possible**, which it is not today.

### 1.5 The debatable one: address

I expect disagreement here, so the argument both ways, and I am not hiding behind
the recommendation:

**For immediate (my recommendation is *against* this):** patients move constantly;
blocking it is hostile; lab results arrive in the portal, not by post.

**For change request (recommended):** the HMS is not ours. It mails to the MPI
address and sends statements there. A patient who updates the portal address and
not the MPI one receives nothing, and nobody can see why — the portal will say "your
address is updated" while the mail goes elsewhere.

**Why I still recommend the review**, despite the UX cost: the portal's copy would
otherwise be *wrong*, not merely incomplete. Telling a patient their address is
updated when the system that will actually use it has not changed is worse than
making them wait. The fix is a short SLA and a visible pending state, not making
the portal lie.

**Alternative worth considering:** allow immediate address change plus an
outbound `DemographicChangeRequest` to the HMS in the same action — the patient sees
their change instantly, and it propagates asynchronously. This avoids the UX cost
entirely, at the price of the portal temporarily differing from the MPI, which is
exactly the divergence class I am trying to avoid. Defensible if you weight patient
experience above mail-delivery correctness.

### 1.6 The duplicate `dateOfBirth` — resolved

Verified by grep across API, seed, harness and web.

**Where each is written.** Both are always written together from one input, so they
cannot diverge through any existing code path:

- `auth/service.ts:170-182` — `person.create` with a nested `patient.create`, both
  from `input.dateOfBirth`.
- `prisma/seed.ts` — same literal into both.
- `testing/harness.ts:99,115` — same value into both.

Neither has an `UPDATE` path; `dateOfBirth` is excluded from `PATCH /api/profile`.

**Where each is read — and this is the finding.**

| Surface | Column |
|---|---|
| `GET /api/auth/me` managing list (`auth.ts:166,169`) | **Patient** |
| FHIR `Patient.birthDate` (`mappers.ts:217`, `PatientRow`) | **Patient** |
| Web clinician dashboard (`DoctorDashboard.tsx:351`) | **Patient** |
| `GET /api/profile` (`profile.ts:57,58`) | **Person** |
| Web Settings screen (`Settings.tsx:115`) | **Person** |

**The patient-facing Settings screen and the clinician-facing dashboard read the
same person's birthday from two different columns.** They agree today only because
creation writes both from one value — there is no database constraint, no
invariants check, and nothing that would stop them separating later. This is a
latent divergence, not a live bug, and it should be described as such.

**Recommendation: `Person.dateOfBirth` becomes the single canonical value, and
`Patient.dateOfBirth` is dropped.**

Rationale:

1. `Patient.personId` is required and unique, so every `Patient` has exactly one
   `Person`. Read-through is always safe — no case where a chart has no person.
2. It eliminates the ambiguity rather than picking a winner. Declaring one column
   "authoritative" leaves the other writable and invites the same bug back.
3. `Person` is the identity record, and date of birth is an attribute of identity.
   It belongs beside first name and last name, not beside `mrn`.
4. The two consumers that currently read `Patient.dateOfBirth` — FHIR
   `Patient.birthDate` and the FHIR `PatientRow` mapper — are internal reads
   through a relation. **No published contract changes**: FHIR still emits
   `birthDate` with the same value.
5. It aligns with the demographic decisions: `dateOfBirth` is Identity-class and
   blocked from patient self-editing (§1.3), which is a rule about the person.

**The cost:** `Person` also holds caregivers and payers who have no `Patient` row.
Their date of birth keeps living on `Person` with no duplication at all, which is
the correct shape.

**How the HMS MPI maps to it.** The MPI's date of birth mirrors to
`Person.dateOfBirth`, and only for people who are HMS-linked — determinable as
`Patient.hmsPatientId IS NOT NULL`, so no extra column is needed for that
distinction. A caregiver's own date of birth is portal-only and never overwritten.

**Migration (not written):** a guard-then-drop. First a `DO $$ … RAISE EXCEPTION`
block that aborts if any `patients.dateOfBirth` differs from its person's, so
divergence that happened outside code cannot be silently discarded; then drop the
column. Today that guard passes trivially.

**One consequence for migration 3:** if demographics are mirrored from the MPI,
`Person` needs `hmsFetchedAt` too, not just the five clinical tables. Demographic
staleness is governed by the same rules, and a mirrored address with no fetch
timestamp cannot be age-checked.

## 2. Clinical cache policy

Requires decision **D7** (§0). TTL is expressed against `hmsFetchedAt`.

### 2.1 Tiers

Two independent values, because they answer different questions:

- **Refresh TTL** — how often we re-pull proactively.
- **Hard staleness ceiling** — the point beyond which we refuse to serve at all.

| Resource | Refresh TTL | Hard ceiling | Reasoning |
|---|---|---|---|
| `Allergy` | 5 min | **30 min** | Drives prescribing blocks. A stale "no allergy" can let a fatal interaction through. |
| `Prescription` (MedicationRequest) | 5 min | **30 min** | Same class of harm: a stale med list misstates what the patient is taking. |
| `Appointment` | 5 min | 24 h | Cancellations must propagate promptly; a stale entry is recoverable by phone. |
| `Observation` — labs | 15 min | 24 h | A lab is a point-in-time fact. Yesterday's latest result is still the latest result. |
| `Observation` — vitals | 15 min | 24 h | as above |
| `Condition` | 1 h | 24 h | Changes slowly; an hour-old problem list is not a clinical risk. |
| `Observation` — immunizations | 1 h | 7 d | Registry reconciliation is not real-time anywhere. |

Deliberately coarse. Four tiers would be premature — the only tier that earns
precision is the one where staleness has teeth.

### 2.2 Behaviour when the HMS is unavailable

1. **Serve the cache, and say how old it is.** Stale-but-labelled beats absent.
   The UI receives `hmsFetchedAt` and renders "results as of 14:32".
2. **Beyond the hard ceiling, do not serve.** Return an explicit unavailable state
   for that resource type. Never render a chart that might be wrong as though it
   were current.
3. **Never fall back to portal-authored rows pretending to be HMS data.**
   `source = PORTAL` rows are a different thing and must not be presented as the
   HMS's view.
4. **Degrade per resource type, not per patient.** An HMS that is up for labs but
   erroring on medications should not take out the whole chart.

### 2.3 Where fresh data is mandatory

Staleness is normally a usability question. There is exactly one place it is a
safety question:

> **The allergy check before prescribing must not run against a cache older than
> the hard ceiling.** If `Allergy.hmsFetchedAt` is beyond 30 minutes, refuse the
> prescription rather than check it against data we know is stale.

This is the fail-closed direction the existing `mayPrescribe` already takes for
uncoded allergies, and it should extend to stale ones.

Also mandatory-fresh, for non-clinical reasons:

- Anything that **writes** (an outbound patient action) — the portal must not
  submit against a stale view of the chart.
- The **patient-identifier display** on any screen where a mismatch has
  consequences.

### 2.4 How webhooks and TTL interact

They are not alternatives; they do different jobs:

- **TTL is the correctness guarantee.** It bounds staleness regardless of what
  the HMS does or whether events arrive. Remove it and correctness depends on the
  HMS behaving.
- **A webhook is an accelerator.** It can only make a refresh happen *sooner*.

**A webhook can never replace the TTL**, because events are lost — HMS downtime,
network, our downtime, silent drops. A lost event with no TTL means permanent
staleness, which is the failure this whole cache policy exists to prevent.

Invalidation therefore means: mark the affected patient dirty and pull on next
read. The TTL still fires, and re-pull is idempotent by `hmsId`, so a webhook and
a TTL expiring together cost one extra no-op query.

## 2.5 Edge cases, and why three states are not enough

Added after review. The three states above are **not sufficient on their own** —
they only work once a fourth, unstated case is given a rule.

### The missing state: never successfully fetched

`hmsFetchedAt IS NULL` means no fetch has ever succeeded. It is not "fresh", and it
is not "old" — the age arithmetic does not apply.

**It must be classified as TOO STALE, unconditionally.** Otherwise the natural
reading of "no timestamp yet" is "nothing to worry about", and the portal would
clear an allergy check on a patient it has never successfully read. That is the
exact failure this design exists to prevent.

### The full classification

| `hmsFetchedAt` | Age | State | Servable |
|---|---|---|---|
| `NULL` | — | **TOO STALE** | No. Fail closed. |
| set | ≤ TTL | Fresh | Yes |
| set | TTL – ceiling | Stale | Yes, labelled with its age |
| set | > ceiling | **TOO STALE** | No. Fail closed. |

### HMS unavailable, or polling failing repeatedly

`hmsFetchedAt` advances **only on a successful response**. An outage therefore
needs no special handling: the cache does not refresh, the timestamp stops moving,
and the entry ages through *stale* into *too stale* on its own. Staleness is derived
from a timestamp rather than tracked as separate state, so the outage path and the
normal path are the same path.

Needs operational work, not schema: backoff between polls, and an alert when a
patient passes their hard ceiling.

### The HMS returns an empty result — the dangerous one

Empty is ambiguous. It means either *"this patient has no allergies"* or *"the
query silently returned nothing"*. Treating them the same is unsafe in **both**
directions:

- Treat empty as *"no change"* and an allergy genuinely removed in the HMS stays
  active in the portal, blocking prescriptions for that drug indefinitely.
- Treat empty as authoritative **without first establishing the response
  succeeded**, and a transient HMS fault silently deletes a patient's entire
  allergy list — the highest-consequence failure available here.

**Rule: reconciliation runs only on a response that is unambiguously successful and
semantically complete.** A 200 with an empty bundle is authoritative and means
"none". A 5xx, a timeout, a truncated body or an unparseable payload is *not*
authoritative and must leave the cache untouched.

### An HMS record disappears

The same pass marks HMS-sourced rows whose `hmsId` was absent from the latest
**successful** pull.

**Tombstone, do not hard delete.** A removed allergy must stop blocking
prescriptions immediately, but the record should survive a retention period — "this
allergy was removed on date X by user Y" is the answer to a future question about
why a prescription was or was not allowed. A hard delete destroys the ability to
explain a past clinical decision.

Portal-authored rows (`source = PORTAL`) are never touched by reconciliation.

### A limit worth stating plainly

The portal bounds **its own** staleness. It cannot detect an HMS that answers
successfully with data that is itself out of date. Our TTL is not a claim about the
HMS's freshness.

## 3. Webhooks vs polling

### 3.1 Technical facts

Independent of any recommendation:

1. **A webhook can be lost.** HMS downtime, network loss, our downtime, silent
   drops. Delivery guarantees from a hospital system are rarely strong.
2. **Polling cannot be lost.** The next tick happens regardless.
3. **Therefore TTL is mandatory in both designs**, and the real question is only
   whether staleness is *bounded by TTL alone* or *usually beaten by an event*.
4. **Per-record invalidation is easy in both.** Polling re-reads the patient;
   a webhook carries the patient id. Neither requires partial-row granularity —
   invalidating the whole patient chart is correct and simple.
5. **Recovery after a missed event differs sharply.** With polling, recovery is the
   next tick. With webhooks alone, recovery requires replay from the HMS — a
   capability that may not exist — or falling back to polling anyway.
6. **Webhooks require more from the HMS**: push support, a reachable public
   endpoint, signature scheme, retry semantics, and a defined answer to "what
   happened while you were down?".
7. **Polling requires only** outbound network access and a scheduler.
8. **Polling cost scales with patient count; webhook cost scales with change
   volume.** For a low-churn clinic, polling is cheaper per patient and webhooks
   are cheaper per patient at scale — neither is decisive here.

### 3.2 Evaluated for *this* portal

| Criterion | Webhooks + TTL fallback | Polling + TTL |
|---|---|---|
| Correctness | Equal — TTL is the guarantee either way | Equal |
| Stale clinical data | Lower *typical* staleness; same *worst* case | Bounded staleness exactly |
| Implementation complexity | Higher: endpoint, signature verification, replay handling, secret management | Lower: a scheduled job |
| HMS unavailable | Degrades to TTL fallback | Degrades to serving cache |
| Operational requirements | Public ingress, secret rotation, monitoring of event flow | Outbound egress, scheduler, HMS rate-limit headroom |
| Invalidate a specific record | Yes, but invalidating the patient is simpler | Yes, same |
| Recover from missed events | Needs HMS replay, else wait for TTL | Next tick |
| **Depends on unknown HMS capabilities** | **Yes — push support is unverified** | **No** |

The decisive row is the last one. The HMS's webhook capability is **an open
question we have not answered**, and option A requires betting the architecture on
it before knowing whether it exists.

### 3.3 Recommendation

**Option B — polling + TTL as the baseline. Add webhooks later as a pure
optimisation.**

Reasoning:

1. **It removes an unverified dependency.** Option A cannot be built at all until
   the HMS confirms push support, signature scheme and replay semantics. Option B
   can be built against any HMS that exposes reads.
2. **It has no lost-event failure mode.** Its worst case is bounded staleness,
   which §2 already defines as acceptable per resource type.
3. **It is strictly additive to choose A later.** This is the key property. TTL
   must exist either way, and a webhook layered on top of a working TTL poller is
   a small, isolated change — mark-dirty on event, pull on next read. Reversing
   that order is not possible: a webhook-first system with no TTL is unsafe from
   day one and can only be made safe afterwards.
4. **Volume does not favour webhooks here.** This is a single-clinic portal;
   polling cost is negligible and the operational surface of a public webhook
   endpoint is not.

**Reverse this recommendation if** the HMS turns out to emit a high-volume,
reliable event stream, or if it requires near-real-time propagation for something
clinically urgent. Both are worth asking about before committing.

## 4. Recommended architecture

```
   HMS ──poll──► Portal API ──► Postgres (cache, source=HMS)
                    │                hmsId + hmsFetchedAt
                    │
                    ├──► Browser (reads; receives staleness metadata)
                    │
                    └──► OutboundActionQueue ──poll──► HMS (transactional,
                                                           idempotent)
```

- Pull on read, gated by `hmsFetchedAt` vs the tier TTL.
- Proactive background refresh as a follow-up, not a prerequisite.
- Webhook endpoint added later, if justified — invalidation only, never
  correctness.
- Every cache read carries its age to the UI.
- One enforcement point for staleness, not per route.

## 5. Changes required eventually

### Schema (needs approval — D7, and D8 below)

- **Migration 3, required:** `hmsFetchedAt DateTime?` on `Observation`,
  `Condition`, `Prescription`, `Allergy`, `Appointment` **and `persons`**
  (demographics mirror from the MPI and need the same age-check — §1.6), plus indexes.
- **Migration 4, contingent on §1.6:** guard-and-drop `patients.dateOfBirth`.
- **Migration 5, optional:** queue-claim columns (`lockedAt`, `lockedBy`) so
  multiple workers cannot double-send.

### Code, by phase

| Phase | Work | Blocked on |
|---|---|---|
| 1 | HMS adapter: auth, client, timeouts, retries, typed error mapping | HMS API access |
| 2 | Pull worker: per-patient, per-resource, upsert by `hmsId` | 1 |
| 3 | Staleness policy: single enforcement point + `fetchedAt` in API responses | 2 |
| 4 | `DemographicChangeRequest` API + review queue + audit | D1–D4 |
| 5 | Patient action queue: enqueue endpoints, delivery worker | 1 |
| 6 | FHIR forwarding; **rewrite Phase 4 tests** to assert forwarding, not local persistence | 1 |
| 7 | Webhook endpoint (optional) | HMS push support |

Note phase 6: the 16 Phase 4 tests currently assert clinical rows land in the portal
database. Under this architecture that becomes false, and they must be rewritten to
assert the HMS outcome. Flagged in the design doc §5.1.

## 6. Decisions requiring your approval

Reviewed version. Each states the recommendation, what it costs technically, what
kind of thing it is, and how hard it is to reverse later.

| # | Decision | Recommended | Domain | Cost of reversing later |
|---|---|---|---|---|
| **D1** | Name changes: immediate, or change request? | Change request | Data ownership; patient safety (identity) | **Low** — the review queue is generic; "immediate" later is just not routing through it |
| **D2** | Address fields: immediate, or change request? | Change request | Data ownership; product behaviour | **Moderate** — if built review-first and you later want immediate-plus-async, that is a second code path, not a config flip |
| **D3** | `dateOfBirth`: never patient-editable, or changeable with ID verification? | Change request + ID verification | **Patient safety**; data ownership | **High** — identity verification is a real subsystem, expensive to build and hard to remove once patients rely on it |
| **D4** | `sexAtBirth`: admin review or clinician review? | Clinician review | Patient safety (clinical datum) | **Low** — the role gate on the review queue is cheap either way |
| **D5** | Should login email become patient-editable? | Yes, with re-verification | **Security** | **Moderate** — once email is a login identity it affects sessions, password reset and audit attribution; retrofitting is invasive |
| **D6** | Polling + TTL now, webhooks deferred? | Polling + TTL | Product behaviour only | **Very low** — webhooks are additive to a TTL poller |
| **D7** | Approve migration 3 adding `hmsFetchedAt`? | Yes — **required** | None semantically | **Very low** — additive, nullable, reversible |
| **D8** | Approve §1.6: make `Person.dateOfBirth` canonical and drop `Patient.dateOfBirth`? | Yes | Data ownership | **Moderate** — touches five read sites across API and FHIR, but all internal |

### Which genuinely need your product judgement

- **D2 (address)** — the only one with a real tension between patient experience
  and correctness. I recommend review, but I expect you to disagree, and §1.5 sets
  out the alternative in full.
- **D3 (date of birth verification)** — this commits you to an identity
  verification process: who verifies, against what, and what happens to a patient
  who cannot produce documents. That is an operational decision, not a technical one.
- **D5 (login email)** — a security posture decision. Making it editable is a
  genuine improvement but widens the account-takeover surface.

D1, D4, D6, D7 and D8 are technical or low-stakes, and I am content to proceed on
them unless you object.

## 7. Safety-critical assumptions

Each of these must hold for the cache design to be safe. If any is untrue in the
real HMS, the design needs revisiting rather than adjusting.

1. **No successful fetch means too stale.** `hmsFetchedAt IS NULL` is classified as
   TOO STALE, never as "no problem". Otherwise the portal clears an allergy check
   on a patient it has never read. (§2.5)
2. **An empty response is authoritative only when the response provably succeeded.**
   Retaining a deleted allergy blocks prescriptions forever; deleting an allergy
   list on a failed fetch is catastrophic. The success/failure distinction is the
   load-bearing part. (§2.5)
3. **Deletions tombstone rather than hard-delete**, so a removed allergy stops
   blocking immediately while remaining auditable. (§2.5)
4. **Reconciliation never touches `source = PORTAL` rows.** A reconciliation bug
   must not be able to delete a clinician-entered record.
5. **The portal bounds only its own staleness.** An HMS answering successfully with
   outdated data is undetectable from here. (§2.5)
6. **The allergy/prescription hard ceiling fails closed.** Beyond it, prescribe
   refuses rather than checks against data known to be old.

### Still open, not blocking

- **Does the HMS expose webhooks at all?** Determines whether D6 is a decision or
  a foregone conclusion, and whether §3.3 should be revisited.
- **What is the review SLA for demographic change requests?** "Reviewed within two
  business days" is a product commitment, and the address recommendation in D2 is
  only defensible with a short one.
- **Is `hmsPatientId` format-constrained by the HMS?** If UUIDs are mandated, that
  is a column-type choice on one column — not a reason to migrate the other 34
  models.
- **What does the HMS do with a partially-matching response?** §2.5 assumes a
  response is either complete or not. If the HMS can return a truncated result
  while reporting success, that assumption fails and reconciliation needs a
  per-resource completeness signal.

---

**Stopping here for approval.** No schema, migration, API, FHIR, test or polling
code was changed in producing this report. The only file written is this document.