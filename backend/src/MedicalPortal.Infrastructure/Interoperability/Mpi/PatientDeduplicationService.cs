using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Interoperability.Mpi;

public class PatientDeduplicationService : IPatientDeduplicationService
{
    private readonly MedicalPortalDbContext _db;
    private readonly ILogger<PatientDeduplicationService> _logger;

    public PatientDeduplicationService(
        MedicalPortalDbContext db,
        ILogger<PatientDeduplicationService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<PatientDeduplicationService>.Instance;
    }

    public async Task<int> ScanForDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
        var patients = await _db.Patients
            .Include(p => p.Person)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        int detectedCount = 0;

        for (int i = 0; i < patients.Count; i++)
        {
            var p1 = patients[i];
            for (int j = i + 1; j < patients.Count; j++)
            {
                var p2 = patients[j];

                var matchReasons = new List<string>();
                decimal score = 0m;

                // 1. National Patient ID (Fayya) exact match
                if (!string.IsNullOrWhiteSpace(p1.NationalPatientId) &&
                    !string.IsNullOrWhiteSpace(p2.NationalPatientId) &&
                    string.Equals(p1.NationalPatientId, p2.NationalPatientId, StringComparison.OrdinalIgnoreCase))
                {
                    score = 1.0m;
                    matchReasons.Add("Identical National Digital Patient ID (Fayya)");
                }
                else
                {
                    // 2. Exact phone match
                    bool phoneMatch = !string.IsNullOrWhiteSpace(p1.Person.Phone) &&
                                      !string.IsNullOrWhiteSpace(p2.Person.Phone) &&
                                      string.Equals(p1.Person.Phone.Trim(), p2.Person.Phone.Trim(), StringComparison.OrdinalIgnoreCase);

                    // 3. Exact Date of Birth match
                    bool dobMatch = p1.Person.DateOfBirth.Date == p2.Person.DateOfBirth.Date;

                    // 4. Exact or near name match
                    bool lastNameMatch = string.Equals(p1.Person.LastName.Trim(), p2.Person.LastName.Trim(), StringComparison.OrdinalIgnoreCase);
                    bool firstNameMatch = string.Equals(p1.Person.FirstName.Trim(), p2.Person.FirstName.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (phoneMatch && dobMatch)
                    {
                        score = 0.95m;
                        matchReasons.Add("Exact Phone Match");
                        matchReasons.Add("Matching Date of Birth");
                        if (lastNameMatch) matchReasons.Add("Matching Last Name");
                    }
                    else if (dobMatch && lastNameMatch && firstNameMatch)
                    {
                        score = 0.90m;
                        matchReasons.Add("Matching Full Name & Date of Birth");
                    }
                    else if (phoneMatch && (lastNameMatch || firstNameMatch))
                    {
                        score = 0.80m;
                        matchReasons.Add("Exact Phone Match");
                        matchReasons.Add("Matching Name Component");
                    }
                    else if (dobMatch && lastNameMatch)
                    {
                        score = 0.70m;
                        matchReasons.Add("Matching Date of Birth & Family Name");
                    }
                }

                if (score >= 0.70m)
                {
                    // Check if candidate pair already recorded
                    var alreadyFlagged = await _db.PatientDuplicateCandidates.AnyAsync(c =>
                        (c.PrimaryPatientId == p1.Id && c.CandidateDuplicatePatientId == p2.Id) ||
                        (c.PrimaryPatientId == p2.Id && c.CandidateDuplicatePatientId == p1.Id),
                        cancellationToken);

                    if (!alreadyFlagged)
                    {
                        var candidate = new PatientDuplicateCandidate
                        {
                            TenantId = tenantId,
                            PrimaryPatientId = p1.Id,
                            CandidateDuplicatePatientId = p2.Id,
                            ConfidenceScore = score,
                            MatchReasonsJson = JsonSerializer.Serialize(matchReasons),
                            Status = DuplicateResolutionStatus.Pending,
                            DetectedAtUtc = DateTime.UtcNow
                        };

                        _db.PatientDuplicateCandidates.Add(candidate);
                        detectedCount++;
                    }
                }
            }
        }

        if (detectedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("MPI duplicate scanner identified {Count} potential duplicate patient pairs.", detectedCount);
        }

        return detectedCount;
    }

    public async Task<bool> ResolveDuplicateAsync(
        Guid candidateId,
        DuplicateResolutionStatus resolution,
        Guid resolvedByUserId,
        string? resolutionNotes = null,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _db.PatientDuplicateCandidates
            .Include(c => c.PrimaryPatient)
            .Include(c => c.CandidateDuplicatePatient)
            .FirstOrDefaultAsync(c => c.Id == candidateId, cancellationToken);

        if (candidate == null || candidate.Status != DuplicateResolutionStatus.Pending)
        {
            return false;
        }

        candidate.Status = resolution;
        candidate.ResolvedAtUtc = DateTime.UtcNow;
        candidate.ResolvedByUserId = resolvedByUserId;
        candidate.ResolutionNotes = resolutionNotes;

        if (resolution == DuplicateResolutionStatus.Merged)
        {
            // Re-key child collections from CandidateDuplicatePatient to PrimaryPatient
            var duplicateId = candidate.CandidateDuplicatePatientId;
            var primaryId = candidate.PrimaryPatientId;

            var appointments = await _db.Appointments.Where(a => a.PatientId == duplicateId).ToListAsync(cancellationToken);
            foreach (var a in appointments) a.PatientId = primaryId;

            var forms = await _db.FormResponses.Where(f => f.PatientId == duplicateId).ToListAsync(cancellationToken);
            foreach (var f in forms) f.PatientId = primaryId;

            var statements = await _db.Statements.Where(s => s.PatientId == duplicateId).ToListAsync(cancellationToken);
            foreach (var s in statements) s.PatientId = primaryId;

            // Merge National ID if missing on primary
            if (string.IsNullOrWhiteSpace(candidate.PrimaryPatient.NationalPatientId) &&
                !string.IsNullOrWhiteSpace(candidate.CandidateDuplicatePatient.NationalPatientId))
            {
                candidate.PrimaryPatient.NationalPatientId = candidate.CandidateDuplicatePatient.NationalPatientId;
            }

            // Append merge audit entry
            var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;
            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                Action = "PATIENT_MERGE",
                ResourceType = "Patient",
                ResourceId = primaryId.ToString(),
                PatientId = primaryId,
                ActorUserAccountId = resolvedByUserId != Guid.Empty ? resolvedByUserId : null,
                CreatedAt = DateTime.UtcNow,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    MergedPatientId = duplicateId,
                    PrimaryPatientId = primaryId,
                    CandidateId = candidateId,
                    Notes = resolutionNotes
                })
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Resolved duplicate candidate {CandidateId} with status {Status}.", candidateId, resolution);
        return true;
    }

    public async Task<IReadOnlyList<DuplicateCandidateDto>> GetPendingCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _db.PatientDuplicateCandidates
            .Include(c => c.PrimaryPatient).ThenInclude(p => p.Person)
            .Include(c => c.CandidateDuplicatePatient).ThenInclude(p => p.Person)
            .Where(c => c.Status == DuplicateResolutionStatus.Pending)
            .OrderByDescending(c => c.ConfidenceScore)
            .ToListAsync(cancellationToken);

        return candidates.Select(c => new DuplicateCandidateDto(
            Id: c.Id,
            PrimaryPatientId: c.PrimaryPatientId,
            PrimaryPatientMrn: c.PrimaryPatient.Mrn,
            PrimaryPatientName: $"{c.PrimaryPatient.Person.FirstName} {c.PrimaryPatient.Person.LastName}",
            CandidateDuplicatePatientId: c.CandidateDuplicatePatientId,
            CandidateDuplicateMrn: c.CandidateDuplicatePatient.Mrn,
            CandidateDuplicateName: $"{c.CandidateDuplicatePatient.Person.FirstName} {c.CandidateDuplicatePatient.Person.LastName}",
            ConfidenceScore: c.ConfidenceScore,
            MatchReasonsJson: c.MatchReasonsJson,
            Status: c.Status.ToString(),
            DetectedAtUtc: c.DetectedAtUtc
        )).ToList();
    }
}
