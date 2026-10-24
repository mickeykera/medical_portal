namespace MedicalPortal.Infrastructure.Hms;

public interface IHmsClient
{
    Task<string?> LoginAsync(CancellationToken cancellationToken = default);

    Task<HmsPatientDto?> SearchPatientByMrnAsync(string hmsMrn, CancellationToken cancellationToken = default);

    Task<HmsEmrBundleDto?> GetEmrAsync(int hmsPatientId, CancellationToken cancellationToken = default);

    Task<List<HmsLabTestDto>> GetLabTestsAsync(int hmsPatientId, CancellationToken cancellationToken = default);

    Task<List<HmsAppointmentDto>> GetAppointmentsAsync(int hmsPatientId, CancellationToken cancellationToken = default);

    Task<HmsAppointmentDto?> CreateAppointmentAsync(HmsCreateAppointmentRequestDto request, CancellationToken cancellationToken = default);

    Task<bool> UpdateAppointmentStatusAsync(int appointmentId, string status, CancellationToken cancellationToken = default);

    Task<HmsRegisterPatientResponseDto?> RegisterPatientAsync(HmsRegisterPatientRequestDto request, CancellationToken cancellationToken = default);
}
