using System.Diagnostics;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalPortal.Web.Controllers;

[ApiController]
public class SystemUptimeController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    [HttpGet("api/system/uptime")]
    public IActionResult Uptime()
    {
        var uptimeSeconds = (DateTime.UtcNow - StartTime).TotalSeconds;
        return Ok(new { status = "ok", uptime = uptimeSeconds });
    }
}

[ApiController]
[Route("R4")]
[RequireAuth]
public class FhirController : ControllerBase
{
    private readonly IFhirService _fhir;
    private readonly IAccessControlService _accessControl;

    public FhirController(IFhirService fhir, IAccessControlService accessControl)
    {
        _fhir = fhir;
        _accessControl = accessControl;
    }

    /*
     * ARCHITECTURE NOTE: SMART on FHIR Integration Point
     * ----------------------------------------------------
     * To plug in SMART on FHIR authorization:
     * 1. Discovery: Expose the SMART configuration manifest at /.well-known/smart-configuration
     *    and /R4/.well-known/smart-configuration defining token_endpoint, authorization_endpoint,
     *    capabilities, and code_challenge_methods_supported.
     * 2. Token Validation & Scopes: Add a SMART JWT bearer handler / filter (or middleware) that validates:
     *    - Clinical scopes (e.g. 'patient/Patient.read', 'patient/Observation.read', 'user/*.read', 'system/*.read').
     *    - The 'launch/patient' context claim asserting the authorized Patient ID.
     * 3. Access Decision: Plug SMART scope validation into ValidatePatientAccessAsync() or a dedicated
     *    action filter, verifying both patient binding and required resource scopes before returning data.
     */

    [HttpGet("metadata")]
    [AllowAnonymous]
    public IActionResult GetMetadata()
    {
        return Ok(_fhir.GetCapabilityStatement());
    }

    private async Task<IActionResult?> ValidatePatientAccessAsync(Guid patientId)
    {
        if (HttpContext.Items["Actor"] is not AccessTokenClaims actor)
        {
            return Unauthorized(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "login",
                        diagnostics = "Authentication required."
                    }
                }
            });
        }

        var decision = await _accessControl.ResolveAccessAsync(actor, patientId, Permission.viewRecords);
        if (!decision.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "forbidden",
                        diagnostics = "You do not have permission to view records for this patient.",
                        details = decision.Reason
                    }
                }
            });
        }

        HttpContext.Items["TargetPatientId"] = patientId;
        HttpContext.Items["IsProxy"] = decision.IsProxy;
        if (decision.IsProxy && decision.PatientPersonId.HasValue)
        {
            HttpContext.Items["ActingForPersonId"] = decision.PatientPersonId.Value;
        }

        return null;
    }

    [HttpGet("Patient/{id}")]
    public async Task<IActionResult> GetPatient(Guid id)
    {
        var accessCheck = await ValidatePatientAccessAsync(id);
        if (accessCheck != null) return accessCheck;

        var patient = await _fhir.GetPatientFhirAsync(id);
        if (patient == null) return NotFound(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "not-found", diagnostics = "Patient not found" } } });
        return Ok(patient);
    }

    [HttpGet("Patient/{id}/$everything")]
    public async Task<IActionResult> GetPatientEverything(Guid id)
    {
        var accessCheck = await ValidatePatientAccessAsync(id);
        if (accessCheck != null) return accessCheck;

        var bundle = await _fhir.GetPatientEverythingBundleAsync(id);
        if (bundle == null) return NotFound(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "not-found", diagnostics = "Patient not found" } } });
        return Ok(bundle);
    }

    [HttpGet("Observation")]
    public async Task<IActionResult> GetObservations([FromQuery] Guid? patient)
    {
        if (!patient.HasValue) return BadRequest(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "required", diagnostics = "patient query parameter is required" } } });

        var accessCheck = await ValidatePatientAccessAsync(patient.Value);
        if (accessCheck != null) return accessCheck;

        return Ok(await _fhir.GetPatientObservationsAsync(patient.Value));
    }

    [HttpGet("Condition")]
    public async Task<IActionResult> GetConditions([FromQuery] Guid? patient)
    {
        if (!patient.HasValue) return BadRequest(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "required", diagnostics = "patient query parameter is required" } } });

        var accessCheck = await ValidatePatientAccessAsync(patient.Value);
        if (accessCheck != null) return accessCheck;

        return Ok(await _fhir.GetPatientConditionsAsync(patient.Value));
    }

    [HttpGet("AllergyIntolerance")]
    public async Task<IActionResult> GetAllergies([FromQuery] Guid? patient)
    {
        if (!patient.HasValue) return BadRequest(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "required", diagnostics = "patient query parameter is required" } } });

        var accessCheck = await ValidatePatientAccessAsync(patient.Value);
        if (accessCheck != null) return accessCheck;

        return Ok(await _fhir.GetPatientAllergiesAsync(patient.Value));
    }

    [HttpGet("MedicationRequest")]
    public async Task<IActionResult> GetMedications([FromQuery] Guid? patient)
    {
        if (!patient.HasValue) return BadRequest(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "required", diagnostics = "patient query parameter is required" } } });

        var accessCheck = await ValidatePatientAccessAsync(patient.Value);
        if (accessCheck != null) return accessCheck;

        return Ok(await _fhir.GetPatientMedicationsAsync(patient.Value));
    }

    [HttpGet("Appointment")]
    public async Task<IActionResult> GetAppointments([FromQuery] Guid? patient)
    {
        if (!patient.HasValue) return BadRequest(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "required", diagnostics = "patient query parameter is required" } } });

        var accessCheck = await ValidatePatientAccessAsync(patient.Value);
        if (accessCheck != null) return accessCheck;

        return Ok(await _fhir.GetPatientAppointmentsAsync(patient.Value));
    }
}
