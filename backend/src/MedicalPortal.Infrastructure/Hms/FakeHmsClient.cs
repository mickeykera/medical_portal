using System.Net;

namespace MedicalPortal.Infrastructure.Hms;

public class FakeHmsClient : IHmsClient
{
    private readonly object _lock = new();

    public Dictionary<string, HmsPatientDto> PatientsByMrn { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, HmsPatientDto> PatientsById { get; } = new();
    public Dictionary<int, HmsEmrBundleDto> EmrBundles { get; } = new();
    public Dictionary<int, List<HmsLabTestDto>> LabTests { get; } = new();
    public Dictionary<int, List<HmsAppointmentDto>> Appointments { get; } = new();

    public List<HmsCreateAppointmentRequestDto> CreatedAppointments { get; } = new();
    public List<(int AppointmentId, string Status)> UpdatedAppointmentStatuses { get; } = new();
    public List<HmsRegisterPatientRequestDto> RegisteredPatients { get; } = new();

    public int FailAttemptsRemaining { get; set; } = 0;
    public HttpStatusCode SimulatedStatusCode { get; set; } = HttpStatusCode.InternalServerError;
    public string SimulatedErrorMessage { get; set; } = "Simulated HMS error";
    public bool SimulateLockout { get; set; } = false;

    public void SeedPatient(HmsPatientDto patient)
    {
        lock (_lock)
        {
            PatientsById[patient.Id] = patient;
            PatientsByMrn[patient.GlobalId] = patient;
        }
    }

    public void SeedEmr(int patientId, HmsEmrBundleDto emr)
    {
        lock (_lock)
        {
            EmrBundles[patientId] = emr;
        }
    }

    public void SeedLabTest(int patientId, HmsLabTestDto test)
    {
        lock (_lock)
        {
            if (!LabTests.TryGetValue(patientId, out var list))
            {
                list = new List<HmsLabTestDto>();
                LabTests[patientId] = list;
            }
            list.Add(test);
        }
    }

    public void SeedAppointment(int patientId, HmsAppointmentDto appt)
    {
        lock (_lock)
        {
            if (!Appointments.TryGetValue(patientId, out var list))
            {
                list = new List<HmsAppointmentDto>();
                Appointments[patientId] = list;
            }
            list.Add(appt);
        }
    }

    private void CheckFailure()
    {
        lock (_lock)
        {
            if (SimulateLockout)
            {
                throw new HttpRequestException("Account is locked out: INVALID_CREDENTIALS", null, HttpStatusCode.Unauthorized);
            }
            if (FailAttemptsRemaining > 0)
            {
                FailAttemptsRemaining--;
                throw new HttpRequestException(SimulatedErrorMessage, null, SimulatedStatusCode);
            }
        }
    }

    public Task<string?> LoginAsync(CancellationToken cancellationToken = default)
    {
        CheckFailure();
        return Task.FromResult<string?>("fake_hms_jwt_token_8h");
    }

    public Task<HmsPatientDto?> SearchPatientByMrnAsync(string hmsMrn, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            PatientsByMrn.TryGetValue(hmsMrn, out var patient);
            return Task.FromResult(patient);
        }
    }

    public Task<HmsEmrBundleDto?> GetEmrAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            EmrBundles.TryGetValue(hmsPatientId, out var bundle);
            return Task.FromResult(bundle);
        }
    }

    public Task<List<HmsLabTestDto>> GetLabTestsAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            if (LabTests.TryGetValue(hmsPatientId, out var list))
            {
                return Task.FromResult(new List<HmsLabTestDto>(list));
            }
            return Task.FromResult(new List<HmsLabTestDto>());
        }
    }

    public Task<List<HmsAppointmentDto>> GetAppointmentsAsync(int hmsPatientId, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            if (Appointments.TryGetValue(hmsPatientId, out var list))
            {
                return Task.FromResult(new List<HmsAppointmentDto>(list));
            }
            return Task.FromResult(new List<HmsAppointmentDto>());
        }
    }

    public Task<HmsAppointmentDto?> CreateAppointmentAsync(HmsCreateAppointmentRequestDto request, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            CreatedAppointments.Add(request);
            var appt = new HmsAppointmentDto(
                Id: 1000 + CreatedAppointments.Count,
                PatientId: request.PatientId,
                DoctorId: request.DoctorId,
                ScheduledDate: request.ScheduledDate,
                DurationMinutes: request.DurationMinutes,
                AppointmentType: request.AppointmentType,
                Status: "Scheduled",
                Notes: request.Notes,
                CreatedAt: DateTime.UtcNow.ToString("o"),
                UpdatedAt: DateTime.UtcNow.ToString("o"));

            SeedAppointment(request.PatientId, appt);
            return Task.FromResult<HmsAppointmentDto?>(appt);
        }
    }

    public Task<bool> UpdateAppointmentStatusAsync(int appointmentId, string status, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            UpdatedAppointmentStatuses.Add((appointmentId, status));
            foreach (var list in Appointments.Values)
            {
                var found = list.FirstOrDefault(a => a.Id == appointmentId);
                if (found != null)
                {
                    list.Remove(found);
                    list.Add(found with { Status = status, UpdatedAt = DateTime.UtcNow.ToString("o") });
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(true);
        }
    }

    public Task<HmsRegisterPatientResponseDto?> RegisterPatientAsync(HmsRegisterPatientRequestDto request, CancellationToken cancellationToken = default)
    {
        CheckFailure();
        lock (_lock)
        {
            RegisteredPatients.Add(request);
            var id = 500 + RegisteredPatients.Count;
            var mrn = $"HMS-FAKE-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
            var patient = new HmsPatientDto(
                Id: id,
                GlobalId: mrn,
                FirstName: request.FirstName,
                LastName: request.LastName,
                DateOfBirth: request.DateOfBirth,
                Gender: request.Gender,
                BloodType: "O+",
                Email: request.Email,
                Phone: request.Phone,
                Address: request.Address,
                EmergencyContactName: null,
                EmergencyContactPhone: null,
                CreatedAt: DateTime.UtcNow.ToString("o"),
                UpdatedAt: DateTime.UtcNow.ToString("o"));

            SeedPatient(patient);
            return Task.FromResult<HmsRegisterPatientResponseDto?>(new HmsRegisterPatientResponseDto(true, mrn, patient));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            PatientsByMrn.Clear();
            PatientsById.Clear();
            EmrBundles.Clear();
            LabTests.Clear();
            Appointments.Clear();
            CreatedAppointments.Clear();
            UpdatedAppointmentStatuses.Clear();
            RegisteredPatients.Clear();
            FailAttemptsRemaining = 0;
            SimulateLockout = false;
        }
    }
}
