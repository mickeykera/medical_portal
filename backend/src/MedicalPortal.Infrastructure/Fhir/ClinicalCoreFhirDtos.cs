using System.Text.Json.Serialization;

namespace MedicalPortal.Infrastructure.Fhir;

// =============================================================================
// Common FHIR R4 Types
// =============================================================================

public record FhirCodingDto(
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("display")] string? Display);

public record FhirCodeableConceptDto(
    [property: JsonPropertyName("coding")] List<FhirCodingDto>? Coding,
    [property: JsonPropertyName("text")] string? Text);

public record FhirIdentifierDto(
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("use")] string? Use = null);

public record FhirReferenceDto(
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("display")] string? Display = null);

public record FhirPeriodDto(
    [property: JsonPropertyName("start")] string? Start,
    [property: JsonPropertyName("end")] string? End = null);

public record FhirHumanNameDto(
    [property: JsonPropertyName("use")] string? Use,
    [property: JsonPropertyName("family")] string? Family,
    [property: JsonPropertyName("given")] List<string>? Given);

public record FhirTelecomDto(
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("use")] string? Use = null);

public record FhirAddressDto(
    [property: JsonPropertyName("line")] List<string>? Line,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("postalCode")] string? PostalCode,
    [property: JsonPropertyName("country")] string? Country);

public record FhirQuantityDto(
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("system")] string? System = null,
    [property: JsonPropertyName("code")] string? Code = null);

// =============================================================================
// FHIR Bundle Type
// =============================================================================

public record FhirBundleEntryDto<T>(
    [property: JsonPropertyName("fullUrl")] string? FullUrl,
    [property: JsonPropertyName("resource")] T Resource);

public record FhirBundleDto<T>(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("entry")] List<FhirBundleEntryDto<T>>? Entry)
{
    public IEnumerable<T> Resources => Entry?.Select(e => e.Resource) ?? Enumerable.Empty<T>();
}

// =============================================================================
// 1. Patient Resource
// =============================================================================

public record FhirPatientDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("identifier")] List<FhirIdentifierDto>? Identifier,
    [property: JsonPropertyName("name")] List<FhirHumanNameDto>? Name,
    [property: JsonPropertyName("telecom")] List<FhirTelecomDto>? Telecom,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("birthDate")] string? BirthDate,
    [property: JsonPropertyName("address")] List<FhirAddressDto>? Address)
{
    public string? Mrn => Identifier?.FirstOrDefault(i => i.System?.Contains("mrn", StringComparison.OrdinalIgnoreCase) == true)?.Value
                          ?? Identifier?.FirstOrDefault()?.Value;

    public string? FullName
    {
        get
        {
            var officialName = Name?.FirstOrDefault();
            if (officialName == null) return null;
            var given = officialName.Given != null ? string.Join(" ", officialName.Given) : "";
            return $"{given} {officialName.Family}".Trim();
        }
    }
}

// =============================================================================
// 2. Encounter Resource
// =============================================================================

public record FhirEncounterParticipantDto(
    [property: JsonPropertyName("individual")] FhirReferenceDto? Individual,
    [property: JsonPropertyName("type")] List<FhirCodeableConceptDto>? Type);

public record FhirEncounterDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("class")] FhirCodingDto? Class,
    [property: JsonPropertyName("type")] List<FhirCodeableConceptDto>? Type,
    [property: JsonPropertyName("subject")] FhirReferenceDto? Subject,
    [property: JsonPropertyName("participant")] List<FhirEncounterParticipantDto>? Participant,
    [property: JsonPropertyName("period")] FhirPeriodDto? Period,
    [property: JsonPropertyName("reasonCode")] List<FhirCodeableConceptDto>? ReasonCode);

// =============================================================================
// 3. Observation Resource
// =============================================================================

public record FhirObservationComponentDto(
    [property: JsonPropertyName("code")] FhirCodeableConceptDto? Code,
    [property: JsonPropertyName("valueQuantity")] FhirQuantityDto? ValueQuantity,
    [property: JsonPropertyName("valueString")] string? ValueString);

public record FhirObservationDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("category")] List<FhirCodeableConceptDto>? Category,
    [property: JsonPropertyName("code")] FhirCodeableConceptDto? Code,
    [property: JsonPropertyName("subject")] FhirReferenceDto? Subject,
    [property: JsonPropertyName("effectiveDateTime")] string? EffectiveDateTime,
    [property: JsonPropertyName("valueQuantity")] FhirQuantityDto? ValueQuantity,
    [property: JsonPropertyName("valueString")] string? ValueString,
    [property: JsonPropertyName("component")] List<FhirObservationComponentDto>? Component);

// =============================================================================
// 4. AllergyIntolerance Resource
// =============================================================================

public record FhirAllergyReactionDto(
    [property: JsonPropertyName("substance")] FhirCodeableConceptDto? Substance,
    [property: JsonPropertyName("manifestation")] List<FhirCodeableConceptDto>? Manifestation,
    [property: JsonPropertyName("severity")] string? Severity);

public record FhirAllergyIntoleranceDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("clinicalStatus")] FhirCodeableConceptDto? ClinicalStatus,
    [property: JsonPropertyName("verificationStatus")] FhirCodeableConceptDto? VerificationStatus,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("category")] List<string>? Category,
    [property: JsonPropertyName("criticality")] string? Criticality,
    [property: JsonPropertyName("code")] FhirCodeableConceptDto? Code,
    [property: JsonPropertyName("patient")] FhirReferenceDto? Patient,
    [property: JsonPropertyName("recordedDate")] string? RecordedDate,
    [property: JsonPropertyName("reaction")] List<FhirAllergyReactionDto>? Reaction);

// =============================================================================
// 5. MedicationRequest Resource
// =============================================================================

public record FhirDosageInstructionDto(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("timing")] object? Timing,
    [property: JsonPropertyName("asNeededBoolean")] bool? AsNeededBoolean);

public record FhirMedicationRequestDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("medicationCodeableConcept")] FhirCodeableConceptDto? MedicationCodeableConcept,
    [property: JsonPropertyName("subject")] FhirReferenceDto? Subject,
    [property: JsonPropertyName("authoredOn")] string? AuthoredOn,
    [property: JsonPropertyName("requester")] FhirReferenceDto? Requester,
    [property: JsonPropertyName("dosageInstruction")] List<FhirDosageInstructionDto>? DosageInstruction);

// =============================================================================
// 6. Appointment Resource
// =============================================================================

public record FhirAppointmentParticipantDto(
    [property: JsonPropertyName("actor")] FhirReferenceDto? Actor,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("required")] string? Required = null);

public record FhirAppointmentDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("serviceCategory")] List<FhirCodeableConceptDto>? ServiceCategory,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("start")] string? Start,
    [property: JsonPropertyName("end")] string? End,
    [property: JsonPropertyName("participant")] List<FhirAppointmentParticipantDto>? Participant,
    [property: JsonPropertyName("comment")] string? Comment = null);

// =============================================================================
// CapabilityStatement Resource
// =============================================================================

public record FhirSoftwareDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string? Version);

public record FhirCapabilityStatementDto(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("fhirVersion")] string FhirVersion,
    [property: JsonPropertyName("software")] FhirSoftwareDto? Software);
