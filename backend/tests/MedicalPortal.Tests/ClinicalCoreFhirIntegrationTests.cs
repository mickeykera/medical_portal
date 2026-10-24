using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MedicalPortal.Infrastructure.Fhir;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedicalPortal.Tests;

public class ClinicalCoreFhirIntegrationTests
{
    private readonly FakeClinicalCoreFhirClient _fakeClient = new();

    // =========================================================================
    // 1. FHIR CAPABILITY STATEMENT & VERSION VALIDATION
    // =========================================================================

    [Fact]
    public async Task GetCapabilityStatement_ReturnsFhirR4_Metadata()
    {
        var capability = await _fakeClient.GetCapabilityStatementAsync();

        Assert.NotNull(capability);
        Assert.Equal("CapabilityStatement", capability.ResourceType);
        Assert.Equal("active", capability.Status);
        Assert.Equal("4.0.1", capability.FhirVersion);
        Assert.NotNull(capability.Software);
        Assert.Contains("OpenMRS", capability.Software.Name);
    }

    [Fact]
    public async Task CheckHealth_ReturnsTrue_WhenCoreIsAvailable()
    {
        var isHealthy = await _fakeClient.CheckHealthAsync();
        Assert.True(isHealthy);

        _fakeClient.SimulateNetworkFailure = true;
        var isUnhealthy = await _fakeClient.CheckHealthAsync();
        Assert.False(isUnhealthy);
    }

    // =========================================================================
    // 2. PATIENT RESOURCE INTEGRATION (READ BY ID & SEARCH BY MRN)
    // =========================================================================

    [Fact]
    public async Task GetPatientById_ReturnsCorrectDemographics()
    {
        var patient = await _fakeClient.GetPatientByIdAsync("patient-1001");

        Assert.NotNull(patient);
        Assert.Equal("Patient", patient.ResourceType);
        Assert.Equal("patient-1001", patient.Id);
        Assert.Equal("MRN-1001", patient.Mrn);
        Assert.Equal("Abebe Tadesse Kebede", patient.FullName);
        Assert.Equal("male", patient.Gender);
        Assert.Equal("1985-04-12", patient.BirthDate);
        Assert.NotNull(patient.Address);
        Assert.Equal("Addis Ababa", patient.Address[0].City);
        Assert.Equal("Ethiopia", patient.Address[0].Country);
    }

    [Fact]
    public async Task GetPatientByMrn_ResolvesPatient_CaseInsensitive()
    {
        var patient = await _fakeClient.GetPatientByMrnAsync("mrn-1001");

        Assert.NotNull(patient);
        Assert.Equal("patient-1001", patient.Id);
        Assert.Equal("MRN-1001", patient.Mrn);
    }

    [Fact]
    public async Task GetPatientByMrn_ReturnsNull_ForUnknownMrn()
    {
        var patient = await _fakeClient.GetPatientByMrnAsync("UNKNOWN-MRN-999");
        Assert.Null(patient);
    }

    // =========================================================================
    // 3. ENCOUNTER RESOURCE INTEGRATION
    // =========================================================================

    [Fact]
    public async Task GetEncounters_ReturnsValidEncounterBundle()
    {
        var bundle = await _fakeClient.GetEncountersAsync("patient-1001");

        Assert.NotNull(bundle);
        Assert.Equal("Bundle", bundle.ResourceType);
        Assert.Equal("searchset", bundle.Type);
        Assert.True(bundle.Total >= 1);

        var encounter = Assert.Single(bundle.Resources);
        Assert.Equal("Encounter", encounter.ResourceType);
        Assert.Equal("enc-5001", encounter.Id);
        Assert.Equal("finished", encounter.Status);
        Assert.Equal("AMB", encounter.Class?.Code);
        Assert.Equal("Patient/patient-1001", encounter.Subject?.Reference);
        Assert.NotNull(encounter.Period);
        Assert.Equal("2026-10-20T08:30:00Z", encounter.Period.Start);
    }

    // =========================================================================
    // 4. OBSERVATION RESOURCE INTEGRATION (VITALS & COMPONENTS)
    // =========================================================================

    [Fact]
    public async Task GetObservations_ReturnsVitalsWithPanelComponents()
    {
        var bundle = await _fakeClient.GetObservationsAsync("patient-1001", category: "vital-signs");

        Assert.NotNull(bundle);
        Assert.True(bundle.Total >= 1);

        var obs = Assert.Single(bundle.Resources);
        Assert.Equal("Observation", obs.ResourceType);
        Assert.Equal("final", obs.Status);
        Assert.Equal("85354-9", obs.Code?.Coding?[0].Code);
        Assert.Equal("Blood Pressure", obs.Code?.Text);

        Assert.NotNull(obs.Component);
        Assert.Equal(2, obs.Component.Count);

        var systolic = obs.Component.FirstOrDefault(c => c.Code?.Coding?[0].Code == "8480-6");
        Assert.NotNull(systolic);
        Assert.Equal(130m, systolic.ValueQuantity?.Value);
        Assert.Equal("mmHg", systolic.ValueQuantity?.Unit);

        var diastolic = obs.Component.FirstOrDefault(c => c.Code?.Coding?[0].Code == "8462-4");
        Assert.NotNull(diastolic);
        Assert.Equal(85m, diastolic.ValueQuantity?.Value);
        Assert.Equal("mmHg", diastolic.ValueQuantity?.Unit);
    }

    // =========================================================================
    // 5. ALLERGYINTOLERANCE RESOURCE INTEGRATION
    // =========================================================================

    [Fact]
    public async Task GetAllergies_ReturnsActiveAllergies_WithCriticality()
    {
        var bundle = await _fakeClient.GetAllergiesAsync("patient-1001");

        Assert.NotNull(bundle);
        Assert.True(bundle.Total >= 1);

        var allergy = Assert.Single(bundle.Resources);
        Assert.Equal("AllergyIntolerance", allergy.ResourceType);
        Assert.Equal("allergy-3001", allergy.Id);
        Assert.Equal("high", allergy.Criticality);
        Assert.Equal("active", allergy.ClinicalStatus?.Coding?[0].Code);
        Assert.Equal("confirmed", allergy.VerificationStatus?.Coding?[0].Code);
        Assert.Equal("Penicillin G", allergy.Code?.Text);
        Assert.Equal("Patient/patient-1001", allergy.Patient?.Reference);
    }

    // =========================================================================
    // 6. MEDICATIONREQUEST RESOURCE INTEGRATION
    // =========================================================================

    [Fact]
    public async Task GetMedications_ReturnsPrescriptions_WithRxNormCoding()
    {
        var bundle = await _fakeClient.GetMedicationsAsync("patient-1001");

        Assert.NotNull(bundle);
        Assert.True(bundle.Total >= 1);

        var med = Assert.Single(bundle.Resources);
        Assert.Equal("MedicationRequest", med.ResourceType);
        Assert.Equal("active", med.Status);
        Assert.Equal("order", med.Intent);
        Assert.Equal("17767", med.MedicationCodeableConcept?.Coding?[0].Code);
        Assert.Equal("Amlodipine 5mg Daily", med.MedicationCodeableConcept?.Text);
        Assert.NotNull(med.DosageInstruction);
        Assert.Contains("5mg", med.DosageInstruction[0].Text);
    }

    // =========================================================================
    // 7. APPOINTMENT RESOURCE INTEGRATION (READ & SCHEDULE)
    // =========================================================================

    [Fact]
    public async Task GetAppointments_ReturnsBookedAppointments()
    {
        var bundle = await _fakeClient.GetAppointmentsAsync("patient-1001");

        Assert.NotNull(bundle);
        Assert.True(bundle.Total >= 1);

        var appt = Assert.Single(bundle.Resources);
        Assert.Equal("Appointment", appt.ResourceType);
        Assert.Equal("booked", appt.Status);
        Assert.Equal("2026-11-10T09:00:00Z", appt.Start);
        Assert.Contains(appt.Participant!, p => p.Actor?.Reference == "Patient/patient-1001");
    }

    [Fact]
    public async Task CreateAppointment_PersistsAndReturnsNewAppointment()
    {
        var newAppt = new FhirAppointmentDto(
            ResourceType: "Appointment",
            Id: null,
            Status: "proposed",
            ServiceCategory: new List<FhirCodeableConceptDto>
            {
                new(new List<FhirCodingDto> { new("http://openmrs.org", "CARDIOLOGY", "Cardiology") }, "Cardiology")
            },
            Description: "New Cardiology Workup",
            Start: "2026-11-15T14:00:00Z",
            End: "2026-11-15T14:45:00Z",
            Participant: new List<FhirAppointmentParticipantDto>
            {
                new(new FhirReferenceDto("Patient/patient-1001", "Abebe Kebede"), "accepted", "required")
            },
            Comment: "Referred by Dr. Dawit"
        );

        var created = await _fakeClient.CreateAppointmentAsync(newAppt);

        Assert.NotNull(created);
        Assert.NotNull(created.Id);
        Assert.StartsWith("appt-", created.Id);
        Assert.Equal("proposed", created.Status);

        var updatedBundle = await _fakeClient.GetAppointmentsAsync("patient-1001");
        Assert.Equal(2, updatedBundle.Total);
    }

    // =========================================================================
    // 8. HTTP CLIENT CONTRACT & PROTOCOL TEST (WIRE FORMAT VALIDATION)
    // =========================================================================

    [Fact]
    public async Task ClinicalCoreFhirClient_SendsBasicAuthAndAcceptHeaders()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://clinical-core.internal/openmrs/ws/fhir2/R4/")
        };

        var options = Options.Create(new ClinicalCoreFhirOptions
        {
            BaseUrl = "https://clinical-core.internal/openmrs/ws/fhir2/R4/",
            Username = "portal-service-user",
            Password = "StrongPortalPassword123!",
            TimeoutSeconds = 10,
            UseFake = false
        });

        var client = new ClinicalCoreFhirClient(httpClient, options, NullLogger<ClinicalCoreFhirClient>.Instance);

        handler.MockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                    "resourceType": "CapabilityStatement",
                    "status": "active",
                    "date": "2026-10-24T00:00:00Z",
                    "fhirVersion": "4.0.1",
                    "software": { "name": "OpenMRS FHIR2 Platform", "version": "2.8.0" }
                }
                """,
                Encoding.UTF8,
                "application/fhir+json"
            )
        };

        var capability = await client.GetCapabilityStatementAsync();

        Assert.NotNull(capability);
        Assert.Equal("4.0.1", capability.FhirVersion);

        // Verify sent headers
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization?.Scheme);

        var credBytes = Convert.FromBase64String(handler.LastRequest.Headers.Authorization!.Parameter!);
        var credString = Encoding.UTF8.GetString(credBytes);
        Assert.Equal("portal-service-user:StrongPortalPassword123!", credString);

        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/fhir+json");
    }

    [Fact]
    public async Task ClinicalCoreFhirClient_Handles404Gracefully()
    {
        var handler = new TestHttpMessageHandler
        {
            MockResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://clinical-core.internal/openmrs/ws/fhir2/R4/")
        };

        var options = Options.Create(new ClinicalCoreFhirOptions
        {
            BaseUrl = "https://clinical-core.internal/openmrs/ws/fhir2/R4/",
            UseFake = false
        });

        var client = new ClinicalCoreFhirClient(httpClient, options, NullLogger<ClinicalCoreFhirClient>.Instance);

        var patient = await client.GetPatientByIdAsync("nonexistent-patient");
        Assert.Null(patient);

        var bundle = await client.GetEncountersAsync("nonexistent-patient");
        Assert.NotNull(bundle);
        Assert.Equal(0, bundle.Total);
    }

    // =========================================================================
    // TEST HTTP MESSAGE HANDLER
    // =========================================================================

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage MockResponse { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(MockResponse);
        }
    }
}
