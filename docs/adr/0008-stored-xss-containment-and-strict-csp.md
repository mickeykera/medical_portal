# ADR 0008: Stored DOM XSS Containment and Strict Content Security Policy (CSP)

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `frontend/`, `MedicalPortal.Tests`)
- **Implements:** Finding SEC-02 Remediation, Target Architecture Rule A, Working Rules 3 & 8

---

## 1. Context & Business Drivers

Security Audit Finding **SEC-02 (Critical - Stored DOM XSS across the portal)** identified multiple injection vectors:
1. **Unencoded Client-Side DOM Insertion**: The SPA repeatedly inserted user-controlled and API-returned strings directly into `.innerHTML` without contextual escaping (toasts, server error notices, appointment reasons, allergy substances/reactions, medications, message bodies, admin user emails/names, and facility details).
2. **Incomplete Server-Side HTML Encoding**: In [`ClinicalSummaryGenerator.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs), provider names (`patient.PrimaryProvider.FirstName/LastName`) and textual observation results (`v.ValueText`, `v.Unit`) were interpolated directly without `WebUtility.HtmlEncode`. Furthermore, the print button relied on an inline `onclick="window.print()"` handler.
3. **Permissive Content Security Policy (CSP)**: [`Program.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Program.cs) configured a CSP permitting `'unsafe-inline'` and loading scripts from external CDNs (`https://cdnjs.cloudflare.com`). This allowed injected inline event handlers (e.g. `<img onerror=...>`, `<svg onload=...>`) to execute in the victim's session.

Because the SPA executes same-origin, stored XSS allows attackers to read displayed Protected Health Information (PHI) and execute authenticated state-changing transactions, bypassing HttpOnly cookie boundaries.

---

## 2. Decision & Architecture

### 2.1 Complete Contextual Encoding in Server-Generated HTML

In [`backend/src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Domain/ClinicalSummaryGenerator.cs):
- Encoded all primary provider name fields:
  ```csharp
  $"Dr. {WebUtility.HtmlEncode(patient.PrimaryProvider.FirstName)} {WebUtility.HtmlEncode(patient.PrimaryProvider.LastName)}"
  ```
- Encoded all observation results, text values, units, and reference ranges:
  ```csharp
  (v.ValueNumber.HasValue ? $"{v.ValueNumber} {WebUtility.HtmlEncode(v.Unit ?? "")}".Trim() : WebUtility.HtmlEncode(v.ValueText ?? "—"))
  ```
- Removed inline `onclick="window.print()"` from the print button and replaced it with an external same-origin script [`frontend/js/print.js`](file:///home/michael/projects/app/medical_portal/frontend/js/print.js), complying with strict CSP.

### 2.2 Strict Content Security Policy (CSP)

In [`backend/src/MedicalPortal.Web/Program.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Program.cs) and [`nginx.conf`](file:///home/michael/projects/app/medical_portal/nginx.conf):
- Removed `'unsafe-inline'` and `'unsafe-eval'` from `script-src`.
- Removed external CDN origins (`cdnjs.cloudflare.com`, `fonts.googleapis.com`, `fonts.gstatic.com`).
- Enforced strict directives:
  ```text
  default-src 'self';
  script-src 'self';
  style-src 'self' 'unsafe-inline';
  font-src 'self' data:;
  img-src 'self' data:;
  connect-src 'self' ws: wss:;
  object-src 'none';
  base-uri 'self';
  frame-ancestors 'none';
  ```
- Guaranteed defense-in-depth: Even if raw HTML was injected into the DOM, the browser strictly blocks the execution of inline scripts and event handlers.

### 2.3 Client-Side Contextual Escaping and DOM Node Assignment

In [`frontend/js/app.js`](file:///home/michael/projects/app/medical_portal/frontend/js/app.js):
- Defined standard contextual HTML encoder:
  ```javascript
  function escapeHtml(str) {
    if (str === null || str === undefined) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
  ```
- Refactored `showToast`: Switched from `toast.innerHTML = ...` to `textContent` assignment on a dedicated DOM element.
- Refactored `showInAppAlert` and `showInAppConfirm`: Created dedicated `h3` and `p` DOM elements assigning `textContent = title; textContent = message;`.
- Applied `escapeHtml` across all interpolated dynamic values:
  - Vitals and Lab results (`v.label`, `v.value`, `v.unit`, `v.referenceRange`).
  - Active Medications (`m.name`, `m.strength`, `m.dosage`, `m.frequency`, `m.instructions`).
  - Appointments (`a.provider.name`, `a.provider.credentials`, `a.reasonForVisit`, `a.id`).
  - Allergies (`a.substance`, `a.drugClass`, `a.severity`, `a.reaction`, `a.drugClassCode`).
  - Messages (`thread.subject`, `m.senderName`, `m.senderRole`, `m.body`).
  - Admin Workforce & Users (`u.person.fullName`, `u.email`, `u.person.mrn`, `m.facilityCode`, `m.departmentCode`, `m.role`).
  - Audit Trail (`l.resourceType`, `l.actor.name`, `l.ipAddress`, `l.hash`).
  - Facilities and Departments (`f.name`, `f.code`, `d.name`, `d.code`, `d.specialty`).
  - Record Shares and Family Proxies (`s.label`, `s.recipientName`, `g.grantor.name`, `g.grantee.name`).

---

## 3. Verification & Negative Tests

We added [`XssContainmentAndCspTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/XssContainmentAndCspTests.cs) containing:
1. `CspHeader_EnforcesStrictScriptPolicy_WithoutUnsafeInlineOrCdns`: Verifies that `script-src 'self'` is enforced without `'unsafe-inline'` and without external CDN hosts.
2. `DefensiveHeaders_EnforceNosniffAndFrameDeny`: Verifies `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, and `Permissions-Policy`.
3. `ClinicalSummaryGenerator_EncodesUntrustedProviderAndObservationData`: Supplies malicious script, image, and svg payloads across 6 entities; asserts all raw payloads are blocked and properly HTML encoded.
4. `FrontendAppJs_DefinesAndUsesEscapeHtml_ForDynamicRendering`: Asserts static presence of `escapeHtml` and `textContent` assignments in toasts and modals.
5. `Frontend_PrintScript_ExistsAndHasZeroBrowserDialogs`: Verifies offline print script without browser dialogs.
