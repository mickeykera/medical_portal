using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/profile")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class ProfileController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public ProfileController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;

        var person = await _db.Persons
            .Include(p => p.Patient)
            .FirstOrDefaultAsync(p => p.Id == actor.PersonId);

        if (person == null)
            return NotFound(new { error = "Profile not found." });

        return Ok(new
        {
            firstName = person.FirstName,
            lastName = person.LastName,
            dateOfBirth = ClinicalFormatting.ToIsoDate(person.DateOfBirth),
            age = ClinicalFormatting.AgeFrom(person.DateOfBirth),
            sexAtBirth = person.SexAtBirth,
            email = person.Email,
            phone = person.Phone,
            addressLine1 = person.AddressLine1,
            addressLine2 = person.AddressLine2,
            city = person.City,
            state = person.State,
            postalCode = person.PostalCode,
            preferredPharmacy = person.PreferredPharmacy,
            emergencyContactName = person.EmergencyContactName,
            emergencyContactPhone = person.EmergencyContactPhone,
            mrn = person.Patient?.Mrn
        });
    }

    public record UpdateProfileRequest(
        string? FirstName,
        string? LastName,
        string? Phone,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? State,
        string? PostalCode,
        string? PreferredPharmacy,
        string? EmergencyContactName,
        string? EmergencyContactPhone);

    [HttpPatch]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;

        var person = await _db.Persons.FirstOrDefaultAsync(p => p.Id == actor.PersonId);
        if (person == null)
            return NotFound(new { error = "Profile not found." });

        if (request.FirstName != null) person.FirstName = request.FirstName.Trim();
        if (request.LastName != null) person.LastName = request.LastName.Trim();
        if (request.Phone != null) person.Phone = request.Phone.Trim();
        if (request.AddressLine1 != null) person.AddressLine1 = request.AddressLine1.Trim();
        if (request.AddressLine2 != null) person.AddressLine2 = request.AddressLine2.Trim();
        if (request.City != null) person.City = request.City.Trim();
        if (request.State != null) person.State = request.State.Trim();
        if (request.PostalCode != null) person.PostalCode = request.PostalCode.Trim();
        if (request.PreferredPharmacy != null) person.PreferredPharmacy = request.PreferredPharmacy.Trim();
        if (request.EmergencyContactName != null) person.EmergencyContactName = request.EmergencyContactName.Trim();
        if (request.EmergencyContactPhone != null) person.EmergencyContactPhone = request.EmergencyContactPhone.Trim();
        person.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("RECORD_UPDATED", "Person", person.Id.ToString(), actor.UserAccountId);

        return Ok(new
        {
            profile = new
            {
                firstName = person.FirstName,
                lastName = person.LastName,
                phone = person.Phone,
                addressLine1 = person.AddressLine1,
                addressLine2 = person.AddressLine2,
                city = person.City,
                state = person.State,
                postalCode = person.PostalCode,
                preferredPharmacy = person.PreferredPharmacy,
                emergencyContactName = person.EmergencyContactName,
                emergencyContactPhone = person.EmergencyContactPhone
            }
        });
    }
}
