using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using Xunit;

namespace MedicalPortal.Tests;

public class DomainLogicTests
{
    [Fact]
    public void AgeFrom_CalculatesAgeCorrectly()
    {
        var dob = new DateTime(1986, 6, 20, 0, 0, 0, DateTimeKind.Utc);
        var asOfBeforeBirthday = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var asOfAfterBirthday = new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(37, ClinicalFormatting.AgeFrom(dob, asOfBeforeBirthday));
        Assert.Equal(38, ClinicalFormatting.AgeFrom(dob, asOfAfterBirthday));
    }

    [Fact]
    public void Scheduling_CheckSlot_DetectsConflicts()
    {
        var baseTime = DateTime.UtcNow.Date.AddDays(2).AddHours(10);
        var busy = new[]
        {
            new SchedulingLogic.TimeSlot(baseTime, baseTime.AddMinutes(30))
        };

        // Overlapping slot
        var candidateConflicting = new SchedulingLogic.TimeSlot(baseTime.AddMinutes(15), baseTime.AddMinutes(45));
        var result1 = SchedulingLogic.CheckSlot(candidateConflicting, busy);
        Assert.False(result1.Ok);
        Assert.Equal("conflicts_with_existing", result1.Problem);

        // Non-overlapping slot
        var candidateFree = new SchedulingLogic.TimeSlot(baseTime.AddMinutes(30), baseTime.AddMinutes(60));
        var result2 = SchedulingLogic.CheckSlot(candidateFree, busy);
        Assert.True(result2.Ok);
    }

    [Fact]
    public void Scheduling_WithinBookingWindow_Enforces180Days()
    {
        var now = DateTime.UtcNow;
        Assert.False(SchedulingLogic.WithinBookingWindow(now.AddDays(-1), now));
        Assert.True(SchedulingLogic.WithinBookingWindow(now.AddDays(30), now));
        Assert.False(SchedulingLogic.WithinBookingWindow(now.AddDays(181), now));
    }

    [Fact]
    public void Billing_BuildSchedule_DividesRemaindersCorrectly()
    {
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // $100.00 total across 3 payments -> 3334, 3333, 3333
        var schedule = BillingLogic.BuildSchedule(10000, 3, startDate);

        Assert.Equal(3, schedule.Count);
        Assert.Equal(3334, schedule[0].AmountCents);
        Assert.Equal(3333, schedule[1].AmountCents);
        Assert.Equal(3333, schedule[2].AmountCents);
        Assert.Equal(10000, schedule.Sum(s => s.AmountCents));
    }

    [Fact]
    public void Billing_AssertPlanAllowed_EnforcesMinimums()
    {
        // Balance < $50 throws
        Assert.Throws<BillingException>(() =>
            BillingLogic.AssertPlanAllowed(4999, 3, false));

        // Invalid installments throws
        Assert.Throws<BillingException>(() =>
            BillingLogic.AssertPlanAllowed(10000, 1, false));

        // Already active plan throws
        Assert.Throws<BillingException>(() =>
            BillingLogic.AssertPlanAllowed(10000, 6, true));

        // Valid plan passes
        BillingLogic.AssertPlanAllowed(10000, 6, false);
    }

    [Fact]
    public void AllergyCheck_DetectsPenicillinCrossReactivity()
    {
        var allergies = new[]
        {
            new Allergy
            {
                Substance = "Penicillin",
                DrugClass = "Beta-lactam (penicillin)",
                DrugClassCode = "J01CA",
                Severity = AllergySeverity.ANAPHYLAXIS
            }
        };

        // Amoxicillin (J01CA01 / J01CA) should trigger contraindication
        var result = AllergyLogic.CheckPrescription("Amoxicillin", new[] { "J01CA01", "J01CA" }, allergies);
        Assert.False(result.Safe);
        Assert.Contains("Penicillin", result.BlockReason);

        // Metformin should be safe
        var safeResult = AllergyLogic.CheckPrescription("Metformin", new[] { "A10BA02", "A10BA" }, allergies);
        Assert.True(safeResult.Safe);
    }

    [Fact]
    public void GeoLogic_CalculatesDistanceAccurately()
    {
        // Springfield Clinic to Memorial Urgent Care
        var p1 = new LatLng(39.7983, -89.6437);
        var p2 = new LatLng(39.7935, -89.6512);

        var km = GeoLogic.DistanceKm(p1, p2);
        Assert.True(km > 0.5 && km < 2.0);
        Assert.Equal("<1 km", GeoLogic.DistanceLabel(0.8));
        Assert.Equal("3.2 km", GeoLogic.DistanceLabel(3.24));
    }
}
