# ADR 0016: PACS and DICOMweb Boundary

**Status:** Proposed  
**Date:** 2026-11-05

## Decision

Integrate imaging through a self-hosted PACS adapter exposing DICOMweb
interfaces (`QIDO-RS`, `WADO-RS`, and `STOW-RS`) and modality worklist
abstractions. Orthanc with its DICOMweb support and OHIF as a LAN-hosted viewer
is the candidate, not an approved clinical deployment. The adapter must have a
fake implementation for tests and must never make internet access a runtime
requirement.

Radiation safety and pregnancy checks remain native workflow guards. The PACS
must not bypass tenant, department, patient-access, audit or release controls.
The final choice requires a facility PACS/device inventory, DICOM conformance
statements, retention policy, storage sizing, backup/restore test and licensing
review.
