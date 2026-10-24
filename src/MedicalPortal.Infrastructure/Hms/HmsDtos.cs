using System.Text.Json.Serialization;

namespace MedicalPortal.Infrastructure.Hms;

public record HmsLoginResponseDto(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user")] HmsUserDto? User);

public record HmsUserDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("department")] string? Department);

public record HmsPatientDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("global_id")] string GlobalId,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("date_of_birth")] string? DateOfBirth,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("blood_type")] string? BloodType,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("emergency_contact_name")] string? EmergencyContactName,
    [property: JsonPropertyName("emergency_contact_phone")] string? EmergencyContactPhone,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("updated_at")] string? UpdatedAt);

public record HmsMedicalHistoryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("diagnosis_date")] string? DiagnosisDate,
    [property: JsonPropertyName("treatment")] string? Treatment,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public record HmsConsultationDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("visit_id")] int? VisitId,
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("doctor_id")] int? DoctorId,
    [property: JsonPropertyName("chief_complaint")] string? ChiefComplaint,
    [property: JsonPropertyName("diagnosis")] string? Diagnosis,
    [property: JsonPropertyName("subjective")] string? Subjective,
    [property: JsonPropertyName("objective")] string? Objective,
    [property: JsonPropertyName("assessment")] string? Assessment,
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("signed_at")] string? SignedAt,
    [property: JsonPropertyName("updated_at")] string? UpdatedAt,
    [property: JsonPropertyName("consultation_date")] string? ConsultationDate);

public record HmsPrescriptionDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("consultation_id")] int? ConsultationId,
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("medication_name")] string MedicationName,
    [property: JsonPropertyName("dosage")] string? Dosage,
    [property: JsonPropertyName("frequency")] string? Frequency,
    [property: JsonPropertyName("duration_days")] int? DurationDays,
    [property: JsonPropertyName("instructions")] string? Instructions,
    [property: JsonPropertyName("prescribing_doctor_id")] int? PrescribingDoctorId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("dispensed")] int? Dispensed,
    [property: JsonPropertyName("dispensed_at")] string? DispensedAt,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public record HmsEmrBundleDto(
    [property: JsonPropertyName("patient")] HmsPatientDto Patient,
    [property: JsonPropertyName("medical_history")] List<HmsMedicalHistoryDto>? MedicalHistory,
    [property: JsonPropertyName("consultations")] List<HmsConsultationDto>? Consultations,
    [property: JsonPropertyName("prescriptions")] List<HmsPrescriptionDto>? Prescriptions);

public record HmsLabResultDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("lab_test_id")] int LabTestId,
    [property: JsonPropertyName("result_data")] string? ResultData,
    [property: JsonPropertyName("reference_range")] string? ReferenceRange,
    [property: JsonPropertyName("flagged")] int? Flagged,
    [property: JsonPropertyName("lab_tech_id")] int? LabTechId,
    [property: JsonPropertyName("entered_at")] string? EnteredAt);

public record HmsLabTestItemDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("order_doctor_id")] int? OrderDoctorId,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("test_type")] string? TestType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sample_id")] string? SampleId,
    [property: JsonPropertyName("ordered_at")] string? OrderedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt);

public record HmsLabTestDto(
    [property: JsonPropertyName("test")] HmsLabTestItemDto Test,
    [property: JsonPropertyName("results")] List<HmsLabResultDto>? Results);

public record HmsLabResponseDto(
    [property: JsonPropertyName("tests")] List<HmsLabTestDto>? Tests);

public record HmsAppointmentDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("doctor_id")] int? DoctorId,
    [property: JsonPropertyName("scheduled_date")] string ScheduledDate,
    [property: JsonPropertyName("duration_minutes")] int? DurationMinutes,
    [property: JsonPropertyName("appointment_type")] string? AppointmentType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("updated_at")] string? UpdatedAt);

public record HmsAppointmentsResponseDto(
    [property: JsonPropertyName("appointments")] List<HmsAppointmentDto>? Appointments);

public record HmsCreateAppointmentRequestDto(
    [property: JsonPropertyName("patient_id")] int PatientId,
    [property: JsonPropertyName("doctor_id")] int? DoctorId,
    [property: JsonPropertyName("scheduled_date")] string ScheduledDate,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("appointment_type")] string AppointmentType,
    [property: JsonPropertyName("notes")] string? Notes);

public record HmsRegisterPatientRequestDto(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("date_of_birth")] string DateOfBirth,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("address")] string? Address);

public record HmsRegisterPatientResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("global_id")] string? GlobalId,
    [property: JsonPropertyName("patient")] HmsPatientDto? Patient);

public record HmsWebhookPayloadDto(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("hmsMrn")] string HmsMrn,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("data")] object? Data);
