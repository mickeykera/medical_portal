# Regulatory & Compliance Architecture Notes: Adot Internal Medicine (Adot-Hospital)

> **Document Status**: DRAFT TECHNICAL CONTROL MAPPING  
> **Applicable Jurisdiction**: Federal Democratic Republic of Ethiopia, Addis Ababa  
> **Relevant Legal Framework**:
> - Proclamation No. 1205/2020 (Electronic Transactions and Digital Signature)
> - Ministry of Health Digital Health Strategy & Health Data Governance Directives
> - Ministry of Innovation and Technology (MInT) Personal Data Protection Directive (Draft / In Force)
> - National HMIS Data Reporting Requirements (DHIS2 / Ministry of Health)
>
> **Notice**: **This document does NOT claim legal compliance.** It provides an engineering mapping of implemented technical safeguards and records open items for legal and regulatory review with hospital counsel and the Ministry of Health.

---

## 1. Technical Controls Mapping

| Control Identifier | Technical Implementation | Supported Requirement / Objective | Implementation Status |
| :--- | :--- | :--- | :--- |
| **CTL-ETH-01: Data Residency & Sovereignty** | Hospital on-premises deployment topology; dedicated local PostgreSQL database (`adot_portal_db`) on hospital LAN; no external cloud transmission without explicit export. | Support national requirement for local storage of citizens' sensitive health records within Ethiopia. | **Implemented** (On-premises hosting model verified) |
| **CTL-ETH-02: Cryptographic Tamper-Evidence** | SHA-256 hash-chained append-only audit log with genesis block verification; DB trigger and EF Core prevention of record updates or deletes (`EnforceAuditLogAppendOnly`). | Support evidentiary admissibility of electronic health records under Proclamation No. 1205/2020. | **Implemented** |
| **CTL-ETH-03: Multi-Factor Authentication (Staff & Patients)** | Mandatory TOTP enrollment (RFC 6238, SHA-1/SHA-256) with single-use cryptographic recovery codes; enforced on staff and patient logins (`mfaStaff: true`, `mfaPatients: true`). | Defense against unauthorized credential access and credential stuffing. | **Implemented** |
| **CTL-ETH-04: Session Lifecycle & Idle Timeout** | Authoritative database-backed session validation on every request; 15-minute idle timeout (`idleTimeoutMinutes: 15`); immediate revocation on logout or credential change. | Mitigation of unauthorized access on unattended hospital terminals. | **Implemented** |
| **CTL-ETH-05: Emergency Break-Glass Access** | Explicit break-glass protocol (`POST /api/clinical/break-glass`) requiring documented clinical justification; high-severity immediate audit broadcast via SignalR and persistent tamper-evident audit log. | Clinical safety compliance during emergency resuscitation or urgent care. | **Implemented** |
| **CTL-ETH-06: 20-Year Retention Policy** | `retentionYears: 20` configured in tenant security settings; soft-deletion tombstones; deletion prohibited in database schema. | Hospital record retention mandate (minimum 20 years for adult medical records). | **Implemented** (Configured & enforced) |
| **CTL-ETH-07: Dual Calendar Reporting** | Gregorian storage (UTC timestamps) with Ethiopian calendar calculation layer (Amete Mihret / Amharic months). | HMIS reporting alignment with Ethiopian fiscal year (Hamle to Sene) and local date displays. | **Pending Phase 7** |
| **CTL-ETH-08: Localization** | English, Amharic (`am`), and Afaan Oromoo (`om`) language support without external CDN dependencies. | Language rights and patient accessibility under Ethiopian health delivery policies. | **Pending Phase 6** |
| **CTL-ETH-09: National Interoperability (DHIS2/eCHIS/MFR)** | Standardized FHIR R4 internal representation; DHIS2 aggregate aggregate push adapter; MFR facility sync behind interfaces. | Ministry of Health national reporting integration. | **Pending Phase 9 & 10** |

---

## 2. Open Items & Verification Gaps

1. **National Data Protection Proclamation Formal Ratification**:
   - Status: Open.
   - Action: Once the federal Data Protection Proclamation is gazetted, verify appointment of local Data Protection Officer (DPO) and record registration requirements with MInT.
2. **Ministry of Health EHR Certification**:
   - Status: Open.
   - Action: Review Ministry of Health EMR/EHR guidelines with Adot Internal Medicine clinical directorate prior to clinical go-live.
3. **Telecommunications Regulatory Approval**:
   - Status: Open.
   - Action: Ethio Telecom / Ethiopian Communications Authority (ECA) shortcode and SMS aggregator registration for appointment reminder SMS delivery.
4. **Electronic Prescription Admissibility**:
   - Status: Open.
   - Action: Confirm Ethiopian Food and Drug Authority (EFDA) digital prescription regulations and requirements for cryptographic clinician signatures under Proclamation 1205/2020.
