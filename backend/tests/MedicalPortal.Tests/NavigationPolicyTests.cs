using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Tests;

public class NavigationPolicyTests
{
    [Fact]
    public void PatientNavigationIncludesFormsAndStaffNavigationDoesNot()
    {
        Assert.Contains("forms", NavigationPolicy.ForRole(UserRole.PATIENT));
        Assert.DoesNotContain("forms", NavigationPolicy.ForRole(UserRole.PHYSICIAN));
        Assert.DoesNotContain("forms", NavigationPolicy.ForRole(UserRole.NURSE));
        Assert.DoesNotContain("forms", NavigationPolicy.ForRole(UserRole.COMPLIANCE_AUDITOR));
    }

    [Fact]
    public void RoleNavigationDoesNotExposeRetiredOrPatientOnlyModules()
    {
        foreach (var role in Enum.GetValues<UserRole>().Where(role => role != UserRole.PATIENT))
        {
            var modules = NavigationPolicy.ForRole(role);
            Assert.DoesNotContain("findcare", modules);
            Assert.DoesNotContain("nearby", modules);
            Assert.DoesNotContain("family", modules);
            Assert.DoesNotContain("sharing", modules);
        }
    }

    [Fact]
    public void BillingNavigationIsLimitedToBillingAndSettings()
    {
        Assert.Equal(["billing", "settings"], NavigationPolicy.ForRole(UserRole.BILLING));
    }
}
