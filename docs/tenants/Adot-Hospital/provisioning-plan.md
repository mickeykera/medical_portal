# Dedicated Database Provisioning Plan: Adot Internal Medicine (Adot-Hospital)

> **Document Status**: PROPOSED / APPROVAL GATE 5  
> **Date**: October 24, 2026  
> **Tenant**: Adot Internal Medicine  
> **Slug**: `Adot-Hospital`  
> **Hosting Model**: Hospital on-premises (`Dedicated database` isolation)  
> **Location**: Addis Ababa, Ethiopia (UTC+3, stored in UTC)

---

## 1. Executive Summary & Architecture Decision

Per the tenant configuration provided for **Adot Internal Medicine**, the requested hosting isolation model is **`Dedicated database`** on **`Hospital on-premises`** infrastructure.

Per Working Rule 6:
> *"If the isolation is a dedicated database or deployment, show me the provisioning plan before building it."*

This document specifies the technical design, deployment topology, database segregation, cryptographic key hierarchy, and credential boundaries prior to physical deployment.

---

## 2. Isolation Topology

The system supports two isolation models:
1. **Shared Database with PostgreSQL Row-Level Security (RLS) & EF Global Query Filters**: Multi-tenant database where every table implements `ITenantScoped`, enforced by PostgreSQL advisory RLS policies and EF Core query interception.
2. **Dedicated Database (Hospital On-Premises Deployment)**: Complete physical database segregation. Adot Internal Medicine operates its own PostgreSQL database (`adot_hospital_portal_db`) and clinical core database (`adot_hospital_openmrs_db`), eliminating any theoretical cross-tenant database co-location risk.

### Dedicated Database Architecture Diagram

```
       [ Hospital Local Area Network (LAN) / Workstations ]
                               |
                               v
                     +-------------------+
                     |   nginx-ingress   | (:5000 / :443)
                     |   (Same-Origin)   |
                     +-------------------+
                        |             |
           /api, /hubs, |             | / (Static SPA)
           /R4, /health |             |
                        v             v
       +---------------------------------------------+
       |   medical-portal backend (Host: adot-local) |
       +---------------------------------------------+
               | (Private Network: portal-internal)
               +--------------------------------------+
               |                                      |
               v                                      v
       +----------------------+             +---------------------+
       | PostgreSQL 17        |             | Redis 7             |
       | DB: adot_portal_db   |             | (Local cache, locks)|
       +----------------------+             +---------------------+
               |
               v (Private Network: core-internal)
       +---------------------------------------------+
       | OpenMRS 2.8+ / O3 Core                      |
       | DB: adot_openmrs_db (PostgreSQL 17)         |
       +---------------------------------------------+
```

---

## 3. Database Specification & Provisioning Steps

### 3.1 Logical Databases & Roles

| Database Name | Dedicated Role | Grants & Privileges | Purpose |
| :--- | :--- | :--- | :--- |
| `adot_portal_db` | `adot_portal_app` | `CONNECT`, `CREATE`, `SELECT, INSERT, UPDATE, DELETE` on schema `public` | Portal data: users, appointments, messaging, billing, audit logs |
| `adot_openmrs_db`| `adot_openmrs_app`| `CONNECT`, `CREATE`, `SELECT, INSERT, UPDATE, DELETE` on OpenMRS schema | Clinical core: chart, clinical orders, observations, encounters |

### 3.2 SQL Provisioning Script for PostgreSQL 17

```sql
-- Step 1: Create dedicated user roles (Passphrases supplied via environment secrets)
CREATE ROLE adot_portal_app WITH LOGIN PASSWORD 'STRICTLY_SUPPLIED_BY_HOSPITAL_SECRET';
CREATE ROLE adot_openmrs_app WITH LOGIN PASSWORD 'STRICTLY_SUPPLIED_BY_HOSPITAL_SECRET';

-- Step 2: Create isolated databases owned by dedicated roles
CREATE DATABASE adot_portal_db OWNER adot_portal_app ENCODING 'UTF8';
CREATE DATABASE adot_openmrs_db OWNER adot_openmrs_app ENCODING 'UTF8';

-- Step 3: Revoke public access
REVOKE ALL ON DATABASE adot_portal_db FROM PUBLIC;
REVOKE ALL ON DATABASE adot_openmrs_db FROM PUBLIC;

GRANT CONNECT ON DATABASE adot_portal_db TO adot_portal_app;
GRANT CONNECT ON DATABASE adot_openmrs_db TO adot_openmrs_app;
```

---

## 4. Cryptographic Key Derivation & Isolation

Each tenant is cryptographically isolated even at the storage layer:
1. **Deterministic HKDF (RFC 5869)**:
   - Root Salt: `adot_tenant_id.ToByteArray()`
   - Domain Info: `MedicalPortal-Tenant-Encryption-Key-v1`
   - Algorithm: HMAC-SHA256
   - Derived Key Length: 256 bits (AES-256-GCM)
2. **Master Key Fingerprint**: SHA-256 fingerprint displayed on tenant administration console (`fp_...`).
3. **Audit Chain Genesis Block**: Block 0 hash `0000000000000000000000000000000000000000000000000000000000000000`.

---

## 5. Seed Data Execution Plan

Upon spinning up `adot_portal_db`, the automated migration and seed pipeline executes:
```bash
dotnet run --project backend/src/MedicalPortal.Web -- --migrate --seed
```

### Seeded Facilities & Departments:
1. **Facility 1**: `Adot Internal Medicine Main Facility` (`ADOT_FAC_1`)
2. **Departments (10)**:
   - Cardiology (`CARDIOLOGY`)
   - Gastroenterology (`GASTROENTERO`)
   - Endocrinology (`ENDOCRINOLOG`)
   - Pulmonology (`PULMONOLOGY`)
   - Rheumatology (`RHEUMATOLOGY`)
   - Nephrology (`NEPHROLOGY`)
   - Laboratory Services (`LABORATORY_S`)
   - Imaging & Radiology (`IMAGING_AND_`)
   - Infusion Suite (`INFUSION_SUI`)
   - Preventive & Wellness Care (`PREVENTIVE_A`)
3. **Templates**: Weekly schedule templates for Monday–Friday (08:00–17:00 UTC) with 30-minute default slot increments.

---

## 6. Required Hospital Credentials & Endpoints

To commission the on-premises deployment, the hospital IT administration must supply:

| Integration | Required Credential / Endpoint | Purpose | Safe Fallback? |
| :--- | :--- | :--- | :--- |
| **HMS / Custom API** | `HMS_BASE_URL` (HTTPS) | Synchronization with hospital HMS | ❌ No fallback secrets. Fake adapter in dev only. |
| **HMS / Custom API** | `HMS_WEBHOOK_SECRET` ($\ge 32$ chars) | Webhook HMAC-SHA256 signature verification | ❌ Required in prod |
| **Payment Gateway** | `OTHER_PAYMENT_GATEWAY_API_KEY` | Telebirr / CBE Birr / local payment API key | ❌ Sandbox fake for tests |
| **Payment Gateway** | `OTHER_PAYMENT_WEBHOOK_SECRET` | Local payment callback verification | ❌ Required in prod |
| **Notifications** | `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD` | Hospital on-premises SMTP relay | ❌ No demo credentials |
| **Notifications** | `LOCAL_SMS_GATEWAY_URL`, `LOCAL_SMS_API_KEY` | Ethio Telecom SMS gateway endpoint | ❌ No demo credentials |
| **Host Domain** | `HOSPITAL_HOST_DOMAIN` (e.g. `portal.adot.et`) | Host header tenant resolution and TLS cert | ❌ Required |

---

## 7. Approval Request

Before proceeding with dedicated container orchestration and database instantiation for Adot Internal Medicine, please review and confirm this provisioning plan.
