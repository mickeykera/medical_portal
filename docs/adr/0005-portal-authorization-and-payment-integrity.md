# ADR 0005: Portal Authorization Hardening and Payment Integrity

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `MedicalPortal.Tests`)
- **Remediates:** SEC-05, SEC-06, SEC-07, SEC-08, SEC-09, SEC-10, SEC-15, SEC-16

---

## 1. Context & Vulnerability Drivers

The Codex security audit identified multiple critical and high-severity access control, payment integrity, and scope enforcement vulnerabilities across the portal API surface:
1. **SEC-05 (Payment Intent Replay & Misattribution):** Payment intents from Stripe/external gateways were not recorded upon confirmation. An attacker or client could replay a confirmed payment intent multiple times or attribute a payment made for Patient A against an invoice for Patient B.
2. **SEC-06 (Record Share Scope Bypass):** The `/api/share` redemption endpoint returned full patient clinical records regardless of the grantor's narrow scope restrictions (`ScopeJson`).
3. **SEC-07 (Caller-Optional Operational Department Scoping):** Schedulers, billing clerks, and nurses without assigned department memberships or facilities could execute actions across any department if they omitted the `x-department-id` header.
4. **SEC-08 (Unrestricted HMS Integration Endpoints):** Endpoints exposing demographic sync conflicts (`/api/integrations/hms/conflicts`) and dead-letter payloads (`/api/integrations/hms/dead-letters`) lacked authentication and administrative role guards.
5. **SEC-09 (BOLA/IDOR in Refill Approval/Denial):** Refill requests were resolved by `RefillId` without verifying that the associated prescription belonged to the caller's authorized `TargetPatientId`.
6. **SEC-10 (Predictable Proxy Invites & Unbound Acceptance):** Proxy invite codes were generated with only 32 bits of entropy (4 bytes hex), allowing brute-force enumeration. Invites were not bound to the recipient's authenticated email, permitting any registered patient to claim another user's invite.
7. **SEC-15 (Form Submission Without Write Grants):** `POST /api/forms/{id}/submit` was guarded only by read-only records permission (`Permission.viewRecords`), enabling read-only proxies to submit binding clinical intake questionnaires.
8. **SEC-16 (Unrestricted Direct Gateway Refunds):** `POST /api/billing/refunds` allowed patients and proxies to trigger direct gateway refunds against their own statements without clinical or billing staff review.

---

## 2. Decision & Remediation Architecture

### 2.1 SEC-05: Payment Intent Replay Prevention & Patient Binding
- **New Entity:** Created [`ConsumedPaymentIntent`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/BillingEntities.cs) with unique index on `GatewayPaymentIntentId` and composite index on `(PatientId, IdempotencyKey)`.
- **Payment Gateway Metadata:** Enhanced [`IPaymentGateway`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Payments/IPaymentGateway.cs) to return intent `PatientId` and `Currency`.
- **Pre-Payment Verification:** [`BillingController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/BillingController.cs) asserts that `intent.PatientId == TargetPatientId` before processing statement allocation, returning `400 Bad Request` on mismatch.
- **Consumption Recording:** Records confirmed intent in `ConsumedPaymentIntents` inside an atomic transaction. Replays are rejected with `409 Conflict`.

### 2.2 SEC-06: FHIR Bundle Record Share Scope Enforcement
- **Scope Parsing:** [`ShareRedemptionController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/ShareRedemptionController.cs) parses `candidate.ScopeJson`. If caller requests categories via request body, verifies `requestedCategories.IsSubsetOf(allowedCategories)`.
- **Resource Filtering:** Introduced `GetScopedPatientBundleAsync` in [`FhirService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Services/FhirService.cs) to filter exported FHIR bundles strictly by permitted resource types (`conditions`, `allergies`, `medications`, `observations`, `appointments`).

### 2.3 SEC-07: Mandatory Operational Staff Scoping
- **Membership Enforcement:** In [`AccessControlService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Services/AccessControlService.cs), operational staff (`SCHEDULER`, `BILLING`, `NURSE`, `LAB`, `PHARMACIST`, `FRONT_DESK`) without any `UserMembership` or `ProviderDepartment` are rejected with `unassigned_staff_scope` (`403 Forbidden`).
- **Scope Derivation & Validation:**
  - If staff has unambiguous membership (exactly 1 department), scope is automatically derived even if `x-department-id` header is omitted.
  - If staff has ambiguous memberships (multiple departments), omitting `x-department-id` returns `403 Forbidden` (`department_scope_required`).
  - Mismatched `x-department-id` outside the user's assigned departments returns `403 Forbidden` (`department_scope_mismatch`).

### 2.4 SEC-08: Guarded HMS Conflicts & Dead-Letter Queues
- **Authentication & Role Restrictions:** Decorated `/api/integrations/hms/conflicts` and `/api/integrations/hms/dead-letters` with `[RequireAuth]` and `[RequireAdmin]` in [`HmsWebhookController.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/HmsWebhookController.cs).
- **Mutating Actions Restricted to Admins:** Read operations are permitted for `ADMIN`, `SYSTEM_ADMIN`, and `COMPLIANCE_AUDITOR`. Mutating endpoints (`resolve` and `replay`) strictly enforce `ADMIN` or `SYSTEM_ADMIN`.

### 2.5 SEC-09: BOLA / IDOR Elimination in Prescription Refills
- **Prescription Ownership Check:** In [`AppointmentsAndRefillsControllers.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/AppointmentsAndRefillsControllers.cs), `ApproveRefill` and `DenyRefill` assert `r.Prescription.PatientId == TargetPatientId`. If a clinician attempts to resolve a refill belonging to another patient, a `404 Not Found` is returned.

### 2.6 SEC-10: Cryptographic 128-Bit Proxy Invites & Email Binding
- **128-Bit Entropy:** In [`SharingAndFormsAndFamilyControllers.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Web/Controllers/SharingAndFormsAndFamilyControllers.cs), invite tokens generate 16 cryptographically random bytes (`RandomNumberGenerator.GetBytes(16)`), formatted as 32 hex characters (`INV-<32 hex chars>`).
- **Recipient Identity Binding:** Acceptance verifies `actorPerson.Email.Equals(invite.Email, StringComparison.OrdinalIgnoreCase)`. Mismatched accounts receive `400 Bad Request`.
- **Self-Acceptance Guard:** Rejects attempts by the inviter to accept their own invite (`actorPerson.Id == invite.InviterPersonId`).

### 2.7 SEC-15: Dedicated Form Submission Permission
- **Permission Model:** Added `Permission.submitForms` to `Permission` enum and `CanSubmitForms` to `ProxyGrant` and `ProxyInvite`.
- **Matrix Configuration:** Granted `(UserRole.PATIENT, AppResource.ClinicalResults, AppAction.Write)` in `PermissionMatrix.cs`.
- **Endpoint Guard:** Changed `SubmitForm` in `SharingAndFormsAndFamilyControllers.cs` to require `[RequirePatientAccess(Permission.submitForms, AppResource.ClinicalResults, AppAction.Write)]`.

### 2.8 SEC-16: Segregation of Refund Authorization
- **Workflow Segregation:** `POST /api/billing/refunds` direct gateway refund processing is restricted strictly to authorized staff (`UserRole.BILLING`, `ADMIN`, `SYSTEM_ADMIN`).
- **Self-Refund Prevention:** Patients and proxies attempting self-refunds receive `403 Forbidden`. Billing staff must provide a non-empty audit `Reason`.

---

## 3. Database Migration

EF Core migration `20261024113152_FixAuthorizationAndPayments`:
- Creates `ConsumedPaymentIntents` table with unique index `IX_ConsumedPaymentIntents_GatewayPaymentIntentId` and composite index `IX_ConsumedPaymentIntents_PatientId_IdempotencyKey`.
- Adds `CanSubmitForms` column to `ProxyGrants` and `ProxyInvites`.

---

## 4. Verification & Testing

Comprehensive negative, abuse, and boundary test cases were implemented in [`AuthorizationAndPaymentsSecurityTests.cs`](file:///home/michael/projects/app/medical_portal/backend/tests/MedicalPortal.Tests/AuthorizationAndPaymentsSecurityTests.cs):
- `Payment_IntentReplay_RejectedWithConflict409`
- `Payment_CrossPatientMisattribution_RejectedWithBadRequest`
- `ShareRedemption_OverreachingScope_RejectedWithBadRequest`
- `ShareRedemption_NarrowScope_FiltersUnauthorizedResources`
- `AccessControl_UnassignedOperationalStaff_RejectedWithForbidden`
- `AccessControl_MultiDepartmentStaff_WithoutHeader_RejectedWithDepartmentScopeRequired`
- `HmsWebhook_NonAdminUser_ViewingConflicts_RejectedWithForbidden`
- `HmsWebhook_NonAdminUser_ResolvingConflict_RejectedWithForbidden`
- `Refills_Clinician_ApprovingCrossPatientRefill_ReturnsNotFound`
- `ProxyInvite_CodeEntropy_AtLeast128Bits`
- `ProxyInvite_Acceptance_ByDifferentEmail_RejectedWithBadRequest`
- `Forms_ProxyWithReadOnlyPermission_SubmittingForm_RejectedWithForbidden`
- `Billing_PatientAttemptingSelfRefund_RejectedWithForbidden`
- `Billing_StaffRefundWithoutReason_RejectedWithBadRequest`

**Test Suite Results:** All 300 tests passing with zero failures.
