# Operational Deployment Runbook: Adot Internal Medicine (`Adot-Hospital`)

> **Facility:** Adot Internal Medicine Main Facility (`ADOT_FAC_1`)  
> **Topology:** Hospital On-Premises, Dedicated Database & Clinical Core  
> **Document Version:** 1.0 (Production Release)  
> **Audience:** Hospital IT Systems Engineers, DevOps, Platform Administrators  

---

## 1. Hardware & Operating Environment Prerequisites

Prior to standing up the on-premises stack at Adot Internal Medicine:

| Component | Minimum Requirement | Recommended Specification |
|:---|:---|:---|
| **Host Operating System** | Ubuntu Server 22.04 LTS / Debian 12 | Ubuntu Server 24.04 LTS (Kernel 6.8+) |
| **CPU Architecture** | x86_64 / 4 Cores | 8 Cores (Intel Xeon / AMD EPYC) |
| **RAM** | 16 GB | 32 GB (ECC Recommended) |
| **Storage** | 100 GB SSD (RAID 1) | 500 GB NVMe SSD (RAID 10) |
| **Container Engine** | Docker Engine 24.0+ & Compose v2.20+ | Docker Engine 27.0+ & Compose v2.29+ |
| **Local Area Network** | Static LAN IP with DNS entry (`portal.adot.et`) | Dual NIC (LAN + Management VLAN) |
| **Uninterruptible Power** | 1500VA UPS with USB signaling | Smart UPS with auto-graceful shutdown |

---

## 2. Pre-Deployment Configuration Steps

### 2.1 Clone Repository & Set File Permissions
```bash
git clone https://github.com/hospital-platform/medical_portal.git /opt/adot-portal
cd /opt/adot-portal
chmod +x scripts/*.sh
```

### 2.2 Configure Secrets & Environment File
Copy the Adot environment template to production `.env`:
```bash
cp .env.adot.example .env.adot
chmod 600 .env.adot
```

Edit `.env.adot` and inject strong, production-grade secrets:
```bash
# 1. Generate 48-byte cryptographically random JWT secret:
JWT_SECRET=$(openssl rand -base64 48)

# 2. Generate 256-bit (64 hex character) authenticated field encryption key:
FIELD_ENCRYPTION_KEY_HEX=$(openssl rand -hex 32)

# 3. Generate distinct passwords for Postgres users:
ADOT_POSTGRES_ROOT_PASSWORD=$(openssl rand -base64 32)
ADOT_PORTAL_DB_PASSWORD=$(openssl rand -base64 32)
ADOT_OPENMRS_DB_PASSWORD=$(openssl rand -base64 32)
```

> [!CAUTION]
> Per system security policy SEC-12 and SEC-13, the backend will refuse to boot in `Production` mode if default placeholder secrets, empty passwords, or derivational fallback keys are detected.

---

## 3. Deployment Execution

### 3.1 Start On-Premises Containers
Launch the dedicated Adot stack using Docker Compose:
```bash
docker compose -f docker-compose.adot.yml --env-file .env.adot up -d
```

### 3.2 Verify Container Health & Status
```bash
docker compose -f docker-compose.adot.yml ps
```
Expected output:
- `adot-postgres`: `Up (healthy)`
- `adot-redis`: `Up (healthy)`
- `adot-backend`: `Up (healthy)`
- `adot-proxy`: `Up` (Listening on LAN port 5000 / 80)

### 3.3 Validate Database Initialization
Inspect PostgreSQL logs to verify dedicated databases `adot_portal_db` and `adot_openmrs_db` were created with permissions granted:
```bash
docker logs adot-postgres
```

---

## 4. Automated Tenant Provisioning

Execute the automated tenant provisioning CLI against the local deployment:
```bash
# Obtain platform admin token, then run:
./scripts/provision-tenant.sh docs/tenants/Adot-Hospital/tenant-config.json http://localhost:5000 "$PLATFORM_ADMIN_TOKEN"
```

The script performs:
1. Schema and business rule validation.
2. WCAG 2.1 AA color contrast calculation for Adot primary color `#e01b24` (`4.81:1` against white — PASSES).
3. Creation of tenant `Adot-Hospital` with dedicated HKDF key fingerprint and genesis audit block.
4. Seeding of Organization `ADOT_HOSPITAL` and Facility `ADOT_FAC_1`.
5. Seeding of all 10 clinical and diagnostic departments:
   - Cardiology, Gastroenterology, Endocrinology, Pulmonology, Rheumatology
   - Nephrology, Laboratory Services, Imaging & Radiology, Infusion Suite, Preventive & Wellness Care
6. Seeding of weekly consultation schedule templates (Monday–Friday 08:00–17:00).

---

## 5. Verification Checklist

Execute the following post-deployment checks:

| Step | Test / Endpoint | Expected Result | Verified By |
|:---|:---|:---|:---:|
| **1. Gateway Health** | `curl -i http://localhost:5000/health` | HTTP `200 OK`, JSON status `healthy` | DevOps |
| **2. Tenant Resolution** | `curl -i -H "Host: portal.adot.et" http://localhost:5000/api/tenants/current` | HTTP `200 OK`, `slug: "Adot-Hospital"` | DevOps |
| **3. Design Tokens** | `curl -i -H "Host: portal.adot.et" http://localhost:5000/api/tenants/current/theme` | Primary color `#e01b24`, WCAG ratio `4.81:1` | UI Team |
| **4. Localization** | `curl -i -H "Host: portal.adot.et" http://localhost:5000/api/localization/am` | HTTP `200 OK`, Amharic translations loaded | Clinician |
| **5. Ethiopian Calendar** | Open browser to appointment booking view | Dual Ethiopian (Meskerem–Pagumē) and Gregorian calendar rendered | Receptionist |
| **6. OpenMRS Sync** | Check `/api/clinical/openmrs/status` | Inbound & outbound sync operational | IT Lead |

---

## 6. Day-2 Maintenance & Routine Operations

### 6.1 Automated Daily Backups
The on-premises stack includes an automated backup script [`scripts/facility-backup.sh`](file:///home/michael/projects/app/medical_portal/scripts/facility-backup.sh).
Add a cron job on the hospital host server:
```bash
# Run backup daily at 02:00 AM East Africa Time
0 2 * * * /opt/adot-portal/scripts/facility-backup.sh /var/backups/medical_portal >> /var/log/portal-backup.log 2>&1
```
Backups are:
- Compressed with `gzip -9`
- Accompanied by SHA-256 integrity manifests
- Secured with `chmod 600`
- Automatically rotated after 30 days

### 6.2 Module Toggling via Platform API
If hospital leadership decides to temporarily disable an integration or feature (e.g. `billing` during annual audit):
```bash
curl -X PATCH http://localhost:5000/api/platform/tenants/{tenantId}/modules \
  -H "Authorization: Bearer $PLATFORM_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"module": "billing", "enabled": false}'
```
The module is instantly disabled at the API level with zero server restarts required.

### 6.3 Incident Response & Offline Operation
In the event of complete WAN cut:
- Hospital workstations on LAN continue unhindered.
- All transactional outbox events queue in PostgreSQL `SyncOutboxMessage`.
- Upon WAN reconnection, outbox sync drains automatically in strict per-patient FIFO order.
