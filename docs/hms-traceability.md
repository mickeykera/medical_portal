# HMS Traceability and Phase 5-9 Evidence Review

Date: 2026-11-06  
Scope: evidence review only; no new HMS module was started.

## Status standard

`Done` requires API, UI, authorization, audit, tests, and translations.  
`Partial` means some implementation exists but one or more of those evidence
categories is missing. `Missing` means no credible implementation evidence was
found. Existing code locations are linked by repository-relative path below.
The test column says `none` where no requirement-specific test exists.

## Requirement matrix

| ID | Status | Code location / symbol | Test names | UI screen / role nav | Proof command |
|---|---|---|---|---|---|
| CL-1 | Partial | `CareAndGeoControllers.cs` deleted; `NavigationPolicy` | `RoleNavigationDoesNotExposeRetiredOrPatientOnlyModules` | role navigation | `rg -n 'findcare|nearby|/api/care|/api/geo' backend frontend` |
| CL-2 | Partial | `frontend/js/app.js` dashboards | none | dashboards | `rg -ni '.NET ONLINE' frontend` |
| CL-3 | Partial | `frontend/js/admin-dashboard.js` | none | admin security only not proven | `rg -ni 'MFA Adoption|mfaAdoption' frontend/js` |
| CL-4 | Partial | `NavigationPolicy.ForRole`, `AuthController` | 3 `NavigationPolicyTests` | server-filtered nav | `dotnet test ... --filter FullyQualifiedName~NavigationPolicyTests` |
| CL-5 | Partial | `frontend/js/app.js` settings | none | staff settings | `rg -n 'Patient settings|Infrastructure' frontend` |
| CL-6 | Partial | `NavigationPolicy` | `PatientNavigationIncludesFormsAndStaffNavigationDoesNot` | patient forms | `dotnet test ... --filter FullyQualifiedName~NavigationPolicyTests` |
| CL-7 | Partial | `app.js` PatientPicker | none | clinician/physician/nurse picker | `rg -n 'PatientPicker|patientPicker' frontend/js/app.js` |
| CL-8 | Partial | `frontend/js/app.js` escaped dialogs | `Frontend_PrintScript_ExistsAndHasZeroBrowserDialogs` | shared dialogs | `dotnet test ... --filter FullyQualifiedName~XssContainmentAndCspTests` |
| CL-9 | Partial | retired controller deletion | none | none | `rg -n 'findcare|nearby|facilities/nearby' .` |
| FND-1 | Partial | `HmsFoundationController.CreateEncounter` | `HmsIntegrationTests` only | ADT screens absent | `rg -n 'CreateEncounter|PatientClass' backend/src` |
| FND-2 | Partial | `Bed`, `HmsFoundationController` | `HmsIntegrationTests` only | bed board API, no complete UI | `rg -n 'bed-board|class Bed' backend/src` |
| FND-3 | Partial | location hierarchy and bed state | none specific | no location-management UI | `rg -n 'location-hierarchy|BedState' backend/src` |
| FND-4 | Missing | none | none | none | `rg -n 'asset registry|usage session' backend/src` |
| FND-5 | Missing | none | none | none | `rg -n 'clinical task|roster|notification' backend/src` |
| FND-6 | Partial | `ClinicalOrder`, `OrdersController` | `HmsIntegrationTests` | order worklists | `rg -n 'ClinicalOrder|worklist' backend/src` |
| FND-7 | Missing | no result-routing aggregate | none | none | `rg -n 'result routing|ResultRouting' backend/src` |
| FND-8 | Missing | none | none | none | `rg -n 'consult|referral task' backend/src` |
| FND-9 | Partial | `Tariff`, `ChargeItem`, `BillingController` | existing billing tests | billing dashboard | `rg -n 'Tariff|ChargeItem|cashier-worklist' backend/src` |
| FND-10 | Partial | `Receipt`, `BillingController` | existing payment tests | cashier/receipts | `rg -n 'Receipt|receipts' backend/src` |
| FND-11 | Partial | payment gate in `OrdersController` | existing order tests | order release feed | `rg -n 'PaymentGate|bill.later|Waived' backend/src` |
| FND-12 | Missing | no payer/claims workflow | none | none | `rg -n 'insurance|payer|claim' backend/src` |
| FND-13 | Partial | `InventoryController`, `StockLedgerEntry` | none specific | pharmacy dashboard alerts | `rg -n 'StockLedger|FEFO|inventory' backend/src` |
| FND-14 | Partial | `InventoryController.Issue` | none specific | pharmacy stock alerts | `rg -n 'quarantined|ExpiryDate|requisition' backend/src` |
| FND-15 | Partial | `InventoryController.Analytics` | none specific | pharmacy alerts only | `rg -n 'analytics|VenClass' backend/src` |
| FND-16 | Partial | issue patient/encounter fields | none specific | no procedure/cost-center UI | `rg -n 'PatientId.*EncounterId' backend/src/MedicalPortal.Core/Entities/InventoryEntities.cs` |
| FND-17 | Partial | `ControlledDrugEntry` | none specific | no controlled-drug screen | `rg -n 'ControlledDrug|controlled-drug' backend/src` |
| FND-18 | Missing | none | none | none | `rg -n 'formulary|antimicrobial' backend/src` |
| FND-19 | Missing | none | none | none | `rg -n 'MachineUsage|usage session|ventilator' backend/src` |
| FND-20 | Missing | none | none | none | `rg -n 'asset registry|maintenance due' backend/src` |
| FND-21 | Missing | none | none | none | `rg -n 'outbox.*event|domain event' backend/src` |
| FND-22 | Missing | none | none | none | `rg -n 'roster|shift' backend/src` |
| FND-23 | Missing | none | none | none | `rg -n 'notification.*task|task queue' backend/src` |
| FND-24 | Partial | `PermissionMatrix` | existing matrix tests | server authorization | `dotnet test ... --filter FullyQualifiedName~GranularRoleAndMatrixTests` |
| FND-25 | Partial | `KpiDefinition`, `KpiController` | none specific | admin KPI panel | `rg -n 'KpiDefinition|api/kpis' backend/src` |
| FND-26 | Missing | no complete KPI engine | none | none | `rg -n 'KpiSnapshot|Calculate.*Kpi' backend/src` |
| BL-1 | Partial | `NavigationPolicy` | navigation tests | billing nav | `rg -n 'billing.*settings' backend/src/MedicalPortal.Core/Domain/NavigationPolicy.cs` |
| BL-2 | Partial | `BillingController.CashierWorklist` | existing billing tests | billing dashboard | `rg -n 'cashier-worklist|receipts' backend/src` |
| BL-3 | Partial | order release feed | existing billing tests | billing dashboard | `rg -n 'order-release-feed|Waive' backend/src` |
| BL-4 | Missing | no inpatient running bill | none | none | `rg -n 'running bill|bed-day' backend/src` |
| BL-5 | Partial | `BillingController.Receivables` | none specific | receivables panel | `rg -n 'receivables|aging' backend/src` |
| BL-6 | Partial | existing refund request entities | payment tests | no complete approval UI | `rg -n 'Refund|refund' backend/src` |
| BL-7 | Missing | no cash shift aggregate | none | none | `rg -n 'cash shift|cashier shift' backend/src` |
| BL-8 | Partial | `OrdersController` tariff endpoints | existing order tests | no billing-manager screen | `rg -n 'Tariff' backend/src` |
| BL-9 | Missing | no revenue report/export | none | none | `rg -n 'revenue report|department revenue' backend/src` |
| BL-10 | Partial | payment-plan entities | payment tests | existing payment-plan surface | `rg -n 'PaymentPlan' backend/src frontend` |
| BL-11 | Partial | billing payload tightening | billing/privacy tests | cashier dashboard | `rg -n 'description|ClinicalOrderType' backend/src/MedicalPortal.Web/Controllers/BillingController.cs` |
| PH-1 | Partial | `app.js` PatientPicker | none specific | clinician dashboard | `rg -n 'PatientPicker|My Patients' frontend/js/app.js` |
| PH-2 | Partial | existing clinical controller/order APIs | clinical tests | chart tabs partial | `rg -n 'Orders and results|clinical' frontend/js/app.js` |
| PH-3 | Missing | no server PDF implementation evidence | none | no proven PDF export | `rg -n 'Pdf|PDF|export' backend/src/MedicalPortal.Web` |
| PH-4 | Partial | appointment APIs | scheduling tests | follow-up scheduling partial | `rg -n 'Request visit|Schedule follow' frontend/js` |
| PH-5 | Partial | message entities/controllers | message tests | message selector not proven | `rg -n 'MessageThread|messages' backend/src frontend/js` |
| PH-6 | Partial | inventory and clinical medication APIs | drug-safety tests | formulary UI missing | `rg -n 'formulary|prescrib' backend/src frontend/js` |
| PH-7 | Partial | `HmsFoundationController`, `AcuteCareController` | Hms tests only | ADT/acute screens incomplete | `rg -n 'TransferEncounter|acute-care' backend/src` |
| PH-8 | Partial | profile/settings UI | none specific | staff settings | `rg -n 'Patient settings|Infrastructure' frontend/js` |
| PH-9 | Partial | `NavigationPolicy` | navigation tests | role nav incomplete | `dotnet test ... --filter FullyQualifiedName~NavigationPolicyTests` |
| AU-1 | Partial | compliance controller | compliance tests | auditor dashboard | `rg -n 'ComplianceController|patient selector' backend/src frontend/js` |
| AU-2 | Partial | `ComplianceController` | compliance tests | audit explorer | `rg -n 'audit explorer|AuditLog' backend/src frontend/js` |
| AU-3 | Missing | no break-glass review queue | BreakGlassTests cover core only | none | `rg -n 'review queue|BreakGlass' backend/src` |
| AU-4 | Missing | no anomaly engine | none | none | `rg -n 'snooping|anomaly|outlier' backend/src` |
| AU-5 | Missing | no recertification workflow | none | none | `rg -n 'recertification|attest' backend/src` |
| AU-6 | Partial | audit logs/admin changes | compliance tests | partial audit UI | `rg -n 'Privilege|configuration|AuditLog' backend/src` |
| AU-7 | Partial | compliance summary | compliance tests | compliance summary | `rg -n 'billingIntegritySignals|compliance/summary' backend/src frontend/js` |
| AU-8 | Partial | audit hash-chain verification | compliance tests | integrity panel | `rg -n 'hash chain|integrity' backend/src` |
| AU-9 | Missing | no incident register | none | none | `rg -n 'incident register|breach' backend/src` |
| AU-10 | Partial | compliance summary/KPI endpoint | none specific | admin KPI panel | `rg -n 'api/kpis|compliance/summary' backend/src frontend/js` |
| DA-1 | Missing | no department KPI scope | none | none | `rg -n 'DA-1|department KPI' docs backend/src` |
| DA-2 | Missing | no department dashboard aggregate | none | none | `rg -n 'department dashboard' backend/src frontend/js` |
| DA-3 | Partial | department/location models | existing admin tests | admin facilities | `rg -n 'Department|Facility' backend/src` |
| DA-4 | Missing | no scoped export | none | none | `rg -n 'department.*export' backend/src` |
| DA-5 | Missing | no department operational queue/KPI scope | none | none | `rg -n 'department.*queue|department.*KPI' backend/src frontend` |
| LB-1 | Partial | `DiagnosticsController` | none specific | lab worklist | `rg -n 'DiagnosticWorkItem|worklist' backend/src` |
| LB-2 | Partial | specimen/accession fields | none | lab specimen table | `rg -n 'SpecimenType|AccessionNumber' backend/src` |
| LB-3 | Missing | no analyzer registry | none | none | `rg -n 'Analyzer|instrument' backend/src` |
| LB-4 | Missing | no QC workflow | none | none | `rg -n 'quality control|QC' backend/src` |
| LB-5 | Partial | preliminary/final result fields | none | no result-entry screen | `rg -n 'PRELIMINARY|FINAL|RecordResult' backend/src` |
| LB-6 | Missing | no reference ranges/flags | none | none | `rg -n 'reference range|critical value' backend/src` |
| LB-7 | Missing | no lab TAT computation | none | none | `rg -n 'turnaround|TAT' backend/src` |
| LB-8 | Missing | no fake instrument adapter | none | none | `rg -n 'InstrumentAdapter|Fake.*Analyzer' backend/src` |
| NR-1 | Partial | existing DHIS2 controller/entities | NationalInteroperabilityTests | DHIS2 admin | `rg -n 'Dhis2|DHIS2' backend/src` |
| NR-2 | Partial | DHIS2 report dataset | NationalInteroperabilityTests | no new KPI mapping | `rg -n 'Dhis2Report|mapping' backend/src` |
| NR-3 | Missing | no new KPI indicator mapping | none | none | `rg -n 'IndicatorMapping' backend/src` |
| NR-4 | Missing | no new-resource FHIR endpoints | FHIR tests cover old resources | none | `rg -n 'DiagnosticWorkItem|AcuteCareEpisode' backend/src/MedicalPortal.Web/Controllers/Fhir*` |
| NR-5 | Partial | FHIR client seams | ClinicalCoreFhirIntegrationTests | integration admin | `rg -n 'FHIR|Fhir' backend/src/MedicalPortal.Infrastructure` |
| NR-6 | Missing | no national export for new data | none | none | `rg -n 'eLMIS|eAPTS|national export' backend/src` |
| NR-7 | Missing | no reporting reconciliation | none | none | `rg -n 'reconciliation.*report' backend/src` |
| PHM-1 | Partial | pharmacy dashboard/order worklist | none specific | pharmacist dashboard | `rg -n 'PHARMACIST|Pharmacy released' frontend/js/app.js` |
| PHM-2 | Partial | inventory stores/requisitions | none | no requisition UI | `rg -n 'requisitions|stores' backend/src` |
| PHM-3 | Partial | controlled drug API | none | no register UI | `rg -n 'controlled-drugs|ControlledDrug' backend/src` |
| PHM-4 | Partial | inventory analytics | none | stock alerts | `rg -n 'api/inventory/analytics' backend/src frontend/js` |
| PHM-5 | Missing | no prescription queue integration | none | released medication orders only | `rg -n 'prescription queue' backend/src frontend/js` |
| PHM-6 | Missing | no formulary restriction | none | none | `rg -n 'formulary' backend/src frontend/js` |
| PHM-7 | Missing | no APTS export | none | none | `rg -n 'APTS|eLMIS' backend/src` |
| ED-1 | Partial | `AcuteCareController.CreateEpisode` | none specific | acute worklist only | `rg -n 'CreateEpisode|EMERGENCY' backend/src` |
| ED-2 | Partial | `Triage`, `TriageAcuity` | none | acute worklist | `rg -n 'TriageAcuity|triagedAt' backend/src` |
| ED-3 | Missing | no ED order sets/bill-later automation | none | none | `rg -n 'order set|bill-later.*emergency' backend/src` |
| ED-4 | Partial | `Disposition` endpoint | none | no disposition UI | `rg -n 'EmergencyDisposition|Disposition' backend/src` |
| ED-5 | Missing | no alert engine/KPI timing | none | none | `rg -n 'overdue reassessment|ED.*24' backend/src` |
| ICU-1 | Partial | acute transfer endpoints | none | no ICU bed board | `rg -n 'TargetUnit.*ICU|ICU' backend/src/MedicalPortal.Web` |
| ICU-2 | Missing | none | none | none | `rg -n 'ventilator|fluid balance|sedation' backend/src` |
| ICU-3 | Missing | none | none | none | `rg -n 'machine usage|cost per patient' backend/src` |
| ICU-4 | Missing | none | none | none | `rg -n 'infusion administration' backend/src` |
| ICU-5 | Partial | acute transfer | none | no ICU transfer UI | `rg -n 'TRANSFER_REQUESTED|TRANSFERRED' backend/src` |
| RD-1 | Missing | no modality registry | none | none | `rg -n 'ModalityRegistry|modality registry' backend/src` |
| RD-2 | Partial | diagnostic modality and guard | none | no imaging worklist UI | `rg -n 'PregnancySafetyAcknowledged|Modality' backend/src` |
| RD-3 | Missing | no dose/device/consumable record | none | none | `rg -n 'radiation dose|device used|contrast' backend/src` |
| RD-4 | Partial | diagnostic preliminary/final result | none | no radiology reporting UI | `rg -n 'RecordResult|CriticalCallback' backend/src` |
| RD-5 | Partial | PACS UID field; ADR 0016 | none | no PACS viewer | `rg -n 'PacsStudyUid|DICOM|DICOMweb' backend` |
| RD-6 | Missing | no imaging TAT/volume KPI | none | none | `rg -n 'imaging.*turnaround|modality.*volume' backend/src` |
| PT-1 | Partial | generic diagnostic work item | none | no pathology workflow UI | `rg -n 'PATHOLOGY|DiagnosticWorkItem' backend/src` |
| PT-2 | Missing | none | none | none | `rg -n 'frozen section|IHC|cytology' backend/src` |
| PT-3 | Missing | none | none | none | `rg -n 'block archive|slide' backend/src` |
| PT-4 | Missing | none | none | none | `rg -n 'autopsy|pathology.*mortuary' backend/src` |
| OR-1 | Partial | acute booking API | none | booking UI missing | `rg -n 'AcuteCareBooking|OPERATING_THEATRE' backend/src` |
| OR-2 | Missing | none | none | none | `rg -n 'WHO checklist|time-out|sign-in' backend/src` |
| OR-3 | Missing | inventory only, no case sheet | none | none | `rg -n 'case consumption|theatre store' backend/src` |
| OR-4 | Missing | none | none | none | `rg -n 'sterilization|anaesthesia machine' backend/src` |
| OR-5 | Missing | none | none | none | `rg -n 'surgical site infection' backend/src` |
| DS-1 | Missing | none | none | none | `rg -n 'partograph|fetal heart' backend/src` |
| DS-2 | Missing | none | none | none | `rg -n 'APGAR|birth notification|stillbirth' backend/src` |
| DS-3 | Partial | delivery booking/acute transfer only | none | no mother-baby UI | `rg -n 'DELIVERY_SUITE|newborn|mother-baby' backend/src` |
| DS-4 | Partial | shared diagnostic pregnancy guard | none | no delivery UI | `rg -n 'PregnancySafetyAcknowledged' backend/src` |
| NIC-1 | Partial | acute transfer to NICU | none | no NICU UI | `rg -n 'TargetUnit.*NICU|NICU' backend/src` |
| NIC-2 | Missing | none | none | none | `rg -n 'dose.per.kg|feeding plan|incubator' backend/src` |
| NIC-3 | Missing | none | none | none | `rg -n 'phototherapy|incubator session' backend/src` |
| NIC-4 | Missing | none | none | none | `rg -n 'EPI|NICU.*follow' backend/src` |
| MT-1 | Partial | `MortuaryController` | none specific | no mortuary nav/UI | `rg -n 'MortuaryCase|MortuaryCustody' backend/src` |
| MT-2 | Partial | `MortuaryController.CanAccess` | none | no GUARD-2 suppression | `rg -n 'CanAccess|MortuaryCases' backend/src` |
| SS-1 | Partial | `SupportNutritionController` diet orders | none | no kitchen role nav | `rg -n 'DietOrder|meal-list' backend/src` |
| SS-2 | Partial | `SupportServicesController`, discharge hook | none | no housekeeping nav | `rg -n 'TERMINAL_CLEANING|SupportServiceTask' backend/src` |
| SS-3 | Partial | `LinenLedgerEntry` | none | no linen UI | `rg -n 'LinenLedger|linen' backend/src` |
| SS-4 | Partial | inventory stores/requisition | none | pharmacy only | `rg -n 'InventoryStore|requisitions' backend/src` |
| SS-5 | Missing | no read-only business-office role surface | none | none | `rg -n 'business office|non-clinical finance' backend/src frontend` |
| GUARD-1 | Partial | `DiagnosticsController.Create` pregnancy check | none | no approval/override UI | `rg -n 'PregnancySafetyAcknowledged' backend/src` |
| GUARD-2 | Partial | `MortuaryController.CanAccess` | none | no deceased suppression | `rg -n 'MortuaryCase|deceased|Death' backend/src` |
| GUARD-3 | Missing | none | none | none | `rg -n 'privacy guard|mortuary privacy' backend/src` |
| GUARD-4 | Missing | none | none | none | `rg -n 'GUARD-4|privacy guard' backend/src docs` |

## Section 7 scenario matrix

| # | Status | Code location | Test names | UI / role nav | Proof command |
|---|---|---|---|---|---|
| 1 | Missing | order/billing/lab pieces only | none | partial billing/lab screens | `dotnet test ... --filter FullyQualifiedName~HmsIntegrationTests` |
| 2 | Partial | `OrdersController` release gate | none scenario-specific | billing release feed | `rg -n 'Waive|PaymentGate' backend/src` |
| 3 | Missing | acute episode/transfer pieces only | none | acute worklist only | `rg -n 'AcuteCareController|bed reservation|handover' backend/src` |
| 4 | Partial | diagnostic pregnancy acknowledgement | none | no override approval UI | `rg -n 'PregnancySafetyAcknowledged' backend/src` |
| 5 | Partial | inventory FEFO/controlled-drug APIs | none | pharmacy alerts only | `rg -n 'FEFO|ControlledDrug|formulary' backend/src` |
| 6 | Missing | booking API only | none | no OR/ICU case UI | `rg -n 'checklist|consumption|machine usage' backend/src` |
| 7 | Missing | delivery booking and NICU transfer only | none | no delivery/NICU UI | `rg -n 'partograph|APGAR|dose.per.kg' backend/src` |
| 8 | Partial | restricted mortuary controller | none | no deceased suppression | `rg -n 'MortuaryController|MortuaryCases' backend/src` |
| 9 | Partial | diet, cleaning, linen APIs | none | no kitchen/linen nav | `rg -n 'DietOrder|TERMINAL_CLEANING|LinenLedger' backend/src` |
| 10 | Partial | compliance summary/hash verification | compliance tests | auditor dashboard | `rg -n 'break.glass|integrity|compliance/summary' backend/src` |
| 11 | Missing | no department diagnostic scope | none | no radiology-admin role | `rg -n 'DEPT_ADMIN|radiology scope' backend/src` |
| 12 | Partial | cleanup/navigation/XSS tests | NavigationPolicyTests, XssContainmentAndCspTests | role nav | `dotnet test ... --filter FullyQualifiedName~NavigationPolicyTests\|FullyQualifiedName~XssContainmentAndCspTests` |

## Test-count explanation

The reported `424 passed` count stayed unchanged because Phases 5, 6, 7, 8,
and 9 added **zero requirement-specific test files or test methods**. The only
HMS test file added after the original baseline was
`NavigationPolicyTests.cs` during Phase 1, containing three tests:

1. `PatientNavigationIncludesFormsAndStaffNavigationDoesNot`
2. `RoleNavigationDoesNotExposeRetiredOrPatientOnlyModules`
3. `BillingNavigationIsLimitedToBillingAndSettings`

`AssemblyInfo.cs` changed xUnit parallelization behavior but added no tests.
The Phase 5-9 test-file audit command was:

```bash
git diff --name-only -- backend/tests/MedicalPortal.Tests
git ls-files --others --exclude-standard backend/tests/MedicalPortal.Tests
```

Its output contained only pre-existing modified files plus
`AssemblyInfo.cs` and `NavigationPolicyTests.cs`; no Phase 5-9 test additions.
The discovery command reported the same suite count used by the gates:

```bash
dotnet test backend/tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj \
  --no-build --no-restore -m:1 --list-tests
```

## PostgreSQL 17 migration and smoke check

Compose reports PostgreSQL `postgres:17-alpine`. A fresh database named
`hms_traceability` was created in the compose PostgreSQL container. Applying
all migrations succeeded:

```text
Applying migration '20261023135431_InitialCreate'.
...
Applying migration '20261106074254_AddKpiFoundation'.
Done.
```

The first EF command used `ConnectionStrings__DefaultConnection`; the
application actually reads `POSTGRES_CONNECTION_STRING`, so that attempt
incorrectly fell back to the development SQLite connection and failed against
`127.0.0.1:5432`. Retrying with the application's actual setting succeeded:

```bash
POSTGRES_CONNECTION_STRING="Host=<postgres-container-ip>;Port=5432;\
Database=hms_traceability;Username=postgres;Password=postgres" \
dotnet ef database update --project backend/src/MedicalPortal.Infrastructure \
  --startup-project backend/src/MedicalPortal.Web --no-build
```

The migrations themselves did not fail as SQLite-specific. The repository
does contain an intentional SQLite development/test fallback in
`Program.cs` (`UseSqlite`) and `appsettings.Development.json`; production
configuration uses PostgreSQL. SQLite-specific test artifacts include the
shared `medical_portal.db` and `Microsoft.EntityFrameworkCore.Sqlite`
dependency.

The requested focused PostgreSQL smoke suite was attempted with
`MigrationsTests`, `MultiTenancySecurityTests`, and `HmsIntegrationTests`.
It returned `11 failed, 14 passed, 0 skipped` because the compose database has
no host-published port and the container IP was unreachable from the host test
process (`NpgsqlException: No route to host` / timeout). This is an environment
connectivity failure, not a passing smoke result. The migration application
was independently successful inside the reachable EF CLI process.

## GUARD-2 finding

Mortuary case reads are currently allowed only for `ADMIN`, `SYSTEM_ADMIN`, and
`DEPT_ADMIN`, via `MortuaryController.CanAccess` in
`backend/src/MedicalPortal.Web/Controllers/MortuaryController.cs`. `PATIENT`,
`CLINICIAN`, `PHYSICIAN`, `NURSE`, `BILLING`, `LAB`, `PHARMACIST`, and other
roles are denied by that method.

Deceased patients are **not** currently hidden from general worklists and
queue boards. `HmsFoundationController.GetBedBoard`,
`OrdersController` worklists, `DiagnosticsController.Worklist`, and other
general queries do not join to `MortuaryCases` or filter deceased status.
There is also no general deceased/death status field on `Patient`. Therefore
GUARD-2 is `Partial`, not `Done`.

## Item 1 gate: PostgreSQL test infrastructure

Item 1 added two infrastructure tests:

1. `PostgresTestInfrastructureTests.DevelopmentComposeOverridePublishesPostgresOnlyOnLoopback`
2. `PostgresTestInfrastructureTests.ContinuousIntegrationDefinesPostgres17TestService`

The test count rose from the previous 424-test baseline to 426:

```text
Before: 424
After: 426
Infrastructure test run: Failed: 0, Passed: 2, Total: 2
SQLite full suite: Failed: 0, Passed: 426, Total: 426
```

The implementation is in `docker-compose.override.yml`, `.github/workflows/ci.yml`,
and `backend/src/MedicalPortal.Web/Program.cs`. The compose override publishes
PostgreSQL only on `127.0.0.1`; CI uses PostgreSQL 17 and the development
SQLite fallback requires both Development environment and
`Database:AllowSqliteFallback=true`.

A fresh PostgreSQL 17 database accepted every migration through
`AddKpiFoundation`. PostgreSQL DateTime compatibility was fixed by normalizing
tracked DateTime values to UTC before persistence in `MedicalPortalDbContext`.

The PostgreSQL suite result is not yet a green gate. The clean full run
returned:

```text
PostgreSQL: Failed: 3, Passed: 423, Skipped: 0, Total: 426
Failures:
- AuthorizationAndPaymentsSecurityTests.Refills_Clinician_ApprovingCrossPatientRefill_ReturnsNotFound
  Expected NotFound, Actual Forbidden
- AuthorizationAndPaymentsSecurityTests.ProxyInvite_CodeEntropy_AtLeast128Bits
  Expected Created, Actual Forbidden
- AuthorizationAndPaymentsSecurityTests.ProxyInvite_Acceptance_ByDifferentEmail_RejectedWithBadRequest
  Expected BadRequest, Actual Forbidden
```

The three failures pass when the authorization test class is run against a
fresh PostgreSQL database in isolation (12/12), indicating cross-test database
state contamination rather than a migration failure. The CI workflow now
requests serialized xUnit collections with `-- -parallel none`; this still
needs a follow-up validation in the CI-compatible runner invocation.
Until that rerun is green, Item 1 remains `Partial`.

Current matrix counts after this item: **Done: 0, Partial: 136, Missing: 0**.
No GUARD-2 or later ordered item was started.

## Item 1 RLS addendum

The runtime database roles are now separated:

- `portal_owner` is a non-superuser migration/schema owner with `BYPASSRLS`
  only for migrations and backfills.
- `portal_app` is `LOGIN`, `NOSUPERUSER`, `NOBYPASSRLS`, `NOCREATEDB`, and
  `NOCREATEROLE`, and does not own application tables.
- The web and worker compose services use `portal_app`; the migration service
  uses `portal_owner`. Passwords are required from environment variables and
  are not the PostgreSQL bootstrap password.
- `portal_app` has no `UPDATE` or `DELETE` grant on audit, stock-ledger,
  payment, receipt, or mortuary chain-of-custody ledgers.

Evidence commands and results:

```text
docker compose -f docker-compose.yml -f docker-compose.override.yml config
  PASS: PostgreSQL 17 is loopback-only in the development/test override;
  production compose has no host database port.

dotnet test backend/tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj \
  --filter FullyQualifiedName~PostgresRlsSecurityTests
  PASS: Failed 0, Passed 6, Total 6

dotnet build backend/MedicalPortal.sln --no-restore --configuration Debug
  PASS: 0 warnings, 0 errors
```

The six RLS tests cover role attributes, forced policy coverage for every
`TenantId` table, append-only grants, no-tenant fail-closed reads, transaction
local-setting leakage on a one-connection pool, and cross-tenant
`WITH CHECK` rejection. The dynamic policy query currently reports no
Phase 2-8 tenant-owned tables missing `relrowsecurity`,
`relforcerowsecurity`, and a policy.

A fresh PostgreSQL 17 database was created, migrations were applied as
`portal_owner`, and seeding completed successfully after granting the owner
its explicitly permitted backfill `BYPASSRLS`:

```text
Applying database migrations...
Database migrations applied successfully.
Seeding database...
Database seeded successfully.
```

The first clean full run as `portal_app` was **Failed: 22, Passed: 410,
Skipped: 0, Total: 432**. The failures are not hidden: they include existing
production-startup expectation tests, payment/refill/proxy tests, tenant
productization tests, and development-login tests. The RLS-specific failures
in that run were caused by the test process not exporting
`PORTAL_APP_PASSWORD`; rerunning the focused RLS suite with the generated
password exported passed 6/6. A second full run still has 22 non-RLS failures,
so Item 1 is not complete and no later item is started.

The test count changed from the original 424 baseline to 426 after the
PostgreSQL infrastructure tests, and to 432 after the six RLS tests and
related coverage. No tests were added in Phases 5-9; the prior unchanged
424 count was therefore evidence that those phase gates had not added
requirement-specific tests.

`DISABLE_MFA` and `MULTI_TENANCY_ENABLED` were legacy environment variables
present in the development compose configuration and in a manual test
command. Searches of `backend/src` found no reads of either variable. They
were removed from the main development service; they do not control backend
behavior.

Current matrix counts remain **Done: 0, Partial: 136, Missing: 0** under the
conservative matrix above. Item 1 remains **Partial** because the complete
`portal_app` suite is not green. Work stops here pending the next instruction.
