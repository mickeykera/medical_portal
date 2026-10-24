# Tenant Onboarding Handover: Adot Internal Medicine (`Adot-Hospital`)

> **Document Status**: READY FOR REVIEW  
> **Date**: October 24, 2026  
> **Tenant Name**: Adot Internal Medicine  
> **Tenant Slug**: `Adot-Hospital`  
> **Region**: Other: Ethiopia, Addis Ababa  
> **Time Zone**: UTC (display and clinical entry: Africa/Addis_Ababa, UTC+3)  
> **Languages**: `en` (configured; `am`, `om` scheduled for Phase 6)

---

## 1. Configuration Summary

The tenant configuration has been validated and applied strictly via the tenant settings engine without code forks:

```json
{
  "tenant": {
    "name": "Adot Internal Medicine",
    "slug": "Adot-Hospital",
    "region": "Other: Ethiopia, Addis Ababa",
    "timezone": "UTC",
    "languages": ["en"],
    "facilities": 1,
    "departments": [
      "Cardiology",
      "Gastroenterology",
      "Endocrinology",
      "Pulmonology",
      "Rheumatology",
      "Nephrology",
      "Laboratory Services",
      "Imaging & Radiology",
      "Infusion Suite",
      "Preventive & Wellness Care"
    ]
  },
  "branding": {
    "primaryColor": "#e01b24",
    "logo": "to be supplied",
    "supportEmail": ""
  },
  "modules": {
    "enabled": [
      "appointments",
      "messaging",
      "billing",
      "refills",
      "proxy",
      "sharing",
      "fhir",
      "findcare",
      "forms"
    ],
    "disabled": []
  },
  "hosting": {
    "provider": "Hospital on-premises",
    "dataResidency": "same country as hospital",
    "isolation": "Dedicated database"
  },
  "integrations": {
    "ehr": "Custom API",
    "payments": "Other gateway",
    "notifications": "Email and SMS",
    "staffSignIn": "Local accounts with MFA"
  },
  "security": {
    "mfaStaff": true,
    "mfaPatients": true,
    "idleTimeoutMinutes": 15,
    "retentionYears": 20,
    "breakGlass": true
  }
}
```

---

## 2. Branding & WCAG 2.1 AA Analysis

- **Tenant Primary Color**: `#e01b24`
- **Contrast Analysis**:
  - Ratio against White (`#ffffff`): **`4.81:1`**
    - Normal Text ($\ge 4.5:1$): **PASSES**
    - Large Text ($\ge 3.0:1$): **PASSES**
  - Ratio against Black (`#000000`): **`4.36:1`**
    - Large Text ($\ge 3.0:1$): **PASSES**
    - Normal Text ($\ge 4.5:1$): **FAILS** (Use white text over primary color buttons and headers).

---

## 3. Seeded Organizational Structure

- **Facility 1**: Adot Internal Medicine Main Facility (`ADOT_FAC_1`)
- **Departments (10)**:
  1. Cardiology (`CARDIOLOGY`)
  2. Gastroenterology (`GASTROENTERO`)
  3. Endocrinology (`ENDOCRINOLOG`)
  4. Pulmonology (`PULMONOLOGY`)
  5. Rheumatology (`RHEUMATOLOGY`)
  6. Nephrology (`NEPHROLOGY`)
  7. Laboratory Services (`LABORATORY_S`)
  8. Imaging & Radiology (`IMAGING_AND_`)
  9. Infusion Suite (`INFUSION_SUI`)
  10. Preventive & Wellness Care (`PREVENTIVE_A`)
- **Schedule Templates**: Monday through Friday (08:00 to 17:00 UTC), 30-minute default consultation slots.

---

## 4. Credentials & Endpoints Required From Hospital

Per Working Rule 5, no placeholder or fallback secrets are permitted in production. The following configuration values must be obtained from Adot Internal Medicine IT operations:

| Component | Setting Name | Description / Requirement | Status |
| :--- | :--- | :--- | :--- |
| **HMS API** | `HMS_BASE_URL` | Base URL of hospital custom HMS API (must be HTTPS) | Awaiting Hospital IT |
| **HMS Webhooks** | `HMS_WEBHOOK_SECRET` | HMAC-SHA256 signature secret ($\ge 32$ chars) | Awaiting Hospital IT |
| **Payment Gateway** | `PAYMENT_GATEWAY_API_KEY` | Telebirr / CBE Birr / local payment gateway API key | Awaiting Hospital IT |
| **Payment Webhook** | `PAYMENT_WEBHOOK_SECRET` | Secret for payment callback verification | Awaiting Hospital IT |
| **Email Gateway** | `SMTP_HOST` / `SMTP_USER` / `SMTP_PASSWORD` | Hospital on-premises SMTP relay server credentials | Awaiting Hospital IT |
| **SMS Gateway** | `SMS_GATEWAY_URL` / `SMS_GATEWAY_KEY` | Ethio Telecom SMS gateway credentials | Awaiting Hospital IT |
| **Branding Assets**| `BRANDING_LOGO_URL` | High-resolution SVG / PNG hospital logo | Awaiting Hospital IT |
| **Branding Assets**| `SUPPORT_EMAIL` | Official hospital patient support email address | Awaiting Hospital IT |

---

## 5. Verification & Test Suite Results

- **Total Test Suite**: 311 automated tests passed (0 failures, 0 skipped).
- **Multi-Tenancy & Isolation Tests**: 11 targeted cross-tenant security and isolation tests passed:
  - Cross-tenant read leak blocked by EF Core query filter.
  - Cross-tenant write leak blocked by `MedicalPortalDbContext.EnforceTenantIsolation` (`SecurityException`).
  - Cross-tenant entity mutation / re-assignment blocked (`SecurityException`).
  - Tenant resolution via header (`X-Tenant-Slug`), host header, and default fallback.
  - Server-side token-to-tenant binding mismatch rejected with 403 (`tenant_mismatch`).
  - Module gating at API level verified (`module_disabled`).
  - Platform Admin zero-PHI access verified.
  - Deterministic HKDF master key derivation verified (`fp_...`).
  - PostgreSQL Row-Level Security policy generation verified across all 48 tables.

---

## 6. Open Items Before Go-Live

1. **User Review of Dedicated Database Provisioning Plan**: Awaiting approval on [`docs/tenants/Adot-Hospital/provisioning-plan.md`](file:///home/michael/projects/app/medical_portal/docs/tenants/Adot-Hospital/provisioning-plan.md).
2. **Localization Phase 6**: Amharic (`am`) and Afaan Oromoo (`om`) translation packages.
3. **Ethiopian Calendar Phase 7**: Dual Gregorian/Ethiopian date input and display components.
4. **Clinical Core Phase 8**: OpenMRS 2.8+ PostgreSQL FHIR sync parity.
5. **National HMIS Integration Phase 9**: DHIS2 aggregate push adapter.
