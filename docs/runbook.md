# Medical Portal Production Runbook: Backup, Restore & Rollback

This runbook establishes standard operating procedures (SOPs) for disaster recovery, continuous archiving, point-in-time recovery (PITR), application rollbacks, and database migration rollbacks for the Springfield Health Medical Portal.

---

## 📋 Table of Contents
1. [Architecture & Storage Overview](#1-architecture--storage-overview)
2. [Backup Procedures](#2-backup-procedures)
   - [Automated Full Database Backups](#21-automated-full-database-backups)
   - [Continuous WAL Archiving & Point-In-Time Recovery (PITR)](#22-continuous-wal-archiving--point-in-time-recovery-pitr)
   - [HIPAA Retention & Encryption at Rest](#23-hipaa-retention--encryption-at-rest)
3. [Restore Procedures](#3-restore-procedures)
   - [Restoring from Full Backup Dump](#31-restoring-from-full-backup-dump)
   - [Executing Point-In-Time Recovery (PITR)](#32-executing-point-in-time-recovery-pitr)
   - [Post-Restore Verification & Integrity Audit](#33-post-restore-verification--integrity-audit)
4. [Rollback Procedures](#4-rollback-procedures)
   - [Application Container Rollback (Zero Downtime)](#41-application-container-rollback-zero-downtime)
   - [Database Migration Rollback (EF Core)](#42-database-migration-rollback-ef-core)
   - [Emergency Circuit Breakers & Safe Modes](#43-emergency-circuit-breakers--safe-modes)
5. [Operational Health & Diagnostics](#5-operational-health--diagnostics)

---

## 1. Architecture & Storage Overview

The medical portal runs on a clustered topology:
- **Application Tier**: Clustered stateless ASP.NET Core 8 Web API nodes (`medical-portal-1`, `medical-portal-2`) behind an Nginx reverse proxy.
- **Relational Data**: PostgreSQL 17/16 with AES-256 application-level field encryption (`AesGcmFieldEncryptionService`) and cryptographic audit hash chaining.
- **Cache & Real-Time Backplane**: Redis 7 Alpine for SignalR cross-node hub fan-out.
- **Health Probes**: Liveness (`/health/live`), Readiness (`/health/ready`), and Comprehensive JSON diagnostics (`/health`).

---

## 2. Backup Procedures

### 2.1 Automated Full Database Backups

Full logical backups are generated daily using PostgreSQL's native custom archive format (`-Fc`), which supports compression, parallel restore, and selective table reconstruction.

#### Backup Execution Script:
```bash
#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="/var/backups/medical_portal"
TIMESTAMP=$(date -u +"%Y%m%d_%H%M%SZ")
BACKUP_FILE="${BACKUP_DIR}/medical_portal_${TIMESTAMP}.dump"
ENCRYPTED_FILE="${BACKUP_FILE}.enc"

mkdir -p "${BACKUP_DIR}"

echo "[$(date -u)] Starting PostgreSQL full backup..."
pg_dump \
  -h "${DB_HOST:-postgres}" \
  -p "${DB_PORT:-5432}" \
  -U "${DB_USER:-postgres}" \
  -d "${DB_NAME:-medical_portal}" \
  -Fc \
  -Z 6 \
  -f "${BACKUP_FILE}"

echo "[$(date -u)] Encrypting backup with AES-256..."
openssl enc -aes-256-gcm -pbkdf2 -iter 100000 \
  -in "${BACKUP_FILE}" \
  -out "${ENCRYPTED_FILE}" \
  -pass "env:BACKUP_ENCRYPTION_KEY"

# Securely remove unencrypted plaintext dump
rm -f "${BACKUP_FILE}"

echo "[$(date -u)] Uploading encrypted backup to immutable off-site vault..."
aws s3 cp "${ENCRYPTED_FILE}" "s3://${BACKUP_S3_BUCKET}/daily/${TIMESTAMP}.dump.enc" \
  --object-lock-mode COMPLIANCE \
  --object-lock-retain-until-date "$(date -u -d '+30 days' +'%Y-%m-%dT%H:%M:%SZ')"

rm -f "${ENCRYPTED_FILE}"
echo "[$(date -u)] Backup completed successfully."
```

### 2.2 Continuous WAL Archiving & Point-In-Time Recovery (PITR)

To protect against catastrophic data loss or accidental deletion between daily snapshots, write-ahead logs (WAL) are streamed continuously:
1. In `postgresql.conf`:
   ```ini
   wal_level = replica
   archive_mode = on
   archive_command = 'test ! -f /wal_archive/%f && cp %p /wal_archive/%f'
   archive_timeout = 300 # Forces a WAL switch every 5 minutes
   ```
2. The `/wal_archive` volume is replicated to cloud object storage with versioning enabled.

### 2.3 HIPAA Retention & Encryption at Rest

In compliance with **HIPAA Security Rule 45 CFR § 164.312(c)(1)**:
- **Daily Backups**: Retained for 30 days.
- **Weekly Backups**: Retained for 12 months.
- **Annual Snapshots**: Retained for a minimum of 6 years in immutable WORM compliance storage.
- **Encryption**: Backups are encrypted before egress using AES-256 with keys managed outside the application environment.

---

## 3. Restore Procedures

### 3.1 Restoring from Full Backup Dump

Use this procedure when provisioning a disaster recovery instance or restoring after a hardware failure.

#### Step 1: Drain Application Traffic
Isolate the database from application traffic to ensure clean restoration:
```bash
docker compose --profile cluster stop medical-portal-1 medical-portal-2
```

#### Step 2: Decrypt the Backup
```bash
openssl enc -d -aes-256-gcm -pbkdf2 -iter 100000 \
  -in "/var/backups/medical_portal_20261023_200000Z.dump.enc" \
  -out "/tmp/restore_target.dump" \
  -pass "env:BACKUP_ENCRYPTION_KEY"
```

#### Step 3: Execute `pg_restore`
```bash
# Clean existing schema and restore database objects and data
pg_restore \
  -h "${DB_HOST:-postgres}" \
  -p "${DB_PORT:-5432}" \
  -U "${DB_USER:-postgres}" \
  -d "${DB_NAME:-medical_portal}" \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  /tmp/restore_target.dump

# Securely wipe plaintext file
shred -u /tmp/restore_target.dump
```

### 3.2 Executing Point-In-Time Recovery (PITR)

When recovering to a specific second before a corruption or malicious action (e.g. `2026-10-23 20:15:00 UTC`):

1. Stop PostgreSQL service:
   ```bash
   docker compose stop postgres
   ```
2. Restore the latest base backup preceding the target recovery time into the data directory.
3. Configure `recovery.signal` in the PostgreSQL data directory:
   ```bash
   touch /var/lib/postgresql/data/recovery.signal
   ```
4. Append recovery settings in `postgresql.conf`:
   ```ini
   restore_command = 'cp /wal_archive/%f %p'
   recovery_target_time = '2026-10-23 20:15:00 UTC'
   recovery_target_action = 'promote'
   ```
5. Start PostgreSQL and verify recovery logs:
   ```bash
   docker compose up -d postgres
   docker compose logs -f postgres | grep -E "restored log file|recovery stopping at|archive recovery complete"
   ```

### 3.3 Post-Restore Verification & Integrity Audit

Before routing production traffic back to restored instances:
1. **Apply Migrations**:
   Ensure database schema matches the running application version:
   ```bash
   dotnet run --project src/MedicalPortal.Web -- --migrate
   ```
2. **Verify Cryptographic Audit Hash Chain**:
   Run the tamper verification check to guarantee audit records are uncorrupted:
   ```bash
   # Executes AuditLogHasher tamper chain verification
   dotnet test tests/MedicalPortal.Tests --filter "FullyQualifiedName~PhiAuditAndComplianceTests.AuditLog_HashChain_ValidatesTamperFreeIntegrity"
   ```
3. **Verify Health Probes**:
   ```bash
   curl -s http://localhost:5000/health | jq .
   ```
   Confirm all entries (`database`, `redis`, `payment_gateway`, `hms`, `notifications`) report `"status": "Healthy"`.

---

## 4. Rollback Procedures

### 4.1 Application Container Rollback (Zero Downtime)

When rolling back to a previous application version (e.g. `v1.4.1`):

1. Update the image tag in `docker-compose.yml` or deployment manifest:
   ```yaml
   image: medical-portal:v1.4.1
   ```
2. Perform rolling replacement node by node:
   ```bash
   # Node 1: Pull and restart
   docker compose --profile cluster up -d --no-deps medical-portal-1
   
   # Verify Node 1 is healthy before proceeding
   curl --fail http://localhost:8080/health/ready
   
   # Node 2: Pull and restart
   docker compose --profile cluster up -d --no-deps medical-portal-2
   ```

### 4.2 Database Migration Rollback (EF Core)

All schema changes adhere to the **Expand-Contract Pattern** (non-breaking additions deployed first; removals deployed only after all code stops using old columns).

#### Scripting a Down-Migration:
To generate an idempotent SQL script to roll back to a specific migration target:
```bash
# Generate SQL script rolling back from Current to TargetMigration
dotnet ef migrations script \
  --project src/MedicalPortal.Infrastructure \
  --startup-project src/MedicalPortal.Web \
  <CurrentMigrationName> <TargetMigrationName> \
  --output ./rollback.sql
```

#### Applying Migration Reversion:
```bash
# Rolls back EF Core database schema directly to target migration
dotnet ef database update <TargetMigrationName> \
  --project src/MedicalPortal.Infrastructure \
  --startup-project src/MedicalPortal.Web
```

### 4.3 Emergency Circuit Breakers & Safe Modes

If an external integration (Stripe, HMS, or SMS/Email provider) suffers an upstream outage, switch to safe mode via environment variables without code modification:

| Variable | Normal Setting | Emergency Safe Mode Setting | Behavior |
|---|---|---|---|
| `STRIPE_USE_FAKE` | `false` | `true` | Activates in-memory payment stub; queues payments for asynchronous retry. |
| `HMS_USE_FAKE` | `false` | `true` | Isolates app from HMS API downtime; caches local EHR updates. |
| `NOTIFICATION_EMAIL_PROVIDER` | `SendGrid` | `Fake` (or `AwsSes`) | Prevents blocked background reminder threads if vendor API fails. |
| `RateLimiting:GeneralApiPermitLimit` | `150` | `300` | Relaxes API rate limiting during legitimate emergency surges. |

Restart the affected application instances after updating environment variables:
```bash
docker compose --profile cluster up -d medical-portal-1 medical-portal-2
```

---

## 5. Operational Health & Diagnostics

### Dependency Health Check Endpoints
- **Liveness**: `GET /health/live` (HTTP 200 if process alive)
- **Readiness**: `GET /health/ready` (HTTP 200 only if PostgreSQL and core dependencies ready)
- **Full Report**: `GET /health`

#### Sample Full Health Response:
```json
{
  "status": "Healthy",
  "totalDurationMs": 4.12,
  "entries": {
    "database": {
      "status": "Healthy",
      "durationMs": 1.25,
      "description": "Database (Npgsql.EntityFrameworkCore.PostgreSQL) is operational.",
      "data": {
        "provider": "Npgsql.EntityFrameworkCore.PostgreSQL",
        "latencyMs": 1
      }
    },
    "redis": {
      "status": "Healthy",
      "durationMs": 0.85,
      "description": "Redis backplane operational (0.4ms).",
      "data": {
        "enabled": true,
        "endpoint": "redis:6379",
        "pingMs": 0.4
      }
    },
    "payment_gateway": {
      "status": "Healthy",
      "durationMs": 0.05,
      "description": "Payment gateway is operational.",
      "data": { "mode": "stripe", "ready": true }
    },
    "hms": {
      "status": "Healthy",
      "durationMs": 0.1,
      "description": "HMS integration client is configured.",
      "data": { "mode": "live", "ready": true }
    },
    "notifications": {
      "status": "Healthy",
      "durationMs": 0.02,
      "description": "Notification delivery subsystem is operational (Email: SendGrid, SMS: Twilio).",
      "data": { "emailProvider": "SendGrid", "smsProvider": "Twilio" }
    }
  }
}
```
