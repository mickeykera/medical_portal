using System.Net;
using MedicalPortal.Core.Entities;

namespace MedicalPortal.Core.Domain;

public static class ClinicalSummaryGenerator
{
    public static string GenerateHtml(
        Patient patient,
        List<Condition> conditions,
        List<Allergy> allergies,
        List<Prescription> prescriptions,
        List<Observation> vitalsAndLabs)
    {
        var p = patient.Person;
        var now = DateTime.UtcNow.ToString("MMM dd, yyyy HH:mm 'UTC'");
        var dob = p.DateOfBirth.ToString("yyyy-MM-dd");
        var age = ClinicalFormatting.AgeFrom(p.DateOfBirth, DateTime.UtcNow);

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <title>Clinical Visit Summary - {WebUtility.HtmlEncode(p.LastName)}, {WebUtility.HtmlEncode(p.FirstName)}</title>
  <style>
    body {{
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      line-height: 1.5;
      color: #1e293b;
      margin: 0;
      padding: 2.5rem;
      background: #fff;
    }}
    @media print {{
      body {{ padding: 0.5in; }}
      .no-print {{ display: none; }}
    }}
    .header {{
      display: flex;
      justify-content: space-between;
      border-bottom: 2px solid #0284c7;
      padding-bottom: 1rem;
      margin-bottom: 1.5rem;
    }}
    .clinic-title {{ font-size: 1.4rem; font-weight: 800; color: #0f172a; }}
    .clinic-sub {{ font-size: 0.85rem; color: #64748b; }}
    .doc-badge {{ font-size: 0.8rem; font-weight: 700; color: #0284c7; text-transform: uppercase; letter-spacing: 0.05em; }}
    .patient-card {{
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 1rem 1.25rem;
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 0.75rem;
      margin-bottom: 1.5rem;
    }}
    .patient-label {{ font-size: 0.75rem; text-transform: uppercase; color: #64748b; font-weight: 600; }}
    .patient-val {{ font-size: 0.95rem; font-weight: 700; color: #0f172a; }}
    .section-title {{
      font-size: 1.05rem;
      font-weight: 700;
      color: #0f172a;
      border-bottom: 1px solid #cbd5e1;
      padding-bottom: 0.35rem;
      margin-top: 1.5rem;
      margin-bottom: 0.75rem;
      display: flex;
      justify-content: space-between;
    }}
    table {{ width: 100%; border-collapse: collapse; margin-bottom: 1rem; font-size: 0.875rem; }}
    th, td {{ text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid #e2e8f0; }}
    th {{ background: #f1f5f9; font-weight: 600; color: #475569; }}
    .pill {{
      display: inline-block;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.75rem;
      font-weight: 600;
    }}
    .pill-danger {{ background: #fee2e2; color: #991b1b; }}
    .pill-good {{ background: #dcfce7; color: #166534; }}
    .pill-info {{ background: #e0f2fe; color: #075985; }}
    .signature-block {{
      margin-top: 3rem;
      display: flex;
      justify-content: space-between;
      border-top: 1px dashed #cbd5e1;
      padding-top: 1rem;
      font-size: 0.8125rem;
      color: #64748b;
    }}
    .btn-print {{
      background: #0284c7;
      color: white;
      border: none;
      padding: 0.5rem 1rem;
      border-radius: 6px;
      font-weight: 600;
      cursor: pointer;
    }}
  </style>
</head>
<body>
  <div class=""no-print"" style=""margin-bottom: 1.5rem; display: flex; justify-content: flex-end; gap: 0.75rem;"">
    <button id=""btn-print"" class=""btn-print"" type=""button"">🖨️ Print / Save as PDF</button>
  </div>

  <div class=""header"">
    <div>
      <div class=""clinic-title"">🏥 Riverside Family Medicine & Health Network</div>
      <div class=""clinic-sub"">400 Riverside Drive, Springfield, IL 62701 • Phone: (217) 555-0100</div>
    </div>
    <div style=""text-align: right;"">
      <div class=""doc-badge"">Official Health Summary</div>
      <div style=""font-size: 0.8125rem; color: #64748b;"">Exported: {now}</div>
    </div>
  </div>

  <div class=""patient-card"">
    <div>
      <div class=""patient-label"">Patient Name</div>
      <div class=""patient-val"">{WebUtility.HtmlEncode(p.LastName)}, {WebUtility.HtmlEncode(p.FirstName)}</div>
    </div>
    <div>
      <div class=""patient-label"">MRN</div>
      <div class=""patient-val"">{WebUtility.HtmlEncode(patient.Mrn)}</div>
    </div>
    <div>
      <div class=""patient-label"">DOB / Age</div>
      <div class=""patient-val"">{dob} ({age} yrs)</div>
    </div>
    <div>
      <div class=""patient-label"">Primary Provider</div>
      <div class=""patient-val"">{(patient.PrimaryProvider != null ? $"Dr. {WebUtility.HtmlEncode(patient.PrimaryProvider.FirstName)} {WebUtility.HtmlEncode(patient.PrimaryProvider.LastName)}" : "Assigned Care Team")}</div>
    </div>
  </div>

  <div class=""section-title"">⚠️ Documented Allergies & Intolerances</div>
  <table>
    <thead>
      <tr>
        <th>Substance</th>
        <th>Category / Class</th>
        <th>Severity</th>
        <th>Clinical Reaction</th>
      </tr>
    </thead>
    <tbody>
      {(allergies.Count > 0 ? string.Join("", allergies.Select(a => $@"
        <tr>
          <td style=""font-weight: 600;"">{WebUtility.HtmlEncode(a.Substance)}</td>
          <td><code>{WebUtility.HtmlEncode(a.DrugClassCode ?? a.DrugClass ?? "—")}</code></td>
          <td><span class=""pill {(a.Severity.ToString().Equals("HIGH", StringComparison.OrdinalIgnoreCase) ? "pill-danger" : "pill-info")}"">{a.Severity}</span></td>
          <td>{WebUtility.HtmlEncode(a.Reaction ?? "Documented clinical hypersensitivity")}</td>
        </tr>
      ")) : "<tr><td colspan=\"4\" style=\"color:#64748b;\">No documented drug or environmental allergies.</td></tr>")}
    </tbody>
  </table>

  <div class=""section-title"">💊 Active Medications & Prescriptions</div>
  <table>
    <thead>
      <tr>
        <th>Medication</th>
        <th>Strength & Form</th>
        <th>Dosage & Frequency</th>
        <th>Directions / Instructions</th>
        <th>Prescribed Date</th>
      </tr>
    </thead>
    <tbody>
      {(prescriptions.Count > 0 ? string.Join("", prescriptions.Select(rx => $@"
        <tr>
          <td style=""font-weight: 600;"">{WebUtility.HtmlEncode(rx.Medication?.Name ?? "Prescription")}</td>
          <td>{WebUtility.HtmlEncode(rx.Medication?.Strength ?? "")} {WebUtility.HtmlEncode(rx.Medication?.Form ?? "")}</td>
          <td>{WebUtility.HtmlEncode(rx.Dosage)} • {WebUtility.HtmlEncode(rx.Frequency)}</td>
          <td>{WebUtility.HtmlEncode(rx.Instructions ?? "As directed by physician")}</td>
          <td>{rx.PrescribedAt:yyyy-MM-dd}</td>
        </tr>
      ")) : "<tr><td colspan=\"5\" style=\"color:#64748b;\">No active prescriptions on record.</td></tr>")}
    </tbody>
  </table>

  <div class=""section-title"">🩺 Active Health Conditions & Diagnoses</div>
  <table>
    <thead>
      <tr>
        <th>Diagnosis / Condition</th>
        <th>Status</th>
        <th>Onset Date</th>
      </tr>
    </thead>
    <tbody>
      {(conditions.Count > 0 ? string.Join("", conditions.Select(c => $@"
        <tr>
          <td style=""font-weight: 600;"">{WebUtility.HtmlEncode(c.Name)}</td>
          <td><span class=""pill pill-good"">{WebUtility.HtmlEncode(c.ClinicalStatus ?? "Active")}</span></td>
          <td>{(c.OnsetDate.HasValue ? c.OnsetDate.Value.ToString("yyyy-MM-dd") : "Chronic / Ongoing")}</td>
        </tr>
      ")) : "<tr><td colspan=\"3\" style=\"color:#64748b;\">No active medical conditions recorded.</td></tr>")}
    </tbody>
  </table>

  <div class=""section-title"">🧪 Recent Laboratory Results & Observations</div>
  <table>
    <thead>
      <tr>
        <th>Test Name</th>
        <th>Result Value</th>
        <th>Reference Range</th>
        <th>Flag</th>
        <th>Observed At</th>
      </tr>
    </thead>
    <tbody>
      {(vitalsAndLabs.Count > 0 ? string.Join("", vitalsAndLabs.Select(v => $@"
        <tr>
          <td style=""font-weight: 600;"">{WebUtility.HtmlEncode(v.DisplayName)}</td>
          <td>{(v.ValueNumber.HasValue ? $"{v.ValueNumber} {WebUtility.HtmlEncode(v.Unit ?? "")}".Trim() : WebUtility.HtmlEncode(v.ValueText ?? "—"))}</td>
          <td>{WebUtility.HtmlEncode(v.RefRangeText ?? "Normal")}</td>
          <td>{(v.IsAbnormal ? "<span class=\"pill pill-danger\">OUT OF RANGE</span>" : "<span class=\"pill pill-good\">NORMAL</span>")}</td>
          <td>{v.ObservedAt:yyyy-MM-dd HH:mm}</td>
        </tr>
      ")) : "<tr><td colspan=\"5\" style=\"color:#64748b;\">No recent laboratory observations available.</td></tr>")}
    </tbody>
  </table>

  <div class=""signature-block"">
    <div>
      <div>Electronic Record Verification ID: <strong>{patient.Id}</strong></div>
      <div>Confidential Medical Record — Protected Health Information (HIPAA)</div>
    </div>
    <div style=""text-align: right;"">
      <div>Authorized Provider Signature: _______________________</div>
      <div>Date: {DateTime.UtcNow:yyyy-MM-dd}</div>
    </div>
  </div>
  <script src=""/js/print.js""></script>
</body>
</html>";
    }
}
