using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class OrganizationModelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizationModelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OrganizationModel_SeedData_Contains2FacilitiesAnd4Departments()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var org = await db.Organizations
            .Include(o => o.Facilities)
                .ThenInclude(f => f.Departments)
            .FirstOrDefaultAsync(o => o.Code == "SHS");

        Assert.NotNull(org);
        Assert.Equal("Springfield Health System", org.Name);
        Assert.Equal(2, org.Facilities.Count);

        var srmc = org.Facilities.FirstOrDefault(f => f.Code == "SRMC");
        Assert.NotNull(srmc);
        Assert.Equal("Springfield Regional Medical Center", srmc.Name);
        Assert.Equal(2, srmc.Departments.Count);
        Assert.Contains(srmc.Departments, d => d.Code == "FAM-MED" && d.Specialty == "Family Medicine");
        Assert.Contains(srmc.Departments, d => d.Code == "CARD-INT" && d.Specialty == "Cardiology");

        var rop = org.Facilities.FirstOrDefault(f => f.Code == "ROP");
        Assert.NotNull(rop);
        Assert.Equal("Riverside Outpatient Pavilion", rop.Name);
        Assert.Equal(2, rop.Departments.Count);
        Assert.Contains(rop.Departments, d => d.Code == "PEDS" && d.Specialty == "Pediatrics");
        Assert.Contains(rop.Departments, d => d.Code == "URG-CARE" && d.Specialty == "Urgent Care");

        var totalDepartments = await db.Departments.CountAsync();
        Assert.True(totalDepartments >= 4);
    }

    [Fact]
    public async Task Provider_Department_ManyToManyRelationship_WorksCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var drPatel = await db.Providers
            .Include(p => p.ProviderDepartments)
                .ThenInclude(pd => pd.Department)
            .FirstOrDefaultAsync(p => p.Email == "apatel@clinic.example");

        Assert.NotNull(drPatel);
        Assert.Equal(2, drPatel.ProviderDepartments.Count);
        Assert.Contains(drPatel.ProviderDepartments, pd => pd.Department.Code == "FAM-MED" && pd.IsPrimary);
        Assert.Contains(drPatel.ProviderDepartments, pd => pd.Department.Code == "CARD-INT" && !pd.IsPrimary);

        var drKim = await db.Providers
            .Include(p => p.ProviderDepartments)
                .ThenInclude(pd => pd.Department)
            .FirstOrDefaultAsync(p => p.Email == "jkim@clinic.example");

        Assert.NotNull(drKim);
        Assert.Equal(2, drKim.ProviderDepartments.Count);
        Assert.Contains(drKim.ProviderDepartments, pd => pd.Department.Code == "PEDS" && pd.IsPrimary);
        Assert.Contains(drKim.ProviderDepartments, pd => pd.Department.Code == "URG-CARE" && !pd.IsPrimary);
    }

    [Fact]
    public async Task Location_And_Appointment_LinkedToDepartment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        // Check Location linkages
        var clinic = await db.Locations
            .Include(l => l.Department)
            .FirstOrDefaultAsync(l => l.Name == "Riverside Family Clinic");

        Assert.NotNull(clinic);
        Assert.NotNull(clinic.Department);
        Assert.Equal("FAM-MED", clinic.Department.Code);

        var northBranch = await db.Locations
            .Include(l => l.Department)
            .FirstOrDefaultAsync(l => l.Name == "Northside Pediatrics");

        Assert.NotNull(northBranch);
        Assert.NotNull(northBranch.Department);
        Assert.Equal("PEDS", northBranch.Department.Code);

        // Check Appointment linkages
        var appointmentsWithDept = await db.Appointments
            .Include(a => a.Department)
            .Where(a => a.DepartmentId != null)
            .ToListAsync();

        Assert.NotEmpty(appointmentsWithDept);
        Assert.Contains(appointmentsWithDept, a => a.Department!.Code == "FAM-MED");
        Assert.Contains(appointmentsWithDept, a => a.Department!.Code == "PEDS");
    }

    [Fact]
    public async Task UserMembership_LinksUsersToFacilitiesAndDepartmentsWithRole()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var clinicianUser = await db.UserAccounts
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Facility)
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Department)
            .FirstOrDefaultAsync(u => u.Email == "apatel@clinic.example");

        Assert.NotNull(clinicianUser);
        Assert.NotEmpty(clinicianUser.Memberships);

        var primaryMembership = clinicianUser.Memberships
            .FirstOrDefault(m => m.Department?.Code == "FAM-MED");

        Assert.NotNull(primaryMembership);
        Assert.Equal("CLINICIAN", primaryMembership.Role);
        Assert.Equal("SRMC", primaryMembership.Facility?.Code);

        var visitingMembership = clinicianUser.Memberships
            .FirstOrDefault(m => m.Facility?.Code == "ROP" && m.DepartmentId == null);

        Assert.NotNull(visitingMembership);
        Assert.Equal("VISITING_CLINICIAN", visitingMembership.Role);

        // Patient user membership
        var patientUser = await db.UserAccounts
            .Include(u => u.Memberships)
                .ThenInclude(m => m.Facility)
            .FirstOrDefaultAsync(u => u.Email == "dana@example.com");

        Assert.NotNull(patientUser);
        Assert.Contains(patientUser.Memberships, m => m.Facility?.Code == "SRMC" && m.Role == "PATIENT");
    }

    [Fact]
    public async Task OrganizationModel_DynamicCreation_PersistsCascadesCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var newOrg = new Organization
        {
            Name = "Metro Health Partners",
            Code = $"MHP-{Guid.NewGuid():N}"[..10]
        };
        db.Organizations.Add(newOrg);

        var facility = new Facility
        {
            Organization = newOrg,
            Name = "Metro Downtown Clinic",
            Code = "MDC"
        };
        db.Facilities.Add(facility);

        var department = new Department
        {
            Facility = facility,
            Name = "Oncology & Infusion Center",
            Code = "ONC",
            Specialty = "Oncology"
        };
        db.Departments.Add(department);

        var provider = new Provider
        {
            FirstName = "Sarah",
            LastName = "Chen",
            Credentials = "MD",
            Specialty = "Oncology",
            Npi = "9988776655",
            Email = $"schen-{Guid.NewGuid():N}@example.com"
        };
        db.Providers.Add(provider);

        var providerDept = new ProviderDepartment
        {
            Provider = provider,
            Department = department,
            IsPrimary = true
        };
        db.ProviderDepartments.Add(providerDept);

        await db.SaveChangesAsync();

        var queriedDept = await db.Departments
            .Include(d => d.Facility)
                .ThenInclude(f => f.Organization)
            .Include(d => d.ProviderDepartments)
                .ThenInclude(pd => pd.Provider)
            .FirstOrDefaultAsync(d => d.Id == department.Id);

        Assert.NotNull(queriedDept);
        Assert.Equal("Metro Downtown Clinic", queriedDept.Facility.Name);
        Assert.Equal(newOrg.Name, queriedDept.Facility.Organization.Name);
        Assert.Single(queriedDept.ProviderDepartments);
        Assert.Equal("Dr. Sarah Chen", $"Dr. {queriedDept.ProviderDepartments.First().Provider.FirstName} {queriedDept.ProviderDepartments.First().Provider.LastName}");
    }
}
