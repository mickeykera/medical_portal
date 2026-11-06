# HMS Module Dependency Map

The HMS is a modular monolith. Solid arrows are application-service/event
dependencies; dotted arrows are adapter boundaries. Modules do not read one
another's tables directly.

```mermaid
flowchart TB
  AUTH["Auth / tenant / permissions / audit"]
  REG["Registration + MPI linkage"]
  ADT["ADT + locations + bed board"]
  ORD["Orders + results routing"]
  BILL["Billing + cashier + payment gate"]
  STOCK["Pharmacy + warehouse + stock ledger"]
  ACUTE["Emergency / inpatient / ICU / OR / delivery / NICU"]
  DIAG["Laboratory / pathology / imaging / physiology"]
  MORT["Mortuary"]
  SUPPORT["Kitchen / housekeeping / laundry / linen / stores"]
  REPORT["KPIs + DHIS2 / eCHIS / MFR"]
  CLIN["OpenMRS/O3 FHIR R4 adapter"]
  OUTBOX["Transactional outbox"]

  AUTH --> REG
  AUTH --> ADT
  AUTH --> ORD
  AUTH --> BILL
  AUTH --> STOCK
  AUTH --> ACUTE
  AUTH --> DIAG
  AUTH --> MORT
  AUTH --> SUPPORT
  AUTH --> REPORT
  REG --> ADT
  REG --> ORD
  ADT --> ACUTE
  ADT --> ORD
  ORD --> BILL
  BILL --> ORD
  ORD --> DIAG
  ORD --> STOCK
  ACUTE --> ORD
  ACUTE --> STOCK
  ACUTE --> MORT
  SUPPORT --> ADT
  STOCK --> REPORT
  ADT --> REPORT
  ORD --> REPORT
  BILL --> REPORT
  REG -. FHIR .-> CLIN
  ORD -. FHIR .-> CLIN
  DIAG -. FHIR/DICOM/ASTM .-> CLIN
  REPORT -. national adapters .-> OUTBOX
  REG --> OUTBOX
  ADT --> OUTBOX
  ORD --> OUTBOX
  BILL --> OUTBOX
  STOCK --> OUTBOX
  ACUTE --> OUTBOX
  DIAG --> OUTBOX
  MORT --> OUTBOX
  SUPPORT --> OUTBOX
```

Safety/privacy guards are cross-cutting: emergency care bypasses payment gates,
radiation orders require the pregnancy-safety workflow, mortuary records are
permissioned, and billing/support roles receive minimum-necessary identity and
operational data only.
