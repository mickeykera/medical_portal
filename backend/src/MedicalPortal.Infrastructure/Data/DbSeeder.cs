#pragma warning disable CS0618 // Type or member is obsolete

using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Data;

public static class DbSeeder
{
    public const string DemoPassword = "ChangeMe!Demo2024";
    public const string DevClinicianTotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    public static async Task SeedAsync(MedicalPortalDbContext db, IPasswordHasher hasher, bool isProduction = false)
    {
        if (isProduction)
        {
            throw new InvalidOperationException("Production startup failed: Seeding is strictly prohibited in production.");
        }
        if (!await db.Tenants.AnyAsync())
        {
            var defaultTenant = new Tenant
            {
                Id = TenantConstants.DefaultTenantId,
                Name = TenantConstants.DefaultTenantName,
                Slug = TenantConstants.DefaultTenantSlug,
                Region = "Ethiopia, Addis Ababa",
                TimeZone = "Africa/Addis_Ababa",
                LanguagesJson = "[\"en\"]",
                IsActive = true,
                MasterKeyFingerprint = "fp_addis_default",
                AuditChainHash = "0000000000000000000000000000000000000000000000000000000000000000"
            };
            defaultTenant.SetSettings(new TenantSettings
            {
                Branding = new TenantBrandingSettings { PrimaryColor = "#0284c7" },
                Modules = new TenantModuleSettings(),
                Hosting = new TenantHostingSettings { Isolation = "Shared database" },
                Integrations = new TenantIntegrationSettings { Ehr = "HMS" },
                Security = new TenantSecuritySettings(),
                Currency = new TenantCurrencySettings
                {
                    Code = "ETB",
                    Symbol = "Br",
                    MinorUnitDigits = 2,
                    Locale = "en-ET",
                    MinPaymentPlanAmountMinorUnits = 50000
                },
                Location = new TenantLocationSettings
                {
                    Country = "Ethiopia",
                    City = "Addis Ababa",
                    TimeZone = "Africa/Addis_Ababa",
                    Latitude = 9.0054,
                    Longitude = 38.7636
                }
            });
            db.Tenants.Add(defaultTenant);
            await db.SaveChangesAsync();
        }

        if (!await db.CalendarDimensions.AnyAsync())
        {
            await SeedCalendarDimensionAsync(db);
        }

        if (await db.Persons.AnyAsync())
        {
            var accountsToFix = await db.UserAccounts.IgnoreQueryFilters().Where(u => u.TenantId == Guid.Empty).ToListAsync();
            if (accountsToFix.Count > 0)
            {
                foreach (var a in accountsToFix) a.TenantId = TenantConstants.DefaultTenantId;
                await db.SaveChangesAsync();
            }

            var devPatel = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == "apatel@clinic.example");
            if (devPatel != null && (!devPatel.MfaEnabled || devPatel.MfaSecret != DevClinicianTotpSecret))
            {
                devPatel.MfaEnabled = true;
                devPatel.MfaSecret = DevClinicianTotpSecret;
                devPatel.MfaEnrolledAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            var defTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == TenantConstants.DefaultTenantId);
            if (defTenant != null)
            {
                defTenant.Name = TenantConstants.DefaultTenantName;
                defTenant.Region = "Ethiopia, Addis Ababa";
                defTenant.TimeZone = "Africa/Addis_Ababa";
                var ts = defTenant.GetParsedSettings();
                ts.Location = new TenantLocationSettings
                {
                    Country = "Ethiopia",
                    City = "Addis Ababa",
                    TimeZone = "Africa/Addis_Ababa",
                    Latitude = 9.0054,
                    Longitude = 38.7636
                };
                defTenant.SetSettings(ts);
            }

            var oldOrg = await db.Organizations.FirstOrDefaultAsync(o => o.Code == "SHS");
            if (oldOrg != null && oldOrg.Name == "Springfield Health System")
            {
                oldOrg.Name = "Addis Care Health Network (DEMO)";
                oldOrg.TaxId = "ET-1234567";
                oldOrg.Website = "https://addiscare.example.org";
                oldOrg.Phone = "+251 11 661 0199";
            }
            var srmc = await db.Facilities.FirstOrDefaultAsync(f => f.Code == "SRMC");
            if (srmc != null && srmc.Name == "Springfield Regional Medical Center")
            {
                srmc.Name = "Addis Central General Hospital (DEMO)";
                srmc.AddressLine1 = "Bole Road, Olympia Area";
                srmc.City = "Addis Ababa";
                srmc.State = "Addis Ababa";
                srmc.PostalCode = "1000";
                srmc.Phone = "+251 11 661 0200";
            }
            var rop = await db.Facilities.FirstOrDefaultAsync(f => f.Code == "ROP");
            if (rop != null && rop.Name == "Riverside Outpatient Pavilion")
            {
                rop.Name = "Bole Outpatient Pavilion (DEMO)";
                rop.AddressLine1 = "Airport Road, Bole";
                rop.City = "Addis Ababa";
                rop.State = "Addis Ababa";
                rop.PostalCode = "1000";
                rop.Phone = "+251 11 661 0100";
            }
            var clinicLoc = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Riverside Family Clinic");
            if (clinicLoc != null)
            {
                clinicLoc.Name = "Bole Family Health Clinic (DEMO)";
                clinicLoc.AddressLine1 = "Bole Subcity, Woreda 03, House 400";
                clinicLoc.City = "Addis Ababa";
                clinicLoc.State = "Addis Ababa";
                clinicLoc.PostalCode = "1000";
                clinicLoc.Phone = "+251 11 661 0100";
                clinicLoc.Latitude = 8.9950;
                clinicLoc.Longitude = 38.7850;
                clinicLoc.TimeZoneId = "Africa/Addis_Ababa";
            }
            var northLoc = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Northside Pediatrics");
            if (northLoc != null)
            {
                northLoc.Name = "Yeka Pediatric Clinic (DEMO)";
                northLoc.AddressLine1 = "Yeka Subcity, Megenagna Area";
                northLoc.City = "Addis Ababa";
                northLoc.State = "Addis Ababa";
                northLoc.PostalCode = "1000";
                northLoc.Phone = "+251 11 661 0110";
                northLoc.Latitude = 9.0300;
                northLoc.Longitude = 38.8000;
                northLoc.TimeZoneId = "Africa/Addis_Ababa";
            }

            var dana = await db.Persons.FirstOrDefaultAsync(p => p.Email == "dana@example.com");
            if (dana != null && (dana.City == "Springfield" || dana.City != "Addis Ababa"))
            {
                dana.AddressLine1 = "Bole Subcity, Woreda 04, House 88";
                dana.City = "Addis Ababa";
                dana.State = "Addis Ababa";
                dana.PostalCode = "1000";
                dana.Phone = "+251 91 155 0123";
                dana.PreferredPharmacy = "Bole Medhanealem Pharmacy (DEMO), Bole Road";
                dana.EmergencyContactPhone = "+251 91 155 0199";
            }

            var oldHospital = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Springfield Community Hospital");
            if (oldHospital != null)
            {
                oldHospital.Name = "Kirkos Community Hospital (DEMO)";
                oldHospital.AddressLine1 = "Kirkos Subcity, Meskel Flower Area";
                oldHospital.City = "Addis Ababa";
                oldHospital.State = "Addis Ababa";
                oldHospital.PostalCode = "1000";
                oldHospital.Phone = "+251 11 661 0200";
                oldHospital.Website = "https://example.org/kirkos-hospital";
                oldHospital.Latitude = 9.0110;
                oldHospital.Longitude = 38.7620;
                oldHospital.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldUrgent = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Memorial Urgent Care");
            if (oldUrgent != null)
            {
                oldUrgent.Name = "Arada Urgent Care Center (DEMO)";
                oldUrgent.AddressLine1 = "Arada Subcity, Piazza Area";
                oldUrgent.City = "Addis Ababa";
                oldUrgent.State = "Addis Ababa";
                oldUrgent.PostalCode = "1000";
                oldUrgent.Phone = "+251 11 661 0210";
                oldUrgent.Latitude = 9.0380;
                oldUrgent.Longitude = 38.7520;
                oldUrgent.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldPharm1 = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Riverside Pharmacy");
            if (oldPharm1 != null)
            {
                oldPharm1.Name = "Bole Medhanealem Pharmacy (DEMO)";
                oldPharm1.AddressLine1 = "Bole Subcity, Medhanealem Mall";
                oldPharm1.City = "Addis Ababa";
                oldPharm1.State = "Addis Ababa";
                oldPharm1.PostalCode = "1000";
                oldPharm1.Phone = "+251 11 661 0220";
                oldPharm1.Latitude = 8.9970;
                oldPharm1.Longitude = 38.7880;
                oldPharm1.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldPharm2 = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Oak Street Drugs");
            if (oldPharm2 != null)
            {
                oldPharm2.Name = "Lideta Community Pharmacy (DEMO)";
                oldPharm2.AddressLine1 = "Lideta Subcity, Balcha Area";
                oldPharm2.City = "Addis Ababa";
                oldPharm2.State = "Addis Ababa";
                oldPharm2.PostalCode = "1000";
                oldPharm2.Phone = "+251 11 661 0221";
                oldPharm2.Latitude = 9.0120;
                oldPharm2.Longitude = 38.7380;
                oldPharm2.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldPharm3 = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Northside Pharmacy");
            if (oldPharm3 != null)
            {
                oldPharm3.Name = "Yeka Hill Pharmacy (DEMO)";
                oldPharm3.AddressLine1 = "Yeka Subcity, Signal Area";
                oldPharm3.City = "Addis Ababa";
                oldPharm3.State = "Addis Ababa";
                oldPharm3.PostalCode = "1000";
                oldPharm3.Phone = "+251 11 661 0222";
                oldPharm3.Latitude = 9.0280;
                oldPharm3.Longitude = 38.7950;
                oldPharm3.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldLab = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Sangamon Valley Diagnostics");
            if (oldLab != null)
            {
                oldLab.Name = "Nifas Silk Diagnostic Laboratory (DEMO)";
                oldLab.AddressLine1 = "Nifas Silk-Lafto Subcity, Gotera Area";
                oldLab.City = "Addis Ababa";
                oldLab.State = "Addis Ababa";
                oldLab.PostalCode = "1000";
                oldLab.Phone = "+251 11 661 0230";
                oldLab.Latitude = 8.9700;
                oldLab.Longitude = 38.7400;
                oldLab.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldRehab = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Springfield Physical Therapy");
            if (oldRehab != null)
            {
                oldRehab.Name = "Kolfe Rehabilitation Center (DEMO)";
                oldRehab.AddressLine1 = "Kolfe Keranio Subcity, Total Area";
                oldRehab.City = "Addis Ababa";
                oldRehab.State = "Addis Ababa";
                oldRehab.PostalCode = "1000";
                oldRehab.Phone = "+251 11 661 0240";
                oldRehab.Latitude = 9.0200;
                oldRehab.Longitude = 38.7100;
                oldRehab.TimeZoneId = "Africa/Addis_Ababa";
            }
            var oldPharm4 = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Westside Pharmacy");
            if (oldPharm4 != null)
            {
                oldPharm4.Name = "Gullele Health Pharmacy (DEMO)";
                oldPharm4.AddressLine1 = "Gullele Subcity, Semien Mazegaja Area";
                oldPharm4.City = "Addis Ababa";
                oldPharm4.State = "Addis Ababa";
                oldPharm4.PostalCode = "1000";
                oldPharm4.Phone = "+251 11 661 0223";
                oldPharm4.Latitude = 9.0600;
                oldPharm4.Longitude = 38.7300;
                oldPharm4.TimeZoneId = "Africa/Addis_Ababa";
            }

            var chicagoLocs = await db.Locations.Where(l => l.TimeZoneId == "America/Chicago").ToListAsync();
            foreach (var cl in chicagoLocs) cl.TimeZoneId = "Africa/Addis_Ababa";

            await db.SaveChangesAsync();

            if (!await db.Organizations.AnyAsync())
            {
                await SeedOrganizationsForExistingDbAsync(db);
            }
            if (!await db.AppointmentTypes.AnyAsync())
            {
                await SeedSchedulingForExistingDbAsync(db);
            }
            return; // Already seeded
        }

        var passwordHash = hasher.HashPassword(DemoPassword);

        // --- Providers -----------------------------------------------------
        var drPatel = new Provider
        {
            FirstName = "Amara",
            LastName = "Patel",
            Credentials = "MD",
            Specialty = "Family Medicine",
            Npi = "1000000001",
            Email = "apatel@clinic.example"
        };
        db.Providers.Add(drPatel);

        var drKim = new Provider
        {
            FirstName = "Joon",
            LastName = "Kim",
            Credentials = "DO",
            Specialty = "Pediatrics",
            Npi = "1000000002",
            Email = "jkim@clinic.example"
        };
        db.Providers.Add(drKim);

        // Practitioner login
        var patelPerson = new Person
        {
            FirstName = "Amara",
            LastName = "Patel",
            DateOfBirth = new DateTime(1980, 4, 12, 0, 0, 0, DateTimeKind.Utc),
            Email = "apatel@clinic.example"
        };
        db.Persons.Add(patelPerson);

        var patelAccount = new UserAccount
        {
            Person = patelPerson,
            Email = "apatel@clinic.example",
            PasswordHash = passwordHash,
            Role = UserRole.CLINICIAN,
            MfaEnabled = true,
            MfaSecret = DevClinicianTotpSecret,
            MfaEnrolledAt = DateTime.UtcNow
        };
        db.UserAccounts.Add(patelAccount);
        drPatel.Account = patelAccount;

        // --- Organizations, Facilities & Departments ------------------------
        var org = new Organization
        {
            Name = "Addis Care Health Network (DEMO)",
            Code = "SHS",
            TaxId = "ET-1234567",
            Website = "https://addiscare.example.org",
            Phone = "+251 11 661 0199"
        };
        db.Organizations.Add(org);

        var facilitySrmc = new Facility
        {
            Organization = org,
            Name = "Addis Central General Hospital (DEMO)",
            Code = "SRMC",
            AddressLine1 = "Bole Road, Olympia Area",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0200"
        };
        db.Facilities.Add(facilitySrmc);

        var facilityRop = new Facility
        {
            Organization = org,
            Name = "Bole Outpatient Pavilion (DEMO)",
            Code = "ROP",
            AddressLine1 = "Airport Road, Bole",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0100"
        };
        db.Facilities.Add(facilityRop);

        var deptFamMed = new Department
        {
            Facility = facilitySrmc,
            Name = "Family Medicine & Primary Care",
            Code = "FAM-MED",
            Specialty = "Family Medicine",
            Phone = "+251 11 661 0201"
        };
        db.Departments.Add(deptFamMed);

        var deptCardiology = new Department
        {
            Facility = facilitySrmc,
            Name = "Cardiology & Internal Medicine",
            Code = "CARD-INT",
            Specialty = "Cardiology",
            Phone = "+251 11 661 0202"
        };
        db.Departments.Add(deptCardiology);

        var deptPeds = new Department
        {
            Facility = facilityRop,
            Name = "Pediatrics & Adolescent Medicine",
            Code = "PEDS",
            Specialty = "Pediatrics",
            Phone = "+251 11 661 0111"
        };
        db.Departments.Add(deptPeds);

        var deptUrgentCare = new Department
        {
            Facility = facilityRop,
            Name = "Urgent Care & Occupational Health",
            Code = "URG-CARE",
            Specialty = "Urgent Care",
            Phone = "+251 11 661 0112"
        };
        db.Departments.Add(deptUrgentCare);

        // Provider to Departments (many-to-many)
        db.ProviderDepartments.AddRange(new[]
        {
            new ProviderDepartment { Provider = drPatel, Department = deptFamMed, IsPrimary = true },
            new ProviderDepartment { Provider = drPatel, Department = deptCardiology, IsPrimary = false },
            new ProviderDepartment { Provider = drKim, Department = deptPeds, IsPrimary = true },
            new ProviderDepartment { Provider = drKim, Department = deptUrgentCare, IsPrimary = false }
        });

        // --- Locations -----------------------------------------------------
        var clinic = new Location
        {
            Department = deptFamMed,
            Name = "Bole Family Health Clinic (DEMO)",
            AddressLine1 = "Bole Subcity, Woreda 03, House 400",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0100",
            Latitude = 8.9950,
            Longitude = 38.7850,
            TimeZoneId = "Africa/Addis_Ababa",
            Type = LocationType.CLINIC
        };
        db.Locations.Add(clinic);

        var northBranch = new Location
        {
            Department = deptPeds,
            Name = "Yeka Pediatric Clinic (DEMO)",
            AddressLine1 = "Yeka Subcity, Megenagna Area",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0110",
            Latitude = 9.0300,
            Longitude = 38.8000,
            TimeZoneId = "Africa/Addis_Ababa",
            Type = LocationType.CLINIC
        };
        db.Locations.Add(northBranch);

        db.ProviderLocations.Add(new ProviderLocation { Provider = drPatel, Location = clinic });
        db.ProviderLocations.Add(new ProviderLocation { Provider = drKim, Location = northBranch });
        db.ProviderLocations.Add(new ProviderLocation { Provider = drKim, Location = clinic });

        // --- Appointment Types ---------------------------------------------
        var apptTypeRoutine = new AppointmentType { Name = "Routine Follow-up", Code = "ROUTINE", DurationMinutes = 30, Description = "Standard routine follow-up consultation." };
        var apptTypeWellness = new AppointmentType { Name = "Annual Wellness Exam", Code = "WELLNESS", DurationMinutes = 45, Description = "Comprehensive annual preventative health exam." };
        var apptTypeNewPatient = new AppointmentType { Name = "New Patient Consultation", Code = "NEW_PATIENT", DurationMinutes = 60, Description = "Initial consultation and complete medical history intake." };
        var apptTypeUrgent = new AppointmentType { Name = "Urgent Care Visit", Code = "URGENT", DurationMinutes = 15, Description = "Acute illness or quick symptom evaluation." };
        db.AppointmentTypes.AddRange(apptTypeRoutine, apptTypeWellness, apptTypeNewPatient, apptTypeUrgent);

        // --- Rooms / Resources ----------------------------------------------
        var clinicRoom1 = new RoomResource { Location = clinic, Name = "Exam Room 1", Code = "EXAM-1" };
        var clinicRoom2 = new RoomResource { Location = clinic, Name = "Exam Room 2", Code = "EXAM-2" };
        var clinicProc = new RoomResource { Location = clinic, Name = "Procedure Suite", Code = "PROC-1" };
        var pedsRoom1 = new RoomResource { Location = northBranch, Name = "Peds Exam Room 1", Code = "PEDS-1" };
        var pedsRoom2 = new RoomResource { Location = northBranch, Name = "Peds Exam Room 2", Code = "PEDS-2" };
        db.RoomResources.AddRange(clinicRoom1, clinicRoom2, clinicProc, pedsRoom1, pedsRoom2);

        // --- Weekly Schedule Templates --------------------------------------
        // Dr. Patel at Riverside Clinic (Mon-Fri 08:30 - 16:30)
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
            {
                Provider = drPatel,
                Location = clinic,
                DayOfWeek = day,
                StartTime = new TimeSpan(8, 30, 0),
                EndTime = new TimeSpan(16, 30, 0)
            });
        }
        // Dr. Kim at Northside Pediatrics (Mon-Fri 09:00 - 17:00)
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
            {
                Provider = drKim,
                Location = northBranch,
                DayOfWeek = day,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(17, 0, 0)
            });
        }
        // Riverside Clinic location default schedule (Mon-Fri 08:00 - 17:00)
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
            {
                Location = clinic,
                DayOfWeek = day,
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(17, 0, 0)
            });
        }

        // --- Schedule Block (Holiday/Maintenance) ---------------------------
        var holidayDate = DateTime.UtcNow.Date.AddDays(30);
        db.ScheduleBlocks.Add(new ScheduleBlock
        {
            Location = clinic,
            StartAt = holidayDate.AddHours(8),
            EndAt = holidayDate.AddHours(17),
            Reason = "Facility Maintenance & Staff Training",
            IsHoliday = true
        });

        // Nearby facilities
        db.Locations.AddRange(new[]
        {
            new Location
            {
                Name = "Kirkos Community Hospital (DEMO)",
                Type = LocationType.HOSPITAL,
                AddressLine1 = "Kirkos Subcity, Meskel Flower Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0200",
                Website = "https://example.org/kirkos-hospital",
                Hours = "Emergency department open 24 hours",
                Latitude = 9.0110,
                Longitude = 38.7620,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Arada Urgent Care Center (DEMO)",
                Type = LocationType.URGENT_CARE,
                AddressLine1 = "Arada Subcity, Piazza Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0210",
                Hours = "Mon-Fri 8am-8pm, Sat-Sun 9am-5pm",
                Latitude = 9.0380,
                Longitude = 38.7520,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Bole Medhanealem Pharmacy (DEMO)",
                Type = LocationType.PHARMACY,
                AddressLine1 = "Bole Subcity, Medhanealem Mall",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0220",
                Hours = "Mon-Sat 9am-8pm, Sun 10am-6pm",
                Latitude = 8.9970,
                Longitude = 38.7880,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Lideta Community Pharmacy (DEMO)",
                Type = LocationType.PHARMACY,
                AddressLine1 = "Lideta Subcity, Balcha Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0221",
                Hours = "Daily 8am-10pm",
                Latitude = 9.0120,
                Longitude = 38.7380,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Yeka Hill Pharmacy (DEMO)",
                Type = LocationType.PHARMACY,
                AddressLine1 = "Yeka Subcity, Signal Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0222",
                Hours = "Mon-Fri 9am-7pm",
                Latitude = 9.0280,
                Longitude = 38.7950,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Nifas Silk Diagnostic Laboratory (DEMO)",
                Type = LocationType.LAB,
                AddressLine1 = "Nifas Silk-Lafto Subcity, Gotera Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0230",
                Hours = "Mon-Fri 7am-5pm",
                Latitude = 8.9700,
                Longitude = 38.7400,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Kolfe Rehabilitation Center (DEMO)",
                Type = LocationType.REHAB,
                AddressLine1 = "Kolfe Keranio Subcity, Total Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0240",
                Hours = "Mon-Fri 7am-6pm",
                Latitude = 9.0200,
                Longitude = 38.7100,
                TimeZoneId = "Africa/Addis_Ababa"
            },
            new Location
            {
                Name = "Gullele Health Pharmacy (DEMO)",
                Type = LocationType.PHARMACY,
                AddressLine1 = "Gullele Subcity, Semien Mazegaja Area",
                City = "Addis Ababa",
                State = "Addis Ababa",
                PostalCode = "1000",
                Phone = "+251 11 661 0223",
                Hours = "Mon-Sat 9am-8pm",
                Latitude = 9.0600,
                Longitude = 38.7300,
                TimeZoneId = "Africa/Addis_Ababa"
            }
        });

        // --- Family --------------------------------------------------------
        var parentPerson = new Person
        {
            FirstName = "Dana",
            LastName = "Rivera",
            DateOfBirth = new DateTime(1986, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "F",
            Email = "dana@example.com",
            Phone = "+251 91 155 0123",
            AddressLine1 = "Bole Subcity, Woreda 04, House 88",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            PreferredPharmacy = "Bole Medhanealem Pharmacy (DEMO), Bole Road",
            EmergencyContactName = "Luis Rivera",
            EmergencyContactPhone = "+251 91 155 0199"
        };
        db.Persons.Add(parentPerson);

        var parentAccount = new UserAccount
        {
            Person = parentPerson,
            Email = "dana@example.com",
            PasswordHash = passwordHash,
            Role = UserRole.PATIENT
        };
        db.UserAccounts.Add(parentAccount);

        // User memberships to facilities & departments with roles
        db.UserMemberships.AddRange(new[]
        {
            new UserMembership
            {
                UserAccount = patelAccount,
                Facility = facilitySrmc,
                Department = deptFamMed,
                Role = "CLINICIAN"
            },
            new UserMembership
            {
                UserAccount = patelAccount,
                Facility = facilityRop,
                Role = "VISITING_CLINICIAN"
            },
            new UserMembership
            {
                UserAccount = parentAccount,
                Facility = facilitySrmc,
                Role = "PATIENT"
            }
        });

        var parentPatient = new Patient
        {
            Person = parentPerson,
            Mrn = "MRN-1000001",
            PrimaryProvider = drPatel
        };
        db.Patients.Add(parentPatient);

        var childA = new Person
        {
            FirstName = "Mateo",
            LastName = "Rivera",
            DateOfBirth = new DateTime(2015, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "M"
        };
        db.Persons.Add(childA);

        var patientA = new Patient
        {
            Person = childA,
            Mrn = "MRN-1000002",
            PrimaryProvider = drPatel
        };
        db.Patients.Add(patientA);

        var childB = new Person
        {
            FirstName = "Sofia",
            LastName = "Rivera",
            DateOfBirth = new DateTime(2018, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "F"
        };
        db.Persons.Add(childB);

        var patientB = new Patient
        {
            Person = childB,
            Mrn = "MRN-1000003",
            PrimaryProvider = drPatel
        };
        db.Patients.Add(patientB);

        // Proxy grants
        db.ProxyGrants.Add(new ProxyGrant
        {
            Grantor = childA,
            Grantee = parentPerson,
            CanViewRecords = true,
            CanMessage = true,
            CanManageAppointments = true,
            CanRequestRefills = true,
            CanPayBills = false, // scoped out
            CanManageSharing = true,
            ConsentGivenAt = DateTime.UtcNow
        });

        db.ProxyGrants.Add(new ProxyGrant
        {
            Grantor = childB,
            Grantee = parentPerson,
            CanViewRecords = true,
            CanMessage = true,
            CanManageAppointments = true,
            CanRequestRefills = true,
            CanPayBills = true,
            CanManageSharing = true,
            ConsentGivenAt = DateTime.UtcNow
        });

        // Care teams
        foreach (var p in new[] { parentPatient, patientA, patientB })
        {
            db.CareTeams.Add(new PatientCareTeam { Patient = p, Provider = drPatel, IsPrimary = true, Role = "Primary care" });
        }
        foreach (var p in new[] { patientA, patientB })
        {
            db.CareTeams.Add(new PatientCareTeam { Patient = p, Provider = drKim, IsPrimary = false, Role = "Pediatrics" });
        }

        // Allergies
        var daysAgo = (int n) => DateTime.UtcNow.AddDays(-n);

        db.Allergies.AddRange(new[]
        {
            new Allergy
            {
                Patient = parentPatient,
                Substance = "Penicillin",
                DrugClass = "Beta-lactam (penicillin)",
                DrugClassCode = "J01CA",
                Reaction = "Anaphylaxis with cardiovascular collapse, 2019",
                Severity = AllergySeverity.ANAPHYLAXIS,
                RecordedAt = daysAgo(2400)
            },
            new Allergy
            {
                Patient = patientA,
                Substance = "Sulfonamides",
                DrugClass = "Sulfonamide",
                DrugClassCode = "SULFONAMIDE",
                Reaction = "Widespread urticaria",
                Severity = AllergySeverity.SEVERE,
                RecordedAt = daysAgo(900)
            },
            new Allergy
            {
                Patient = patientB,
                Substance = "Iodinated contrast",
                DrugClass = "Iodinated contrast",
                DrugClassCode = "IODINATED_CONTRAST",
                Reaction = "Facial swelling after a CT scan",
                Severity = AllergySeverity.SEVERE,
                RecordedAt = daysAgo(500)
            }
        });

        // Vitals
        db.Observations.AddRange(new[]
        {
            new Observation { Patient = parentPatient, Category = ObservationCategory.VITAL, Code = "85354-9", DisplayName = "Blood pressure", ValueText = "104/66", Unit = "mmHg", RefRangeLow = 110, RefRangeHigh = 139, RefRangeText = "110-139 mmHg", IsAbnormal = true, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = parentPatient, Category = ObservationCategory.VITAL, Code = "8867-4", DisplayName = "Heart rate", ValueText = "104", Unit = "bpm", RefRangeLow = 60, RefRangeHigh = 99, RefRangeText = "60-99 bpm", IsAbnormal = true, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = parentPatient, Category = ObservationCategory.VITAL, Code = "2708-6", DisplayName = "SpO2", ValueText = "94", Unit = "%", RefRangeLow = 95, RefRangeHigh = 100, RefRangeText = "95-100 %", IsAbnormal = true, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = parentPatient, Category = ObservationCategory.VITAL, Code = "8310-5", DisplayName = "Temperature", ValueText = "37.1", Unit = "°C", RefRangeLow = 36.1m, RefRangeHigh = 37.2m, RefRangeText = "36.1-37.2 °C", IsAbnormal = false, ObservedAt = DateTime.UtcNow },

            new Observation { Patient = patientA, Category = ObservationCategory.VITAL, Code = "85354-9", DisplayName = "Blood pressure", ValueText = "168/98", Unit = "mmHg", RefRangeLow = 110, RefRangeHigh = 139, RefRangeText = "110-139 mmHg", IsAbnormal = true, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientA, Category = ObservationCategory.VITAL, Code = "8867-4", DisplayName = "Heart rate", ValueText = "96", Unit = "bpm", RefRangeLow = 60, RefRangeHigh = 99, RefRangeText = "60-99 bpm", IsAbnormal = false, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientA, Category = ObservationCategory.VITAL, Code = "2708-6", DisplayName = "SpO2", ValueText = "92", Unit = "%", RefRangeLow = 95, RefRangeHigh = 100, RefRangeText = "95-100 %", IsAbnormal = true, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientA, Category = ObservationCategory.VITAL, Code = "8310-5", DisplayName = "Temperature", ValueText = "36.8", Unit = "°C", RefRangeLow = 36.1m, RefRangeHigh = 37.2m, RefRangeText = "36.1-37.2 °C", IsAbnormal = false, ObservedAt = DateTime.UtcNow },

            new Observation { Patient = patientB, Category = ObservationCategory.VITAL, Code = "85354-9", DisplayName = "Blood pressure", ValueText = "122/78", Unit = "mmHg", RefRangeLow = 110, RefRangeHigh = 139, RefRangeText = "110-139 mmHg", IsAbnormal = false, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientB, Category = ObservationCategory.VITAL, Code = "8867-4", DisplayName = "Heart rate", ValueText = "88", Unit = "bpm", RefRangeLow = 60, RefRangeHigh = 99, RefRangeText = "60-99 bpm", IsAbnormal = false, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientB, Category = ObservationCategory.VITAL, Code = "2708-6", DisplayName = "SpO2", ValueText = "98", Unit = "%", RefRangeLow = 95, RefRangeHigh = 100, RefRangeText = "95-100 %", IsAbnormal = false, ObservedAt = DateTime.UtcNow },
            new Observation { Patient = patientB, Category = ObservationCategory.VITAL, Code = "8310-5", DisplayName = "Temperature", ValueText = "38.4", Unit = "°C", RefRangeLow = 36.1m, RefRangeHigh = 37.2m, RefRangeText = "36.1-37.2 °C", IsAbnormal = true, ObservedAt = DateTime.UtcNow }
        });

        // Labs & Immunizations
        db.Observations.AddRange(new[]
        {
            new Observation { Patient = parentPatient, Category = ObservationCategory.LAB, Code = "2345-7", DisplayName = "Glucose", ValueNumber = 92, Unit = "mg/dL", RefRangeLow = 70, RefRangeHigh = 99, RefRangeText = "70-99 mg/dL", IsAbnormal = false, ObservedAt = daysAgo(10) },
            new Observation { Patient = parentPatient, Category = ObservationCategory.LAB, Code = "2093-3", DisplayName = "Total Cholesterol", ValueNumber = 212, Unit = "mg/dL", RefRangeLow = 0, RefRangeHigh = 200, RefRangeText = "<200 mg/dL", IsAbnormal = true, ObservedAt = daysAgo(10) },
            new Observation { Patient = parentPatient, Category = ObservationCategory.IMMUNIZATION, Code = "140", DisplayName = "Influenza, seasonal", LotNumber = "FLU-2291", ImmunizationStatus = "COMPLETED", ImmunizationStatusCode = "UP_TO_DATE", ObservedAt = daysAgo(400) },

            new Observation { Patient = patientA, Category = ObservationCategory.IMMUNIZATION, Code = "10", DisplayName = "MMR", LotNumber = "MMR-7741", ImmunizationStatus = "COMPLETED", ImmunizationStatusCode = "UP_TO_DATE", ObservedAt = daysAgo(3000) },
            new Observation { Patient = patientA, Category = ObservationCategory.LAB, Code = "718-7", DisplayName = "Hemoglobin", ValueNumber = 13.4m, Unit = "g/dL", RefRangeLow = 13.5m, RefRangeHigh = 17.5m, RefRangeText = "13.5-17.5 g/dL", IsAbnormal = true, ObservedAt = daysAgo(45) },

            new Observation { Patient = patientB, Category = ObservationCategory.IMMUNIZATION, Code = "21", DisplayName = "Varicella", LotNumber = "VAR-3390", ImmunizationStatus = "COMPLETED", ImmunizationStatusCode = "UP_TO_DATE", ObservedAt = daysAgo(2500) }
        });

        // Historical HbA1c series
        var a1cHistory = new[]
        {
            (540, 9.4m), (450, 9.0m), (360, 8.6m), (270, 8.3m),
            (180, 7.9m), (90, 7.4m), (12, 6.9m)
        };
        foreach (var (days, val) in a1cHistory)
        {
            db.Observations.Add(new Observation
            {
                Patient = parentPatient,
                Category = ObservationCategory.LAB,
                Code = "4548-4",
                DisplayName = "HbA1c",
                ValueNumber = val,
                Unit = "%",
                RefRangeLow = 4,
                RefRangeHigh = 5.6m,
                RefRangeText = "4.0-5.6 %",
                IsAbnormal = val > 5.6m,
                ObservedAt = daysAgo(days)
            });
        }

        // Conditions & Procedures
        db.Conditions.AddRange(new[]
        {
            new Condition { Patient = parentPatient, Code = "38341003", Name = "Hypertension", ClinicalStatus = "CHRONIC", OnsetDate = new DateTime(2021, 9, 14, 0, 0, 0, DateTimeKind.Utc) },
            new Condition { Patient = parentPatient, Code = "44054006", Name = "Type 2 Diabetes Mellitus", ClinicalStatus = "ACTIVE", OnsetDate = new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Condition { Patient = patientA, Code = "50983009", Name = "Mild intermittent asthma", ClinicalStatus = "ACTIVE", OnsetDate = new DateTime(2019, 5, 30, 0, 0, 0, DateTimeKind.Utc) }
        });

        db.Procedures.AddRange(new[]
        {
            new Procedure { Patient = parentPatient, Code = "274031008", Name = "Routine physical examination", PerformedAt = daysAgo(120), PerformedBy = "Amara Patel, MD", Facility = "Bole Family Health Clinic (DEMO)" },
            new Procedure { Patient = parentPatient, Code = "168594001", Name = "Upper endoscopy", PerformedAt = daysAgo(500), PerformedBy = "Amara Patel, MD", Facility = "Bole Family Health Clinic (DEMO)" },
            new Procedure { Patient = patientA, Code = "185349003", Name = "Routine pediatric check-up", PerformedAt = daysAgo(180), PerformedBy = "Joon Kim, DO", Facility = "Yeka Pediatric Clinic (DEMO)" }
        });

        // Medications & Prescriptions
        var lisinopril = new Medication { Name = "Lisinopril", GenericName = "Lisinopril", Form = "Tablet", Strength = "10 mg", RxNormCode = "314076" };
        var metformin = new Medication { Name = "Metformin", GenericName = "Metformin HCl", Form = "Tablet", Strength = "500 mg", RxNormCode = "860975" };
        db.Medications.AddRange(lisinopril, metformin);

        db.Prescriptions.AddRange(new[]
        {
            new Prescription { Patient = parentPatient, Medication = lisinopril, Provider = drPatel, Dosage = "10 mg", Frequency = "Once daily", Instructions = "Take in the morning.", IsActive = true },
            new Prescription { Patient = parentPatient, Medication = metformin, Provider = drPatel, Dosage = "500 mg", Frequency = "Twice daily with meals", IsActive = true }
        });

        // Appointments
        var inDays = (int n) => DateTime.UtcNow.AddDays(n);
        db.Appointments.AddRange(new[]
        {
            new Appointment
            {
                Patient = parentPatient,
                Provider = drPatel,
                Location = clinic,
                Department = deptFamMed,
                AppointmentType = apptTypeRoutine,
                RoomResource = clinicRoom1,
                StartAt = inDays(14),
                EndAt = inDays(14).AddMinutes(30),
                Status = AppointmentStatus.CONFIRMED,
                ReasonForVisit = "Follow-up on blood pressure"
            },
            new Appointment
            {
                Patient = patientA,
                Provider = drKim,
                Location = northBranch,
                Department = deptPeds,
                AppointmentType = apptTypeRoutine,
                RoomResource = pedsRoom1,
                StartAt = inDays(21),
                EndAt = inDays(21).AddMinutes(20),
                Status = AppointmentStatus.CONFIRMED,
                ReasonForVisit = "Asthma action plan review"
            }
        });

        // Billing
        var st1 = new Statement
        {
            Patient = parentPatient,
            TotalCents = 250000,
            AmountPaidCents = 50000,
            BalanceCents = 200000,
            Description = "Outpatient general medical consultation and comprehensive laboratory work",
            DueAt = inDays(30),
            Status = StatementStatus.PARTIALLY_PAID
        };
        var st2 = new Statement
        {
            Patient = parentPatient,
            TotalCents = 150000,
            AmountPaidCents = 0,
            BalanceCents = 150000,
            Description = "Cardiology specialist consultation (overdue)",
            DueAt = daysAgo(20),
            Status = StatementStatus.OPEN
        };
        var st3 = new Statement
        {
            Patient = patientB,
            TotalCents = 85000,
            AmountPaidCents = 0,
            BalanceCents = 85000,
            Description = "Pediatric wellness visit & immunizations",
            DueAt = inDays(15),
            Status = StatementStatus.OPEN
        };
        db.Statements.AddRange(st1, st2, st3);

        db.Payments.Add(new Payment
        {
            Statement = st1,
            AmountCents = 50000,
            PaymentToken = "pi_seed_001",
            GatewayPaymentIntentId = "pi_seed_001",
            GatewayChargeId = "ch_seed_001",
            Status = PaymentStatus.SUCCEEDED,
            Last4 = "1001",
            CardBrand = "visa"
        });

        // Form definition
        db.FormDefinitions.Add(new FormDefinition
        {
            Slug = "annual-checkin",
            Title = "Annual Health Check-in",
            Description = "Please review and update your general health details ahead of your upcoming visit.",
            Category = "Clinical",
            FieldsJson = @"[
                {""id"":""symptoms"",""label"":""Any new symptoms since your last visit?"",""type"":""textarea"",""required"":false},
                {""id"":""smoking"",""label"":""Do you currently use tobacco products?"",""type"":""boolean"",""required"":true},
                {""id"":""exercise_days"",""label"":""Days per week you engage in moderate exercise"",""type"":""number"",""required"":false}
            ]"
        });

        await db.SaveChangesAsync();

        // Initialize snapshots
        foreach (var p in new[] { parentPatient, patientA, patientB })
        {
            var snapService = new SnapshotService(db);
            await snapService.RefreshSnapshotAsync(p.Id);
        }
    }

    private static async Task SeedOrganizationsForExistingDbAsync(MedicalPortalDbContext db)
    {
        var org = new Organization
        {
            Name = "Addis Care Health Network (DEMO)",
            Code = "SHS",
            TaxId = "ET-1234567",
            Website = "https://addiscare.example.org",
            Phone = "+251 11 661 0199"
        };
        db.Organizations.Add(org);

        var facilitySrmc = new Facility
        {
            Organization = org,
            Name = "Addis Central General Hospital (DEMO)",
            Code = "SRMC",
            AddressLine1 = "Bole Road, Olympia Area",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0200"
        };
        db.Facilities.Add(facilitySrmc);

        var facilityRop = new Facility
        {
            Organization = org,
            Name = "Bole Outpatient Pavilion (DEMO)",
            Code = "ROP",
            AddressLine1 = "Airport Road, Bole",
            City = "Addis Ababa",
            State = "Addis Ababa",
            PostalCode = "1000",
            Phone = "+251 11 661 0100"
        };
        db.Facilities.Add(facilityRop);

        var deptFamMed = new Department
        {
            Facility = facilitySrmc,
            Name = "Family Medicine & Primary Care",
            Code = "FAM-MED",
            Specialty = "Family Medicine",
            Phone = "+251 11 661 0201"
        };
        db.Departments.Add(deptFamMed);

        var deptCardiology = new Department
        {
            Facility = facilitySrmc,
            Name = "Cardiology & Internal Medicine",
            Code = "CARD-INT",
            Specialty = "Cardiology",
            Phone = "+251 11 661 0202"
        };
        db.Departments.Add(deptCardiology);

        var deptPeds = new Department
        {
            Facility = facilityRop,
            Name = "Pediatrics & Adolescent Medicine",
            Code = "PEDS",
            Specialty = "Pediatrics",
            Phone = "+251 11 661 0111"
        };
        db.Departments.Add(deptPeds);

        var deptUrgentCare = new Department
        {
            Facility = facilityRop,
            Name = "Urgent Care & Occupational Health",
            Code = "URG-CARE",
            Specialty = "Urgent Care",
            Phone = "+251 11 661 0112"
        };
        db.Departments.Add(deptUrgentCare);

        var drPatel = await db.Providers.FirstOrDefaultAsync(p => p.Email == "apatel@clinic.example");
        var drKim = await db.Providers.FirstOrDefaultAsync(p => p.Email == "jkim@clinic.example");

        if (drPatel != null)
        {
            db.ProviderDepartments.Add(new ProviderDepartment { Provider = drPatel, Department = deptFamMed, IsPrimary = true });
            db.ProviderDepartments.Add(new ProviderDepartment { Provider = drPatel, Department = deptCardiology, IsPrimary = false });
        }

        if (drKim != null)
        {
            db.ProviderDepartments.Add(new ProviderDepartment { Provider = drKim, Department = deptPeds, IsPrimary = true });
            db.ProviderDepartments.Add(new ProviderDepartment { Provider = drKim, Department = deptUrgentCare, IsPrimary = false });
        }

        var clinic = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");
        if (clinic != null) clinic.Department = deptFamMed;

        var northBranch = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Yeka Pediatric Clinic (DEMO)");
        if (northBranch != null) northBranch.Department = deptPeds;

        var patelAccount = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == "apatel@clinic.example");
        if (patelAccount != null)
        {
            db.UserMemberships.Add(new UserMembership { UserAccount = patelAccount, Facility = facilitySrmc, Department = deptFamMed, Role = "CLINICIAN" });
            db.UserMemberships.Add(new UserMembership { UserAccount = patelAccount, Facility = facilityRop, Role = "VISITING_CLINICIAN" });
        }

        var parentAccount = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == "dana@example.com");
        if (parentAccount != null)
        {
            db.UserMemberships.Add(new UserMembership { UserAccount = parentAccount, Facility = facilitySrmc, Role = "PATIENT" });
        }

        var appts = await db.Appointments.ToListAsync();
        foreach (var appt in appts)
        {
            if (appt.LocationId == clinic?.Id) appt.Department = deptFamMed;
            else if (appt.LocationId == northBranch?.Id) appt.Department = deptPeds;
            else appt.Department = deptFamMed;
        }

        await db.SaveChangesAsync();
    }

    public static async Task SeedSchedulingForExistingDbAsync(MedicalPortalDbContext db)
    {
        var apptTypes = new[]
        {
            new AppointmentType { Name = "Routine Follow-up", Code = "ROUTINE", DurationMinutes = 30, Description = "Standard routine follow-up consultation." },
            new AppointmentType { Name = "Annual Wellness Exam", Code = "WELLNESS", DurationMinutes = 45, Description = "Comprehensive annual preventative health exam." },
            new AppointmentType { Name = "New Patient Consultation", Code = "NEW_PATIENT", DurationMinutes = 60, Description = "Initial consultation and complete medical history intake." },
            new AppointmentType { Name = "Urgent Care Visit", Code = "URGENT", DurationMinutes = 15, Description = "Acute illness or quick symptom evaluation." }
        };
        db.AppointmentTypes.AddRange(apptTypes);

        var clinic = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");
        var northBranch = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Yeka Pediatric Clinic (DEMO)");
        var drPatel = await db.Providers.FirstOrDefaultAsync(p => p.Email == "apatel@clinic.example");
        var drKim = await db.Providers.FirstOrDefaultAsync(p => p.Email == "jkim@clinic.example");

        if (clinic != null)
        {
            clinic.TimeZoneId = "Africa/Addis_Ababa";
            var r1 = new RoomResource { Location = clinic, Name = "Exam Room 1", Code = "EXAM-1" };
            var r2 = new RoomResource { Location = clinic, Name = "Exam Room 2", Code = "EXAM-2" };
            var r3 = new RoomResource { Location = clinic, Name = "Procedure Suite", Code = "PROC-1" };
            db.RoomResources.AddRange(r1, r2, r3);

            if (drPatel != null)
            {
                foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
                {
                    db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
                    {
                        Provider = drPatel,
                        Location = clinic,
                        DayOfWeek = day,
                        StartTime = new TimeSpan(8, 30, 0),
                        EndTime = new TimeSpan(16, 30, 0)
                    });
                }
            }

            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            {
                db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
                {
                    Location = clinic,
                    DayOfWeek = day,
                    StartTime = new TimeSpan(8, 0, 0),
                    EndTime = new TimeSpan(17, 0, 0)
                });
            }
        }

        if (northBranch != null)
        {
            northBranch.TimeZoneId = "Africa/Addis_Ababa";
            var nr1 = new RoomResource { Location = northBranch, Name = "Peds Exam Room 1", Code = "PEDS-1" };
            var nr2 = new RoomResource { Location = northBranch, Name = "Peds Exam Room 2", Code = "PEDS-2" };
            db.RoomResources.AddRange(nr1, nr2);

            if (drKim != null)
            {
                foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
                {
                    db.WeeklyScheduleTemplates.Add(new WeeklyScheduleTemplate
                    {
                        Provider = drKim,
                        Location = northBranch,
                        DayOfWeek = day,
                        StartTime = new TimeSpan(9, 0, 0),
                        EndTime = new TimeSpan(17, 0, 0)
                    });
                }
            }
        }

        var holidayDate = DateTime.UtcNow.Date.AddDays(30);
        db.ScheduleBlocks.Add(new ScheduleBlock
        {
            Location = clinic,
            StartAt = holidayDate.AddHours(8),
            EndAt = holidayDate.AddHours(17),
            Reason = "Facility Maintenance & Staff Training",
            IsHoliday = true
        });

        var appts = await db.Appointments.ToListAsync();
        var routineType = apptTypes[0];
        foreach (var appt in appts)
        {
            if (appt.AppointmentTypeId == null)
                appt.AppointmentType = routineType;
        }

        await db.SaveChangesAsync();
    }

    public static async Task SeedCalendarDimensionAsync(MedicalPortalDbContext db)
    {
        var start = new DateOnly(1900, 1, 1);
        var end = new DateOnly(2100, 12, 31);
        var batch = new List<CalendarDim>(2500);

        for (var curr = start; curr <= end; curr = curr.AddDays(1))
        {
            batch.Add(CalendarDim.FromGregorianDate(curr));
            if (batch.Count >= 2500)
            {
                await db.CalendarDimensions.AddRangeAsync(batch);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await db.CalendarDimensions.AddRangeAsync(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            batch.Clear();
        }
    }
}
