using System.Net;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MedicalPortal.Tests;

public class XssContainmentAndCspTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XssContainmentAndCspTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region 1. Content-Security-Policy (CSP) & Transport Hardening

    [Fact]
    public async Task CspHeader_EnforcesStrictScriptPolicy_WithoutUnsafeInlineOrCdns()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("Content-Security-Policy"),
            "Responses must include Content-Security-Policy header");

        var csp = response.Headers.GetValues("Content-Security-Policy").First();

        // Must disallow inline scripts and remote script CDNs (Remediation for SEC-02)
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.DoesNotContain("'unsafe-eval'", csp);
        Assert.DoesNotContain("https://cdnjs.cloudflare.com", csp);
        Assert.DoesNotContain("https://fonts.googleapis.com", csp);
        Assert.DoesNotContain("https://fonts.gstatic.com", csp);

        // Must include object-src 'none' and frame-ancestors 'none'
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
    }

    [Fact]
    public async Task DefensiveHeaders_EnforceNosniffAndFrameDeny()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/tenants/current");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").FirstOrDefault());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").FirstOrDefault());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").FirstOrDefault());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").FirstOrDefault());
        Assert.Contains("microphone=()", response.Headers.GetValues("Permissions-Policy").FirstOrDefault());
    }

    #endregion

    #region 2. Clinical Summary Generator XSS Containment (SEC-02 Server-Side)

    [Fact]
    public void ClinicalSummaryGenerator_EncodesUntrustedProviderAndObservationData()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            Mrn = "MRN-<script>alert('mrn')</script>",
            Person = new Person
            {
                FirstName = "Jane<img src=x onerror=alert('first')>",
                LastName = "Doe\" autofocus onfocus=alert('last')",
                DateOfBirth = new DateTime(1990, 5, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            PrimaryProvider = new Provider
            {
                FirstName = "<script>alert('provider-first')</script>",
                LastName = "Doctor<b onmouseover=alert('provider-last')>Attacker</b>"
            }
        };

        var allergies = new List<Allergy>
        {
            new Allergy
            {
                Substance = "<script>alert('allergy-substance')</script>",
                DrugClass = "Antibiotic<img src=x onerror=alert('drugclass')>",
                Severity = AllergySeverity.SEVERE,
                Reaction = "Anaphylaxis\"><script>alert('reaction')</script>"
            }
        };

        var conditions = new List<Condition>
        {
            new Condition
            {
                Name = "Hypertension<script>alert('condition')</script>",
                ClinicalStatus = "Active\" onfocus=alert('status')"
            }
        };

        var prescriptions = new List<Prescription>
        {
            new Prescription
            {
                Dosage = "10mg<script>alert('dosage')</script>",
                Frequency = "Daily\"><img src=x onerror=alert('freq')>",
                Instructions = "Take with water<svg/onload=alert('inst')>",
                Medication = new Medication
                {
                    Name = "Lisinopril<script>alert('med')</script>",
                    Strength = "10mg\" onfocus=alert('strength')",
                    Form = "Tablet<script>alert('form')</script>"
                }
            }
        };

        var vitals = new List<Observation>
        {
            new Observation
            {
                DisplayName = "Blood Pressure<script>alert('obs-name')</script>",
                ValueText = "<img src=x onerror=alert('obs-val')>",
                Unit = "<svg/onload=alert('unit')>",
                RefRangeText = "Normal<b onclick=alert('ref')>Range</b>"
            }
        };

        var html = ClinicalSummaryGenerator.GenerateHtml(patient, conditions, allergies, prescriptions, vitals);

        // 1. None of the raw unencoded XSS payload strings must be present in the output HTML
        Assert.DoesNotContain("<script>alert('provider-first')</script>", html);
        Assert.DoesNotContain("<b onmouseover=alert('provider-last')>", html);
        Assert.DoesNotContain("<img src=x onerror=alert('obs-val')>", html);
        Assert.DoesNotContain("<svg/onload=alert('unit')>", html);
        Assert.DoesNotContain("<b onclick=alert('ref')>", html);
        Assert.DoesNotContain("<script>alert('allergy-substance')</script>", html);
        Assert.DoesNotContain("<img src=x onerror=alert('drugclass')>", html);
        Assert.DoesNotContain("\"><script>alert('reaction')</script>", html);
        Assert.DoesNotContain("<script>alert('condition')</script>", html);
        Assert.DoesNotContain("\" onfocus=alert('status')", html);
        Assert.DoesNotContain("<script>alert('dosage')</script>", html);
        Assert.DoesNotContain("<svg/onload=alert('inst')>", html);
        Assert.DoesNotContain("<script>alert('med')</script>", html);
        Assert.DoesNotContain("\" onfocus=alert('strength')", html);

        // 2. Encoded entities must be present
        Assert.Contains("&lt;script&gt;alert(&#39;provider-first&#39;)&lt;/script&gt;", html);
        Assert.Contains("&lt;img src=x onerror=alert(&#39;obs-val&#39;)&gt;", html);
        Assert.Contains("&lt;script&gt;alert(&#39;allergy-substance&#39;)&lt;/script&gt;", html);

        // 3. Print button must not have inline event handlers (must use external script /js/print.js)
        Assert.DoesNotContain("onclick=\"window.print()\"", html);
        Assert.Contains("/js/print.js", html);
    }

    #endregion

    #region 3. Frontend Static Analysis & XSS Hygiene (SEC-02 Client-Side)

    [Fact]
    public void FrontendAppJs_DefinesAndUsesEscapeHtml_ForDynamicRendering()
    {
        var frontendDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../frontend"));
        if (!Directory.Exists(frontendDir) || !File.Exists(Path.Combine(frontendDir, "index.html")))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "frontend");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                {
                    frontendDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        var appJsPath = Path.Combine(frontendDir, "js/app.js");
        Assert.True(File.Exists(appJsPath), $"app.js not found at {appJsPath}");

        var content = File.ReadAllText(appJsPath);

        // 1. Must define escapeHtml function
        Assert.Contains("function escapeHtml(str)", content);
        Assert.Contains("&amp;", content);
        Assert.Contains("&lt;", content);
        Assert.Contains("&gt;", content);
        Assert.Contains("&quot;", content);
        Assert.Contains("&#39;", content);

        // 2. showToast must not interpolate unescaped HTML via innerHTML
        Assert.DoesNotContain("toast.innerHTML = `<div>${msg}</div>`", content);
        Assert.Contains("textDiv.textContent = msg", content);

        // 3. In-app dialogs must use textContent for message and title
        Assert.Contains("titleEl.textContent = title", content);
        Assert.Contains("msgEl.textContent = message", content);

        // 4. Critical areas must use escapeHtml
        Assert.Contains("escapeHtml(a.substance)", content);
        Assert.Contains("escapeHtml(a.drugClass", content);
        Assert.Contains("escapeHtml(u.email)", content);
        Assert.Contains("escapeHtml(m.senderName)", content);
        Assert.Contains("escapeHtml(m.body)", content);
        Assert.Contains("escapeHtml(err.message)", content);
    }

    [Fact]
    public void Frontend_PrintScript_ExistsAndHasZeroBrowserDialogs()
    {
        var frontendDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../frontend"));
        if (!Directory.Exists(frontendDir) || !File.Exists(Path.Combine(frontendDir, "index.html")))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "frontend");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                {
                    frontendDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        var printJsPath = Path.Combine(frontendDir, "js/print.js");
        Assert.True(File.Exists(printJsPath), $"print.js not found at {printJsPath}");

        var content = File.ReadAllText(printJsPath);
        Assert.Contains("btn-print", content);
        Assert.Contains("window.print()", content);
        Assert.DoesNotContain("alert(", content);
        Assert.DoesNotContain("confirm(", content);
        Assert.DoesNotContain("prompt(", content);
    }

    #endregion
}
