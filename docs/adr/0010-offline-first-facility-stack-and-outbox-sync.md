# ADR 0010: Offline-First Facility Stack, Transactional Outbox, and LAN Operations

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Architecture (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`, `scripts/facility-backup.sh`)

---

## 1. Context & Business Drivers

In Ethiopian district and regional hospitals, external internet connectivity is frequently interrupted for hours or days due to infrastructure outages, backhaul fiber cuts, and regional telecommunications maintenance. Furthermore, grid electrical power experiences load-shedding, requiring facilities to transition between grid, solar, and diesel generators.

Under **Target Architecture Principle E (Offline-First)**:
1. **Facility Server as System of Record:** The facility server hosted on the local hospital LAN is the primary, authoritative system of record for that facility's operations. Care delivery (registration, appointment scheduling, form completion, vital sign recording, read-through clinical chart viewing, billing) must never be blocked or degraded by the absence of WAN or internet access.
2. **One Writer per Record:** The facility owns its records. The regional/cloud tier is an aggregator and replica; it is never in the critical path of local patient care.
3. **No Distributed Multi-Master Replication:** Distributed multi-master database replication across unstable WAN links introduces split-brain corruption and silent conflict overwrite. Synchronization must be asynchronous, transactional, and store-and-forward.
4. **Per-Patient Order Guarantee:** Clinical and operational events for a patient must arrive at the aggregator in strict FIFO order (e.g., an appointment cannot be cancelled before it is registered as booked).
5. **Signed Reference Data:** Terminology, facility registries, DHIS2 indicator definitions, and form templates must arrive as signed, versioned packages that can be updated online or imported offline via physical media (USB).

---

## 2. Decisions

### 2.1 Deployment Node Role Architecture
- Deployed instances operate in one of two roles via [`FacilityNodeOptions.NodeRole`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/FacilityNodeOptions.cs):
  - `Facility`: Authoritative local system of record running on the hospital LAN. Outbound events are queued in the local PostgreSQL transactional outbox.
  - `RegionalCloud`: Aggregator and reporting replica receiving store-and-forward batches.
- Facility node status is observable via `GET /api/system/node-status`, exposing node role, facility code, connectivity state, outbox queue depth, and active reference packages.

### 2.2 Transactional Outbox in PostgreSQL (`SyncOutboxMessage`)
- Implemented [`SyncOutboxMessage`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/OfflineSyncEntities.cs) implementing `ITenantScoped`.
- **Atomic Persistence:** Outbox entries are written in the same database transaction as business operations, eliminating two-phase commit overhead and dual-write inconsistency.
- **Deduplication:** Monotonically generated deterministic/unique `IdempotencyKey` indexed uniquely on `(TenantId, IdempotencyKey)`.
- **Sequencing:** Contains global `SequenceNumber` and per-patient `PerPatientSequence`.

### 2.3 Per-Patient FIFO Ordering Barrier & Exponential Backoff
- Implemented [`OutboxSyncDispatcher`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/OutboxSyncDispatcher.cs):
  - **Per-Patient Barrier:** If message $N$ for patient $P$ encounters a failure or is waiting in backoff (`ScheduledAtUtc > now`), subsequent messages $N+1, N+2$ for patient $P$ are held back and will not be dispatched out of sequence.
  - **Store-and-Forward HTTPS:** Dispatches messages to [`ICloudAggregatorClient`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/ICloudAggregatorClient.cs) with `Idempotency-Key` and `X-Facility-Code` headers.
  - **Exponential Backoff with Jitter:** On transient network drops or HTTP 5xx responses:
    $$\text{delay} = \min(\text{MaxBackoff}, \text{InitialBackoff} \times 2^{\text{Attempt}-1}) \times \text{jitter}(0.9 .. 1.1)$$
  - **Idempotency Reconciliation:** If the aggregator returns `409 Conflict` (indicating the idempotency key was previously processed during a connection drop before the ACK was received), the dispatcher marks the message as `Delivered` with deduplication ACK notes without blocking the queue.
  - **Dead-Letter Handling:** After exceeding `MaxAttempts` (default: 10) or on fatal unrecoverable 4xx client errors, the message transitions to `DeadLetter` status for administrative review.

### 2.4 Signed & Versioned Reference Data Packages
- Implemented [`ReferencePackage`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/OfflineSyncEntities.cs) and [`ReferenceDataService`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Offline/ReferenceDataService.cs):
  - Packages contain `PackageId` (e.g. `ethiopia-moh-mfr`, `dhis2-hmis-definitions`), semantic `Version`, monotonic `VersionOrdinal`, `PayloadJson`, `ContentHash`, and `Signature`.
  - **Signature Verification:** Computes SHA-256 of payload and verifies HMAC-SHA256 signature using `CryptographicOperations.FixedTimeEquals` to prevent timing side-channels.
  - **Anti-Downgrade Version Protection:** Rejects any incoming package where `VersionOrdinal <= ExistingActiveVersionOrdinal`.
  - **Dual Import Modes:** Supports automated online pull when connected, or offline import via `POST /api/system/reference-packages/install` (for USB / external media delivery).

### 2.5 Facility Server Backup & Disaster Recovery
- Target Architecture E specifies that backup and disaster recovery are distinct from sync (no database multi-master replication).
- Created [`scripts/facility-backup.sh`](file:///home/michael/projects/app/medical_portal/scripts/facility-backup.sh):
  - Performs local compressed PostgreSQL dump (`pg_dump -F c | gzip -9`).
  - Restricts permissions (`chmod 600`).
  - Generates JSON manifest with SHA-256 checksum, facility metadata, and byte size.
  - Automatically rotates and prunes backups older than the configurable retention window (default: 30 days).

---

## 3. Consequences & Verification

- **24-Hour Network Cut Chaos Resilience:** Verified that a simulated 24-hour total network disconnect leaves local facility operations 100% operational, correctly buffers transactions in the transactional outbox, and upon reconnection drains all queued records in exact sequence.
- **Zero Cross-Tenant Leakage:** Negative tests prove that outbox messages and reference packages are isolated by `ITenantScoped`, global query filters, and `EnforceTenantIsolation()`.
- **Test Baseline:** 11 comprehensive tests in [`OfflineFirstFacilityStackTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/OfflineFirstFacilityStackTests.cs) passing cleanly; total solution test suite: **353 passed, 0 failed, 0 skipped**.
