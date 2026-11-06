# HMS + EMR Transformation Specification

Audience: GitHub Copilot (agent mode). Save this file in the repo as `docs/hms-transformation-spec.md` and work from it phase by phase. It is deliberately long; do not summarize it away. Requirement IDs (CL-1, BL-3, …) are for traceability: every ID must map to a test and a line in `docs/progress.md`.

---

## 0. Operating rules (always apply)

1. **Inspect before changing.** Read the repo, `docs/adr/*`, `docs/progress.md`, `docs/open-questions.md`, `docs/security-audit-2026-10-24.md` and the existing tests first. Everything in section 1 is a snapshot from memory of earlier work: verify it, and tell me where reality differs.
2. **Work in phases (section 8) and stop at every STOP gate.** Do not begin the next phase until I reply "go". At each stop report: files changed, tests run with raw pass/fail counts, requirement IDs closed, what is left, risks, and one clear question.
3. **Small, reviewable commits.** One branch per phase. Build and the full test suite must be green before you report.
4. **Never weaken a security control** (tenant isolation, audit hash chain, step-up auth, MFA, CSRF, CSP, rate limits, production startup guards, secret handling). Every new endpoint needs authentication, tenant scoping, role/permission checks, department scope where relevant, and an audit entry. Reads of PHI are audited.
5. **No invented specifications.** Where the Ethiopian Ministry of Health, DHIS2, EFDA, EPSA, eCHIS, tariffs, formularies, or clinical protocols are not in the repo, build behind interfaces with configurable data and fakes, and record the unknown in `docs/open-questions.md`. Never ship demo clinical rules as if validated; mark seed data "DEMO".
6. **No real patient data.** Synthetic seed data only, Ethiopian names, Addis Ababa locations, +251 phones, ETB amounts.
7. **Hospital-specific behavior is tenant configuration, not code forks.** Every new module sits behind a tenant module flag enforced at the API (not only hidden in the UI).
8. **Every user-facing string is translatable (en, am, om).** No browser dialogs (`alert`, `confirm`, `prompt`) anywhere; use accessible in-app modals, typed confirmation for destructive actions, and toasts. No runtime CDN dependencies (the system must work on a LAN with no internet). All dynamic content rendered as text (XSS safe). WCAG 2.1 AA.
9. **Dates and money:** store UTC `timestamptz` and Gregorian `date`; show Gregorian and Ethiopian calendars (user preference, both on clinical safety fields); time zone `Africa/Addis_Ababa`; money in integer santim, displayed as ETB.
10. **When ambiguous or destructive, stop and ask me.** Destructive means dropping tables, deleting data, or removing features not listed here.

---

## 1. Context snapshot (verify against the repo)

- Stack: .NET 8 / C# 12, EF Core 8, PostgreSQL 16/17 (SQLite dev fallback is being removed for production), ASP.NET Core SignalR, BCrypt, JWT cookie sessions with server-side session validation, Docker Compose + nginx (same-origin proxy), xUnit (400+ tests).
- Repo layout: `backend/` (MedicalPortal.Core, .Infrastructure, .Web, tests) and `frontend/` (currently a vanilla JS SPA with tabs: Overview, Doctor Cockpit, Trends, Forms, Messages, Billing, Family, Sharing, Find Care, Facilities, Settings, plus an admin dashboard module `admin-dashboard.js`).
- Existing security: MFA (TOTP) for staff, step-up re-authentication for sensitive admin actions, tenant model, department scope, append-only audit log with hash chain and verification, break-glass access, CSRF header, CSP, production fail-closed startup guards, share/invite hardening.
- Existing roles (verify): PATIENT (and proxy contexts), CLINICIAN, NURSE, SCHEDULER, BILLING, LAB, PHARMACIST, COMPLIANCE_AUDITOR, DEPT_ADMIN, ADMIN (hospital admin / IT), PLATFORM_ADMIN (zero PHI), INTEGRATION_ADMIN.
- Existing Ethiopia work: ETB formatting, Addis Ababa defaults, Ethiopian calendar conversion + `calendar_dim`, Amharic/Afaan Oromoo locale files, dev seeded accounts (`scripts/dev-logins.sh`, `scripts/dev-totp.sh`, dev-only guards).
- Clinical entities in the portal DB (Observation, Condition, Allergy, Prescription …) are marked obsolete "mastered in OpenMRS". **Read ADR 0001 and the spike document.** Section 2.4 explains how to reconcile that with this spec.

---

## 2. Product pivot and principles

### 2.1 The pivot

The product is no longer a patient-facing "medical portal". It is a **hospital management system (HMS) integrated with an electronic medical record (EMR)** used by hospital staff across departments. The patient portal remains only as one role-specific dashboard (PATIENT/PROXY) for appointments, records, billing statements, forms, messaging, family access and sharing.

### 2.2 Global cleanups (apply first)

- **CL-1** Remove **Find Care** and **Nearby Facilities** from the entire product: frontend tabs and scripts, API routes (`/api/care/*`, `/api/facilities/nearby`, `/api/care/places`, `/api/geo/*` if used only by them), services (GeoLogic, PublicIpService if only used for this, outbound ip-api.com call), DTOs, tests, i18n keys, docs, seed data, nav items, deep links, feature flags. Patients included. Keep nothing dead.
- **CL-2** Remove the **".NET ONLINE" status badge** from every dashboard. System health belongs only on the IT/Admin "System status" page.
- **CL-3** Remove the **MFA adoption** card/metric from every dashboard except the IT/Admin security page.
- **CL-4** Navigation is **role-driven and server-authoritative**: each role gets only the screens in section 6; hidden UI must also be denied by the API. Add a single `NavigationPolicy` source of truth (backend returns allowed modules for the session; frontend renders it).
- **CL-5** Labels must match context: no "Patient settings" for staff, no "Patient identity" in department settings, no "Infrastructure" section outside IT.
- **CL-6** Forms are available **only** on the PATIENT dashboard. Remove Forms from all staff roles.
- **CL-7** Replace every patient `<select>` dropdown for staff with the shared **PatientPicker** component (a searchable list showing full name + MRN/ID + class tag + location), see PH-1.
- **CL-8** Remove all browser dialogs; add a lint/test that fails the build if any appear.
- **CL-9** Delete dead code, unused i18n keys and obsolete tests created by the removals; do not leave placeholders.

### 2.3 Design principles

- **Modular monolith with bounded contexts** (one deployable, strict module boundaries): Registration/ADT, Orders & Results, Billing/Cashier, Pharmacy & Stock, Emergency, Inpatient/Nursing, ICU, Operating Theatre, Delivery Suite, NICU, Laboratory, Pathology, Imaging & Physiology, Mortuary, Support Services, Reporting/KPIs, Admin/IT, Compliance. Modules communicate via application services and **domain events through a transactional outbox** (never direct table access across modules).
- **FHIR R4 JSON** is the interoperability representation (section 5.3). Internal storage may differ.
- **Offline-first:** every module must work on the hospital LAN with no internet; anything needing the cloud (sync, DHIS2) uses store-and-forward. A network loss must never block care.
- **Safety first:** workflow guards are explicit (billing gate exceptions for emergencies, radiation safety, controlled drugs, wrong-patient prevention).
- **Evidence base** (from sources reviewed; verify against current official documents and record in `docs/open-questions.md`): Ethiopian hospital reform guidelines (EHRIG) and the Hospital Services Transformation Guideline define hospital KPIs and flows (for example emergency triage within 5 minutes of arrival, emergency stay under 24 hours, emergency mortality, bed occupancy, mean length of stay, inpatient mortality, pressure ulcers, surgical site infections, outpatient waiting time); emergency patients must reach triage without financial hindrance; outpatient flow includes central triage, medical record room, cashier, laboratory with cashier, and pharmacy dispensing; Ethiopian pharmacies use Auditable Pharmaceutical Transactions and Services (APTS/eAPTS: ABC/VEN analysis, stock status analysis, stock transfer, physical inventory, expiry tracking) with EPSA/eLMIS stock reporting.

### 2.4 Reconciling with the clinical-core decision (ADR 0001)

Do not rebuild what the chosen clinical core already provides. Phase 0 must produce `docs/adr/0002-hms-module-ownership.md` with a table: **module → owner (native .NET module | OpenMRS/Bahmni via FHIR | hybrid) → reason**. Default if ADR 0001 stands: patient identity, encounters, orders and clinical observations are mastered in the clinical core and reached through the adapter; the **operational HMS modules** (beds/ADT workflow board, billing gate, stock/warehouse, assets, ICU/OR/NICU/delivery operational logs, support services, mortuary, KPIs) are native modules in this codebase. If you find that the spike chose differently, follow the ADR and tell me what changes. Either way, the interfaces and UI in this spec stay the same.

---

## 3. Workflow map (the hospital architecture diagram)

The reference image is a **functional relationship diagram** of the hospital. It is the authoritative description of how departments connect, which flows are critical, and which relationships are undesirable. Implement it as software, not as decoration.

### 3.1 Modules (nodes)

Node
Type
Notes from the diagram

Main entrance hall (registration, queue, wayfinding)
communal
"community" links to OPD, ED, diagnostics, business

Emergency (with triage by nurses; ambulance admission)
communal
needs future expansion; discreet ambulance entrance

Outpatient polyclinics
communal
central triage, cashier, record room, pharmacy dispensing

Inpatient wards / nursing units
private

ICUs
private

NICU (added by owner)
private
fed by delivery suite, ICU, OR

Operating theatre
private

Delivery suite
private

Pharmacy (+ warehouse / stock)
private

Diagnostic unit: Laboratory, Pathology, Radiology (X-ray, ultrasound, CT, MRI), Physiology (ECG, EEG, EMG)
mixed
lab, radiology and pathology marked "needs future expansion"

Mortuary
private
privacy-sensitive

Administration: business/finance offices
private

Support services: kitchen and dining (cafeteria), housekeeping, laundry, central linen room, storages/stores
private (kitchen communal)

"Needs future expansion" on Emergency, Radiology, Laboratory and Pathology means: design these as **configuration-driven registries** (modalities, analyzers, test catalogs, bays) so new equipment or services are added in admin screens, not code.

### 3.2 Relationships (edges) and what they mean in software

Rating legend from the diagram: **absolutely important** (black), **especially important** (dark blue), **important** (light blue), **ordinary closeness** (dotted), **undesirable** (red arrow).

From → To
Diagram label
Rating
Software meaning

Emergency ↔ ICUs
critical patient flow
absolutely important
One-action transfer (ADT) with bed reservation, handover (SBAR) note, atomic and offline-safe, priority escalation, never blocked by billing

Operating theatre ↔ ICUs
shared equipment and critical patient flow
absolutely important
Post-op transfer to ICU, shared asset pool and check-out, theatre-to-ICU handover

Inpatient ↔ Operating theatre
critical patient flow
absolutely important
Pre-op list, theatre booking, post-op return, bed hold

Emergency ↔ Pharmacy
medicine flow
especially important
ED sub-store (emergency drug stock), fast dispensing, crash-cart stock

Pharmacy ↔ Inpatient wards
medicine flow
especially important
Ward orders, dispensing, ward sub-stores, returns

Emergency ↔ Radiology
patient flow (prompt need)
especially important
STAT imaging orders, transport tasks, prioritized worklist

Emergency ↔ Inpatient
doctor flow (frequent consultations)
important
Inpatient consult requests by specialty with response time tracking

Diagnostics ↔ ED / OPD / IPD / ICU / OR
distribution of specimen, patient flow, reports
especially important
Specimen collection, labelling, transport, accession, chain of custody, result return to ordering physician

OR ↔ Pathology / Lab
distribution of specimen
important
Intra-op specimens, frozen section requests

Delivery suite ↔ OR
shared staff and facility
important
Emergency caesarean booking, shared anaesthesia/midwife roster pool

Delivery suite ↔ ICU / wards
shared staff and equipment
important
Maternal transfer; newborn to NICU

Delivery suite ↔ Radiology
**safety**
**undesirable**
**Radiation safety guard** (see 3.3)

Mortuary ↔ Inpatient / Outpatient / Emergency
**privacy**
**undesirable**
**Privacy guard**: deceased patients are handled through a restricted mortuary workflow only; no mortuary visibility on public or general ward screens

Mortuary ↔ Pharmacy
shared staff and facility
important
Shared staffing pool only; no data coupling

Support services ↔ Inpatient
distribution of food, laundry etc.
especially important
Diet orders, linen requisitions, housekeeping tasks triggered by patient events

Main entrance ↔ OPD / ED / diagnostics / business
community
especially important
Registration, queue tickets, routing, wayfinding

Offices ↔ Main entrance
**privacy**
**undesirable**
Administrative and financial data never exposed to public/clinical screens

Mortuary / OR / Pharmacy ↔ others
dotted
ordinary
Reporting links only, no workflow coupling

### 3.3 Guards derived from the undesirable edges

- **GUARD-1 Radiation safety (Delivery suite ↔ Radiology):** every X-ray/CT/fluoroscopy order checks pregnancy status (flag from ANC/delivery records or a required question). If pregnant or unknown for a person of childbearing age: require clinical justification, radiologist approval, shielding acknowledgement, and suggest ultrasound/MRI alternatives. Audit every override. Pregnant patients in the delivery suite cannot be routed into the radiation worklist automatically.
- **GUARD-2 Mortuary privacy:** deceased handling is a separate permissioned workflow (death declaration → mortuary intake → storage → release). Only MORTUARY roles and authorized clinicians/registrars see it. A deceased patient's name must not appear on general worklists, queue screens or public displays after death declaration.
- **GUARD-3 Administration segregation:** finance/business roles never see clinical data; clinical roles never see payroll/administrative data.
- **GUARD-4 Billing gate exemptions:** emergency and life-saving care is never blocked by payment (section 4.3).

### 3.4 Module map (include this in `docs/architecture/hms-map.md`)

```
flowchart TB
  ENT["Main entrance: registration, queue, wayfinding"]
  OPD["Outpatient polyclinics"]
  ED["Emergency + triage"]
  IPD["Inpatient wards"]
  ICU["ICUs"]
  NICU["NICU"]
  OR["Operating theatre"]
  DS["Delivery suite"]
  PH["Pharmacy + warehouse"]
  MORT["Mortuary"]
  BILL["Billing / cashier"]
  ADM["Administration offices"]
  subgraph DIAG["Diagnostic unit"]
    LAB["Laboratory"]
    PATH["Pathology"]
    RAD["Radiology: X-ray, US, CT, MRI"]
    PHYS["Physiology: ECG, EEG, EMG"]
  end
  subgraph SUP["Support services"]
    KIT["Kitchen and dining"]
    HK["Housekeeping"]
    LAUN["Laundry"]
    LIN["Central linen room"]
    STO["Storages"]
  end
  ENT ==>|community| OPD
  ENT ==>|community| ED
  ED ==>|critical patient flow| ICU
  OR ==>|critical patient flow, shared equipment| ICU
  IPD ==>|critical patient flow| OR
  ED -->|medicine flow| PH
  PH -->|medicine flow| IPD
  ED -->|prompt radiology| RAD
  ED -->|doctor flow: consults| IPD
  OPD -->|orders, specimens| DIAG
  IPD -->|orders, specimens| DIAG
  ICU -->|orders, specimens| DIAG
  OR -->|specimens| PATH
  DS -->|shared staff| OR
  DS -->|maternal transfer| ICU
  DS -->|newborn transfer| NICU
  ICU -->|neonate transfer| NICU
  OR -->|neonate transfer| NICU
  DS --x|safety guard| RAD
  MORT --x|privacy guard| IPD
  MORT --x|privacy guard| ED
  MORT --x|privacy guard| OPD
  ADM --x|privacy guard| ENT
  SUP -->|food, linen, cleaning| IPD
  OPD -->|charges| BILL
  ED -->|charges, bill later| BILL
  BILL -->|payment confirmation releases orders| DIAG
  BILL -->|payment confirmation releases dispensing| PH
```

---

## 4. Cross-cutting foundations (build before the role dashboards)

### 4.1 Patient class and ADT

- **FND-1** Patient **class** per encounter: `OUTPATIENT`, `INPATIENT`, `EMERGENCY` (plus `DAY_CASE`, `OBSERVATION`). Map to FHIR `Encounter.class` (AMB, IMP, EMER, SS/OBSENC). Class is set at registration/triage and changes only through ADT events (admit, transfer, discharge, convert ED → inpatient). Every list, banner and report shows class. Nurses and physicians can classify and re-classify (with reason, audited, permission-controlled).
- **FND-2** **Location hierarchy:** Facility → Department → Unit (ward/ICU/NICU/OR/delivery/ED/OPD clinic) → Room → Bed/Bay. Bed states: available, occupied, reserved, cleaning, blocked, out of service. Bed state changes publish events (housekeeping reacts).
- **FND-3** **ADT service:** admit, transfer (with reason, from/to location, handover note, timestamps), discharge (disposition: home, referral, LAMA, death, transfer-out), and a live **bed board** per unit. Transfers between ED, ICU, OR, wards, delivery suite and NICU are single transactions that move responsibility, orders, and active tasks.
- **FND-4** Patient **banner** shown on every clinical screen: name, MRN, age/sex, class, location/bed, allergies, critical alerts, isolation status, pregnancy/neonate flags.
- **FND-5** Registration at the main entrance: search/create patient (MRN, local identifiers, duplicate detection), visit creation, queue ticket, routing to central triage (OPD) or triage (ED). Queue/waiting displays must never reveal diagnoses.

### 4.2 Orders and results framework

- **FND-6** A generic **Order** aggregate: type (lab, imaging, physiology, pathology, medication, procedure, nursing task, diet, consult, linen/housekeeping request), ordering practitioner, performing department, priority (routine, urgent, STAT), clinical indication, status machine (`draft → pending_payment → released → in_progress → resulted → verified → delivered → closed`, plus `cancelled`, `rejected`), timestamps for each step, and links to encounter and patient. Map to FHIR `ServiceRequest`/`MedicationRequest`/`Task`.
- **FND-7** **Result return path:** every result/report is routed to the **ordering physician** (and optionally the responsible team) with an in-app notification, unread tracking, acknowledgement, and critical-value escalation (call-back record). Abnormal flags use reference ranges.
- **FND-8** Worklists per performing department, filtered by status, priority and payment gate. Each receiving department sees the **patient profile needed to perform the order** (identity, age/sex, allergies, relevant clinical summary, indication, pregnancy flag) under minimum-necessary rules, with every view audited.

### 4.3 Billing gate (pay-before-service)

- **FND-9** Catalog of billable services and **tariffs per tenant in ETB** (admin-managed, effective dates). Orders create **charge items**.
- **FND-10** Gate behavior: a chargeable order enters `pending_payment`; when the cashier confirms payment the order auto-releases to the department worklist and notifies it. The department UI shows "Paid / Awaiting payment / Waived / Bill-later" and cannot start the order without a released status.
- **FND-11** **Exceptions (configurable, audited):** (a) **Emergency and life-saving care is never blocked**: ED orders are `bill_later` automatically, creating a receivable; (b) waivers/exemptions (indigent, staff, maternal/child programs, referral) with reason and approver; (c) credit/insurance/employer letters and community insurance (CBHI/SHI as configurable payers, no hardcoded schemes); (d) supervisor override with typed reason and step-up; (e) refund/cancel before service reverses the charge.
- **FND-12** Unpaid-but-performed orders (after emergency bill-later) appear on the cashier's receivables queue and the patient's running bill.

### 4.4 Inventory and stock framework (reused by pharmacy, stores, OR, ICU, linen, kitchen, housekeeping)

- **FND-13** Item master (generic name, ATC/EML code where relevant, strength, form, unit, pack, category, VEN class), **stores** (central warehouse and department sub-stores), **batches/lots** with expiry and manufacturer, and an **append-only stock ledger** (receive, issue, transfer, return, adjustment, wastage, expiry write-off, dispense, consume). On-hand is derived from the ledger; never edit balances directly.
- **FND-14** **FEFO** issuing (first-expiry-first-out), near-expiry alerts, quarantine for recalled/expired batches, min/max/reorder levels, requisition → approval → issue → receive workflow between stores, goods receipt notes with supplier/EPSA reference, physical inventory (count sheets, variance posting with approval).
- **FND-15** **APTS-style analytics** (configurable): ABC analysis, VEN analysis, ABC/VEN reconciliation, stock status analysis, stock-out days, wastage and expiry rates, consumption trends. Provide adapters/interfaces for exporting to national logistics systems (eLMIS/eAPTS) behind a flag with a fake; spec unknowns go to `docs/open-questions.md`.
- **FND-16** Every consumption can be **attributed** to patient/encounter, procedure, machine, department and cost center.
- **FND-17** **Controlled/narcotic drugs register:** dual-signature issue and administration, balance reconciliation per shift, discrepancy escalation.

### 4.5 Equipment and asset registry (shared equipment)

- **FND-18** Asset master (type, model, serial, department, location, status, owner unit, maintenance schedule, calibration due) including ventilators, monitors, infusion pumps, incubators, defibrillators, anaesthesia machines, imaging modalities, analyzers.
- **FND-19** **Usage sessions:** start/stop (by nurse/technician), linked to patient and encounter, with measured resources (hours, consumables used, gas/oxygen flow or volume where recorded, disposables, staff time) and computed **cost per use** from item costs and tariffs. Check-out/check-in and transfer of **shared equipment** between ED, ICU, OR and delivery suite with location history. Maintenance tickets and downtime.

### 4.6 Tasks, notifications, rosters

- **FND-20** Task engine (nursing tasks, transport tasks, cleaning tasks, linen requests, stock requisitions) with assignee/unit, due time, escalation, and audit.
- **FND-21** Real-time notifications scoped to tenant, unit and role (SignalR groups; no broadcast to all clients).
- **FND-22** Staff roster and **shared-staff pools** (for example anaesthesia and midwife pools shared by OR and delivery suite), shift handover notes.

### 4.7 Security, privacy, audit

- **FND-23** Replace the coarse role list with a **permission catalog**: roles = permission sets, plus unit/department assignments and capability flags (for example `can_prescribe_controlled`, `can_triage`, `can_release_orders`). Seed role presets, keep server-side enforcement.
- **FND-24** All new reads of PHI audited; break-glass supported for emergency access; department admins scoped to their department only; platform admin sees no PHI.
- **FND-25** Per-module authorization matrix test (every role × every endpoint).

### 4.8 Reporting and KPIs

- **FND-26** KPI engine producing de-identified aggregates (section 6.4 lists indicators). Computed per department and hospital, with date range in both calendars, and a DHIS2 aggregate mapping hook (exporter exists; add indicator definitions as configuration).

---

## 5. Data and standards

### 5.1 Entity sketch (names are suggestions; follow existing conventions)

`Encounter(class, status, patientId, location history)`, `Location(tree)`, `Bed`, `AdtEvent`, `Order`, `OrderStatusHistory`, `Specimen`, `LabAnalyzer`, `LabTestCatalog`, `LabResult`, `ImagingModality`, `ImagingStudy`, `ImagingReport`, `RadiationDoseRecord`, `PathologyCase`, `PathologySlide`, `PathologyReport`, `ChargeItem`, `Tariff`, `Payment`, `Receipt`, `CashShift`, `Waiver`, `Item`, `Store`, `Batch`, `StockLedgerEntry`, `Requisition`, `Dispense`, `MedicationAdministration`, `Asset`, `AssetUsageSession`, `NursingVisit`, `NursingIntervention`, `TriageRecord`, `IcuMonitoringEntry`, `TheatreCase`, `SurgicalChecklist`, `AnaesthesiaRecord`, `LabourRecord`, `Partograph`, `DeliveryRecord`, `NeonatalRecord`, `MortuaryCase`, `DeathRecord`, `DietOrder`, `HousekeepingTask`, `LinenLedger`, `Task`, `Roster`, `KpiSnapshot`. All tenant-owned tables carry `TenantId` and follow the existing tenant-isolation pattern (query filters + row-level security if implemented).

### 5.2 Terminologies

ICD-10 (diagnoses), LOINC (labs/observations), DICOM (imaging), national Essential Medicines List with generic names and ATC codes for the formulary (verify the current Ethiopian EML and EFDA requirements), UCUM units. Keep code systems as configurable tables.

### 5.3 FHIR R4 mapping (expose read endpoints and internal events using these)

Patient, Encounter, Location, Practitioner/PractitionerRole, ServiceRequest, Task, Specimen, Observation, DiagnosticReport, ImagingStudy, MedicationRequest, MedicationDispense, MedicationAdministration, Procedure, Condition, AllergyIntolerance, ChargeItem, Invoice, PaymentNotice, Account, Coverage, Device, DeviceUseStatement, SupplyRequest, SupplyDelivery, NutritionOrder, Schedule/Slot/Appointment, DocumentReference (death certificate, reports). Birth, APGAR and partograph measurements as Observations. Imaging via DICOMweb (STOW/QIDO/WADO) against a PACS (see 6.12).

---

## 6. Role dashboards and modules (the owner's change list, per role)

For every role: left navigation shows only what is listed; **everything else is removed from the UI and denied by the API**. Each screen must serve a real task; no decorative stat cards or placeholder data.

### 6.1 Billing / Cashier (role BILLING; add BILLING_MANAGER capability)

- **BL-1** Remove: Trends, Forms, Messages, Sharing, Find Care, Nearby Facilities, Family. Keep only billing-department functions.
- **BL-2** **Cashier worklist:** pending charges by patient (from lab, imaging, pathology, procedures, bed-days, pharmacy dispensing, OR); search by name/MRN/queue number; take payment (cash, mobile money or bank transfer reference, deposit, partial payment); issue sequential receipts (ETB, Ethiopian + Gregorian date, tenant letterhead, printable PDF, reprint audited).
- **BL-3** **Order release feed:** shows which orders were released after payment and which are waiting; one click to waive with reason (permission + step-up).
- **BL-4** **Inpatient running bill:** charges accumulating per admission, deposits, discharge bill clearance.
- **BL-5** **Receivables:** bill-later (emergency) charges, credit/insurance/employer letters, claim status, aging buckets.
- **BL-6** **Refunds and cancellations:** request/approve flow with reason codes and step-up for approval.
- **BL-7** **Shift management:** open/close cash shift, expected vs counted cash, variance report, daily reconciliation per cashier.
- **BL-8** **Tariff management** (billing manager): price list per service, effective dates, ETB.
- **BL-9** **Revenue reports:** by department and service, daily/monthly, outstanding balances, waiver and refund totals; export (step-up).
- **BL-10** Existing payment plan feature stays, using the shared primary button token.
- **BL-11** Billing staff see identity and charge data only, never clinical content.

### 6.2 Physician / clinician (role CLINICIAN)

- **PH-1** **On login the physician lands on "My Patients"**: a **list** (not a dropdown) of their patients showing **full name and MRN/ID**, age/sex, class tag (IPD/OPD/ED), location/bed, allergy and critical-result flags, last seen, pending results. Sources: care team assignment, today's appointments, inpatients under their care. Search, filter by class/ward, sort. Selecting a row opens the chart with the patient banner. Provide the shared `PatientPicker` component and use it everywhere a physician selects a patient.
- **PH-2** Chart tabs: Summary, Vitals and observations, Medications, Orders and results (lab, imaging, pathology, physiology), Notes, Problems/diagnoses, Allergies, Procedures, Timeline. Ordering lab/imaging/pathology/physiology/medication from the chart, with the billing gate status visible.
- **PH-3** **Fix Export to PDF:** reproduce and diagnose the current failure (capture the error), then implement server-side PDF generation that works offline, embeds fonts for Amharic (Ethiopic script) and Latin, shows Gregorian and Ethiopian dates, escapes all content, and is audited as an export. Evaluate PDF libraries for license and offline suitability and record the choice in an ADR. Add tests.
- **PH-4** **Fix "Request visit":** it currently navigates to Find Nearby Care. Remove that behavior. Replace with **"Schedule follow-up"** (book a follow-up for the selected patient with themselves or a department, in an in-app modal, using department schedules). Remove any "request visit" wording that belongs to patients.
- **PH-5** **Messages:** thread list shows **patient name and MRN** with unread counts; the "View" selector lists the physician's patients by name; inter-staff consult messages tied to a patient or an order are supported. (Patients can message only via the patient dashboard.)
- **PH-6** **Prescribing** against stock (see 6.9): shows availability at the physician's linked pharmacy/store, formulary restrictions, controlled-drug and antimicrobial-restriction rules, interaction/allergy checks (demo knowledge base is production-blocked), and override justification.
- **PH-7** Inpatient tools: admit/transfer/discharge, classification, consult requests, OR booking requests, ICU/NICU transfer requests.
- **PH-8** **Settings:** title and content are staff-appropriate (profile, language, calendar display, notification preferences, MFA/devices). Remove the label "Patient settings". Remove the "Infrastructure" section entirely.
- **PH-9** Remove from the physician navigation: Trends (unless it is clinical trending for the selected patient), Forms, Sharing, Family, Find Care, Nearby Facilities, Billing.

### 6.3 Compliance auditor (role COMPLIANCE_AUDITOR)

Research basis: hospital HIPAA-style audit programs expect review of authentication events, account and privilege changes, access to ePHI (view, create, modify, delete, print, export), patient searches, break-glass activations with justification and retrospective review, administrative/configuration changes and API activity; weekly review of break-glass, broad searches and exports; quarterly access recertification; tamper-evident, retained, time-synchronized logs; segregation of duties. (The owner shared a link with a list of what auditors need; it could not be opened. Treat this section as the starting point and ask the owner to compare.)

- **AU-1** Remove the "viewing patient" dropdown and any patient selector. Remove Forms, Messages, Billing, Family, Sharing, Find Care, Nearby Facilities.
- **AU-2** **Audit explorer:** filter by actor, role, department, action type, resource type, date range (both calendars), outcome (success/denied), source IP/device; patient-specific lookup only via an explicit, itself-audited "investigation case" (case ID, reason), never a browsing dropdown.
- **AU-3** **Break-glass review queue:** every emergency access with justification, session actions, reviewer assignment, outcome, and due date (target within 24–72 hours, configurable); overdue highlighting.
- **AU-4** **Anomaly and snooping detection views:** repeated or bulk access, after-hours spikes, access to VIP/employee/family records (flagged patients), denied-then-granted sequences, peer-group outliers by role/department, large exports/prints. Rules are configurable and produce review tasks.
- **AU-5** **Access recertification:** quarterly user-access review (user, role, department, last login, dormant accounts) with attest/revoke request workflow (revocation executed by IT, with audit).
- **AU-6** **Privilege and configuration change log:** role grants, MFA resets, security-policy changes, module-flag changes, tariff and waiver changes.
- **AU-7** **Billing-integrity signals without billing access:** counts and rates of waivers, overrides, bill-later, refunds and reversals by user and department (no patient financial detail).
- **AU-8** **Integrity and retention:** verify the audit hash chain, show retention status, time-sync status, export (step-up) with export itself audited. Logs treated as sensitive data.
- **AU-9** **Incident and breach register** (case management: detected, assessed, notified, closed) and policy-attestation tracking (optional, configurable).
- **AU-10** **Trends = overall hospital performance, not one patient's lab test.** De-identified aggregates only: the EHRIG-style KPIs in 6.4, plus compliance KPIs (break-glass volume and review timeliness, access recertification completion, unauthorized-access findings).

### 6.4 Hospital performance KPIs (shown to auditors, hospital admin, relevant dept admins)

Implement as configurable KPI definitions; compute from operational data: outpatient attendance and waiting time to treatment; outpatients not seen same day; ED attendances; **ED patients triaged within 5 minutes of arrival**; ED stays over 24 hours; ED mortality; inpatient admissions, **bed occupancy rate**, **average length of stay**, bed turnover, inpatient mortality; pressure-ulcer incidence; surgical-site-infection percentage; surgical volume and delay for elective admission; maternity indicators (deliveries, caesarean rate, stillbirths, maternal deaths, newborn admissions to NICU); laboratory turnaround time and test volumes; imaging turnaround and volumes; pharmacy availability of tracer medicines, stock-outs, expiry/wastage; referral rates; revenue and waiver rates. Show trend lines with Gregorian/Ethiopian period labels. Verify definitions against the current official Ministry documents and record gaps.

### 6.5 Department admin (role DEPT_ADMIN)

- **DA-1** Remove: Overview (patient-portal overview), Trends (patient-style), Forms, Messages, Billing, Family, Find Care, Nearby Facilities, Sharing. Remove the **Organization page entirely**.
- **DA-2** **Everything is scoped to the admin's own department(s), enforced on the server.** Example: a Radiology department admin sees only radiology users, radiology audit events (events by radiology staff or in radiology encounters), radiology assets, orders, KPIs and stock. A cross-department request returns 403/404 and is audited.
- **DA-3** Screens: **Department dashboard** (department-scoped operational summary and pending items, no cross-department data), **Staff and access** (users in the department; invite, role within permitted set, enable/disable, unlock, force logout, MFA reset with step-up), **Department audit log** (department only), **Department settings** (replaces "Hospital settings": schedule templates, appointment types/services offered, working hours, equipment list, notification preferences, shifts/rosters).
- **DA-4** Settings screen text says "Department", never "Patient identity"; remove hospital-wide settings and **MFA adoption** from this role.
- **DA-5** Cannot grant roles above their own, cannot touch other departments, cannot see PHI beyond what their department role allows.

### 6.6 IT / hospital admin (role ADMIN)

Keep the existing admin console (users, organization, audit, security policy, integrations, billing settings, tenant settings) and add **System status** (DB, backup, sync, integrations, worker health, version). **MFA adoption** lives here only. Add module-flag read-only view, equipment/asset admin, tariff admin (shared with billing manager), and role/permission administration. Remove the ".NET ONLINE" badge.

### 6.7 Laboratory (role LAB; sub-capabilities: technician, supervisor)

- **LB-1** Remove: Trends, Forms, Billing, Family, Sharing, Nearby Care, Find Care entirely.
- **LB-2** **Incoming orders worklist:** physician orders appear automatically with the patient profile needed to perform them (identity, age/sex, indication, allergies if relevant, pregnancy flag, collection requirements); status and **payment gate** shown ("Paid" required to proceed; "Bill-later/Emergency" and "Waived" allowed). Unpaid orders are visible but locked, with a "waiting for cashier" state.
- **LB-3** **Specimen workflow:** collection (who, when, container), barcode/label printing, transport, accessioning, rejection with reason and recollect request, storage/retention.
- **LB-4** **Analyzer registry (configuration-driven, expandable):** hematology analyzer, clinical chemistry analyzer, immunoassay, coagulation, urinalysis, blood gas/electrolytes, microbiology culture and identification/sensitivity, molecular (PCR), serology and blood bank, point-of-care devices, and others added by admin. Each analyzer has its test menu, assigned bench/location, status, maintenance, QC schedule, and reagent consumption. Physicians choose tests from the **test catalog**; the system routes each test to a capable analyzer/bench.
- **LB-5** **Result entry** manual or via instrument interface adapters (ASTM/HL7/CSV; design the adapter interface and a fake instrument for tests), with auto-flagging against reference ranges (age/sex specific), delta checks, critical value call-back log, technologist verification and supervisor release.
- **LB-6** **Send findings to the ordering physician:** a release action that delivers the verified report to the ordering physician's results inbox with notification and read/acknowledge tracking; critical values escalate. Also available in the chart.
- **LB-7** **QC:** daily QC entry, Levey-Jennings charts, rules, corrective action log; reagent and consumable stock using the stock framework; turnaround-time KPIs.
- **LB-8** Laboratory worklist can be filtered by priority (STAT first), source unit (ED, OPD, IPD, ICU, OR) and analyzer.

### 6.8 Nurse (role NURSE with unit assignment: ward, OPD, ED, ICU, NICU, OR, delivery)

- **NR-1** **Patient list by assigned unit** with class (inpatient/outpatient/emergency), bed, acuity flags, due tasks and medications.
- **NR-2** **Classification:** nurses and physicians can classify and re-classify patients as inpatient, outpatient or emergency (reason required, audited, permission-based), driven by ADT.
- **NR-3** **Nursing visit log:** every visit to a patient records time, nurse, **purpose of visit**, and the interventions performed, and the system shows **how many visits** a patient received per shift/day/admission. Interventions include: medication given (drug, dose, route, batch, **stock deducted**), **vaccine** (vaccine, dose number, site, lot, EPI schedule), **injection/shot**, **glucose reading**, vital signs, IV fluids and infusion rates, wound care, catheter care, oxygen therapy, intake/output, repositioning, education, other (configurable catalog). Each item can consume stock and, when billable, create charges.
- **NR-4** **Medication administration record (MAR)** with five-rights check, scheduled vs given vs withheld with reason, double-check for high-alert and controlled drugs.
- **NR-5** Shift handover (SBAR) per unit and patient; task list; incident reporting.
- **NR-6** Nursing assessments, I&O charts, pressure-ulcer risk and incidence (feeds KPIs).
- **NR-7** Nurses working in ICU/OR/NICU/delivery see those units' modules (below) through unit assignment; nurses elsewhere do not.

### 6.9 Pharmacy with warehouse and stock (role PHARMACIST; STOREKEEPER capability)

- **PHM-1** Pharmacy modules: **Dispensing**, **Prescription queue**, **Stock and warehouse**, **Requisitions**, **Controlled drugs**, **Analysis**.
- **PHM-2** **Stores:** central warehouse plus sub-stores (OPD pharmacy, IPD pharmacy, ED, ICU, OR, delivery suite, NICU, ward cupboards), with transfers and per-store balances (stock framework FND-13..17).
- **PHM-3** **Prescribing control ("which doctors can prescribe from which stock"):** each prescriber is linked to one or more dispensing stores/formularies by department and capability. A prescription can only be written for items that exist in a formulary the doctor is authorized for, shows live availability in the linked store, blocks out-of-stock or recalled batches (with alternative suggestions), enforces controlled-drug and restricted-antimicrobial approval rules, and records the dispensing store. Admins/pharmacy managers configure the doctor↔store/formulary mapping.
- **PHM-4** **Dispensing:** verify prescription, check allergy/interaction (production-blocked until a licensed source is configured), pick by FEFO, label, counsel, **payment gate check** (paid, waived, or bill-later), record dispense and stock deduction, partial fills, returns.
- **PHM-5** Inpatient/ICU/OR **ward stock and unit-dose** distribution with reconciliation; OR and ICU consumption reported back by the nursing/theatre records.
- **PHM-6** **APTS-style tools:** physical inventory, expiry tracking, ABC/VEN, stock status, stock transfers, tracer-medicine availability, wastage; eLMIS/eAPTS export interface (fake until a spec exists).
- **PHM-7** Alerts: low stock, near expiry, stock-out of vital medicines, controlled-drug discrepancy.

### 6.10 Emergency department (roles: TRIAGE_NURSE capability, ED nurse, ED physician)

- **ED-1** **Triage screen:** arrival registration (including unknown/unidentified patients with temporary ID and later merge), triage assessment by nurses, vital signs, chief complaint, **configurable triage scale** (the sources show a 3-level system in some Ethiopian hospitals and color-coded scales elsewhere; support configurable levels/colors such as red/orange/yellow/green with discriminators; record the national standard question in `docs/open-questions.md`), triage timestamp, and **arrival-to-triage time** with a 5-minute target indicator.
- **ED-2** ED tracking board (bays, status, assigned team, waiting times, length of stay with a 24-hour warning), ambulance pre-arrival notification, trauma/resuscitation flags.
- **ED-3** Fast order sets (STAT labs and imaging, emergency meds from the ED sub-store), **bill-later by default** (FND-11), no payment barrier.
- **ED-4** **Disposition:** discharge, admit to ward, **transfer to ICU** (critical patient flow), refer, take to OR, death (mortuary workflow). Non-emergency patients are redirected to central triage/OPD.
- **ED-5** Escalation alerts for abnormal vitals, overdue reassessment, and long stays. KPIs: triaged within 5 minutes, stay over 24 hours, ED mortality.

### 6.11 ICU (nurses and intensivists)

- **ICU-1** ICU bed board and admission from ED/OR/ward/delivery suite with transfer handover.
- **ICU-2** **Monitoring chart:** vitals, ventilator settings, fluid balance, sedation/pain scores, infusions, lines/tubes, organ-support flags, early-warning score and a configurable severity score.
- **ICU-3** **Machine usage and resource tracking (nurses' responsibility):** for every ventilator, monitor, infusion pump, dialysis machine, etc., nurses start/stop usage sessions per patient (FND-19); the system records duration, settings changes, consumables (circuits, filters, disposables), oxygen/medical gas use where measured, and computes resource consumption and cost per patient-day and per machine. Dashboard: machine utilization, idle time, maintenance due, consumables burn rate.
- **ICU-4** Medication/infusion administration with stock deduction from the ICU sub-store.
- **ICU-5** Transfer out to ward/OR/NICU (for neonates) with handover. Nurses assigned to ICU, and only them (plus intensivists), see this module.

### 6.12 Imaging and physiology (Radiology: X-ray, ultrasound, CT, MRI; Physiology: ECG, EEG, EMG)

- **RD-1** **Modality registry (configuration-driven):** X-ray, ultrasound, CT, MRI, plus physiology devices ECG, EEG and EMG (these are diagnostic physiology rather than radiology but are grouped in the diagnostic unit); each modality has rooms, devices, exam catalog, protocols, scheduling and maintenance.
- **RD-2** **Orders worklist** per modality with priority, indication, payment gate, prep instructions, contrast allergy and renal function prompts, **pregnancy safety guard (GUARD-1)**, transport tasks for inpatients and ED.
- **RD-3** **Technologist workflow:** check-in, performed procedure, dose recording for ionizing modalities (**radiation dose record**), device used (asset usage session), consumables (contrast, film/plates) deducted from stock.
- **RD-4** **Reporting:** radiologist/physiologist report with templates, preliminary and final, critical findings call-back, result delivery to the ordering physician (FND-7).
- **RD-5** **Images:** integrate an open-source PACS (recommend Orthanc with DICOMweb and the OHIF viewer, to be confirmed in an ADR; fully self-hosted for offline use). Provide DICOM Modality Worklist and image storage interfaces behind abstractions, with a fake for tests. Physiology waveforms stored as DICOM/files with metadata.
- **RD-6** Turnaround KPIs and volumes by modality.

### 6.13 Pathology

- **PT-1** **Case workflow:** accessioning (from OR, ED, wards, OPD, lab), gross description, tissue processing, blocks and slides tracking (barcodes), staining orders, microscopy, **diagnosis report** (synoptic templates for common cancers, configurable), addenda, sign-out, release to the ordering physician.
- **PT-2** Cytology and frozen section (intra-operative request from OR with fast turnaround and phone call-back record), special stains/IHC as catalog items, referral to external labs.
- **PT-3** Specimen and block archive with retention and retrieval; turnaround KPIs.
- **PT-4** Autopsy requests linked to the mortuary module (permissioned).

### 6.14 Operating theatre

- **OR-1** **Theatre scheduling:** theatres, sessions, elective and emergency lists, surgeon/anaesthetist/nurse assignments (shared staff pool with the delivery suite), priority and delay tracking ("delay for elective surgical admission" KPI).
- **OR-2** **Case record:** pre-op checklist, **WHO Surgical Safety Checklist** (sign-in, time-out, sign-out), consent record, anaesthesia record, procedure(s), timings, team, swab/instrument/sharps count, implants (lot/serial), specimens sent to pathology, complications, post-op orders and destination (PACU/ICU/ward/NICU for neonates).
- **OR-3** **Medicines and consumables used:** a case consumption sheet tracked against the theatre sub-store (FND-13..16) with per-case cost; drugs can be **prescribed by the surgeon/anaesthetist** into the case (subject to formulary rules); controlled drugs follow FND-17.
- **OR-4** Equipment sessions (anaesthesia machine, diathermy, etc.), sterilization batch tracking linkage if CSSD is configured.
- **OR-5** Surgical site infection surveillance feeds KPIs.

### 6.15 Delivery suite (labour and delivery)

- **DS-1** Admission in labour (from ED/OPD/ANC), labour record and **partograph** (WHO-style), maternal vitals, fetal heart monitoring, interventions, mode of delivery.
- **DS-2** **Delivery record:** time, mode, outcome (live birth/stillbirth), newborn details (sex, weight, APGAR 1/5/10 min), immediate newborn care, birth notification record, maternal complications (haemorrhage, eclampsia, etc.), referral and death review hooks (maternal and perinatal death surveillance is a national requirement; keep reporting configurable and note the data-set question in `docs/open-questions.md`).
- **DS-3** **Connections:** emergency caesarean → OR booking via the shared staff pool; mother → ward or ICU; **newborn → NICU** with a single transfer action; link mother and baby records (newborn gets own patient record and MRN, linked to mother).
- **DS-4** **GUARD-1** applies to radiation orders for pregnant patients. Delivery suite has its own sub-store for drugs and consumables.

### 6.16 NICU

- **NIC-1** Admission from delivery suite, ICU or OR (and ED/ward), with gestational age, birth weight, APGAR, maternal history linkage.
- **NIC-2** Neonatal chart: weight-based dosing (**mandatory weight and dose-per-kg calculations with hard safety limits**), feeding plan (breast milk, formula, TPN), respiratory support, phototherapy, thermal care, kangaroo mother care, infection surveillance, vitals/early-warning.
- **NIC-3** **Incubator/ventilator/phototherapy machine usage sessions** with resource and cost tracking (FND-19), consumables from the NICU sub-store.
- **NIC-4** Discharge planning, immunization/EPI schedule start, follow-up booking, mortality and morbidity KPIs.

### 6.17 Mortuary

- **MT-1** Death declaration (by authorized clinician) → death record and certificate document → mortuary intake (tag, location/cabinet, time, receiving staff), storage, **chain of custody** log, family notification record, release (verified identity and authorization, receipt), forensic/infectious flags, autopsy link to pathology.
- **MT-2** **GUARD-2** privacy rules apply; mortuary staff see only mortuary cases. Death data feeds mortality KPIs and vital-event reporting (configurable).

### 6.18 Support services

- **SS-1 Kitchen and dining (cafeteria):** **diet orders** from wards/NICU/ICU (diet type, allergies, restrictions, NPO), daily patient meal production list by ward, meal delivery confirmation, kitchen stock (stock framework), optional staff-cafeteria menu/orders (configurable, off by default).
- **SS-2 Housekeeping:** task queue auto-created by events (bed released at discharge/transfer → terminal cleaning; spill/biohazard requests; OR turnover cleaning), assignment, completion with timestamps that **return the bed to "available"**, waste handling checklists, supply stock.
- **SS-3 Laundry and central linen room:** linen items and par levels per ward, ward requisitions, soiled-linen collection, wash cycles, clean distribution, loss/damage counts, ledger per ward and linen item (stock framework with unit-based items).
- **SS-4 Storages/stores:** central and departmental stores, goods receipt, requisition/issue, stock counts, min/max alerts (FND-13..16).
- **SS-5 Administration/business office:** read-only operational finance views limited to non-clinical data (revenue totals, receivables aging), strictly segregated from clinical data (GUARD-3). Optional HR/roster integration left as future work.
- Support roles never access clinical data beyond the minimum (for example, the kitchen sees diet and allergy information only).

### 6.19 Patient dashboard (PATIENT / PROXY)

Keep Overview, Records, Appointments, **Forms (only here)**, Messages, Billing statements, Family/proxy, Sharing, Settings. **Remove Find Care and Nearby Facilities.** Billing statements show ETB. Existing hardening stays.

### 6.20 Registration / front desk (role REGISTRAR / SCHEDULER)

Patient registration and search, visits, queue tickets and routing (central triage), appointments scheduling per department, medical record room requests, cashier hand-off. No clinical content beyond what registration needs.

### 6.21 Platform and integration admins

Unchanged: platform admin has zero PHI; integration admin manages the clinical-core adapter, DHIS2 exports, MFR, eCHIS interface, PACS/analyzer interfaces and dead letters.

---

## 7. Cross-module acceptance scenarios (end-to-end tests)

1. **OPD lab flow:** register → central triage → physician orders CBC (hematology analyzer) → order shows `pending_payment` → cashier receives ETB payment and issues a receipt → order auto-releases to lab worklist with patient profile → technician runs and verifies → physician receives the report and acknowledges → audit trail complete.
2. **Unpaid guard:** the same flow without payment: lab sees the order but cannot start it; supervisor waiver with step-up releases it; waiver appears in the auditor's integrity signals.
3. **Emergency flow:** ambulance arrival → triage within target time recorded → STAT labs and imaging released as bill-later → disposition to ICU: single transfer, bed reserved, handover note, ICU nurse sees the patient; receivable shows in cashier queue.
4. **Radiation safety:** pregnant patient with an X-ray order triggers GUARD-1 and requires justification/approval; the override is audited.
5. **Prescribing control:** doctor linked to the OPD pharmacy cannot prescribe an item that is not in its formulary or is out of stock; controlled drug requires capability and dual signature; FEFO picks the earliest-expiry batch; stock ledger balances.
6. **Surgery:** elective case booked, checklist completed, consumables and drugs deducted from the theatre store, specimen sent to pathology, patient moved to ICU, ICU machine usage session started by a nurse, cost per patient-day computed.
7. **Delivery:** labour with partograph → delivery → newborn gets a linked record → critical newborn transferred to NICU → NICU incubator session logged → weight-based dosing guard prevents an overdose.
8. **Mortuary privacy:** death declared in the ward → patient disappears from general worklists/queue boards → mortuary case created; unauthorized roles cannot see it.
9. **Support loop:** diet order created for an inpatient → kitchen list updated; discharge triggers a housekeeping task; completion returns the bed to available; ward linen requisition issued from the central linen room.
10. **Auditor:** break-glass event appears in the review queue with justification; the auditor reviews and closes it; the hash-chain verification passes; the auditor cannot open any patient chart, message, form, billing line item or Find Care.
11. **Dept admin scope:** a radiology admin sees radiology data only; attempts against laboratory data return 403 and are logged.
12. **Cleanup:** automated checks that no Find Care/Nearby Facilities route, i18n key, nav item or script remains; no ".NET ONLINE" badge; MFA adoption appears only in IT; no browser dialogs; no CDN URLs.

---

## 8. Phases (stop at every gate)

**Phase 0 — Inspection and plan [STOP]**
Verify section 1 against the repo. Produce: gap analysis (what exists vs this spec) in `docs/hms-gap-analysis.md`; `docs/adr/0002-hms-module-ownership.md` (section 2.4); data model proposal and migration plan; a module dependency diagram; a sequenced backlog with estimates; PDF-library, PACS and instrument-interface ADRs; and questions for me. Do not change code.

**Phase 1 — Global cleanups [STOP]**
CL-1 to CL-9. Remove Find Care and Nearby Facilities everywhere, the ".NET ONLINE" badge, MFA adoption (except IT), staff Forms; implement role-driven navigation (CL-4). Update tests and seeds. Show before/after navigation per role.

**Phase 2 — Foundations [STOP]**
FND-1 to FND-26: patient class/ADT/locations/bed board, orders framework and result routing, billing gate with exceptions, stock ledger and stores, asset registry and usage sessions, tasks/notifications/rosters, permission catalog, KPI engine skeleton. Migrations with safe backfill. Tests for each invariant (ledger balances, FEFO, order state machine, gate exceptions, ADT atomicity).

**Phase 3 — Fix and rebuild existing dashboards [STOP after each role]**
Billing/cashier (6.1), Physician (6.2), Compliance auditor (6.3), Department admin (6.5), IT admin (6.6), Laboratory (6.7), Nurse (6.8), Registration (6.20). Include PDF export fix, Request Visit fix, My Patients list, messages selector, settings fixes, scoped admin views, dev seed accounts for each role.

**Phase 4 — Pharmacy, warehouse and stock [STOP]**
6.9, 6.18 SS-4 (stores), prescribing control, controlled drugs, APTS analytics.

**Phase 5 — Acute care units [STOP after each]**
Emergency (6.10), ICU (6.11), Operating theatre (6.14), Delivery suite (6.15), NICU (6.16), with the critical patient-flow transfers between them.

**Phase 6 — Diagnostics [STOP]**
Laboratory analyzers and QC depth (6.7), Imaging and physiology with PACS integration and GUARD-1 (6.12), Pathology (6.13).

**Phase 7 — Mortuary and support services [STOP]**
6.17 and 6.18 SS-1 to SS-3, SS-5.

**Phase 8 — KPIs and national reporting [STOP]**
6.4 indicators, dashboards for auditor/admin/dept admin, DHIS2 aggregate mapping configuration for the new indicators, FHIR endpoints for the new resources (section 5.3).

**Phase 9 — Hardening and proof [STOP]**
Authorization matrix tests for every role × endpoint, tenant isolation tests for every new table, offline/LAN-only test run, load test on worklists/bed board, accessibility audit, security regression run, section 7 end-to-end scenarios, updated README/dev logins/docs. Report anything not done.

---

## 9. Seeded development accounts and data

Extend the existing dev-only seeding (never in Production; guarded) with one account per role in section 6 (including triage nurse, ICU nurse, theatre nurse, midwife, anaesthetist, radiographer, radiologist, pathologist, pharmacist, storekeeper, cashier, registrar, mortuary attendant, kitchen, housekeeping, laundry/linen) plus two departments of each type, realistic Addis Ababa data (wards, beds, theatres, delivery rooms), a demo formulary, demo stock with batches near expiry, demo tariffs in ETB, demo analyzers and modalities, and demo patients in every class and location. Update `scripts/dev-logins.sh` to print them. Mark all clinical seed data DEMO.

## 10. Definition of done (every requirement)

Implemented, authorized (role + tenant + department), audited, translated (en/am/om), accessible, offline-tolerant, covered by unit/integration/authorization tests, documented in `docs/`, listed in `docs/progress.md`, and demonstrated with command output. If a requirement cannot be completed, say so and say why. Never claim a step is done without showing the evidence.