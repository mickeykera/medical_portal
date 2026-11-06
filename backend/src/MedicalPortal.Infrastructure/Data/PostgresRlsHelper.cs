using System.Text;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Data;

public static class PostgresRlsHelper
{
    public static readonly string[] TenantScopedTables = new[]
    {
        "Persons",
        "UserAccounts",
        "UserSessions",
        "Patients",
        "Providers",
        "Locations",
        "ProviderLocations",
        "CareTeams",
        "Appointments",
        "AppointmentTypes",
        "RoomResources",
        "WeeklyScheduleTemplates",
        "ScheduleBlocks",
        "Medications",
        "Prescriptions",
        "RefillRequests",
        "Allergies",
        "Observations",
        "Conditions",
        "Procedures",
        "HealthSnapshots",
        "Statements",
        "Payments",
        "ConsumedPaymentIntents",
        "PaymentPlans",
        "PaymentInstallments",
        "ProcessedWebhookEvents",
        "MessageThreads",
        "MessageThreadParticipants",
        "Messages",
        "ProxyGrants",
        "ProxyInvites",
        "RecordShares",
        "RecordShareAccesses",
        "FormDefinitions",
        "FormResponses",
        "NotificationPreferences",
        "NotificationDeliveryRecords",
        "AuditLogs",
        "BreakGlassOverrides",
        "Organizations",
        "Facilities",
        "Departments",
        "ProviderDepartments",
        "UserMemberships",
        "HmsDeadLetterRecords",
        "HmsDemographicConflicts",
        "HmsProcessedWebhookEvents"
    };

    public static string GenerateRlsSql(bool enable = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine(enable
            ? "-- Enable and Force Row-Level Security across all tenant-scoped tables"
            : "-- Row-Level Security disabled (Single-tenant mode active)");
        foreach (var table in TenantScopedTables)
        {
            sb.AppendLine($"DO $$ BEGIN");
            sb.AppendLine($"  IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = '{table}') THEN");
            if (enable)
            {
                sb.AppendLine($"    ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");
                sb.AppendLine($"    ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;");
                sb.AppendLine($"    DROP POLICY IF EXISTS \"{table}_tenant_isolation\" ON \"{table}\";");
                sb.AppendLine($"    CREATE POLICY \"{table}_tenant_isolation\" ON \"{table}\"");
                sb.AppendLine($"      FOR ALL");
                sb.AppendLine($"      USING (\"TenantId\" = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);");
            }
            else
            {
                sb.AppendLine($"    ALTER TABLE \"{table}\" DISABLE ROW LEVEL SECURITY;");
                sb.AppendLine($"    DROP POLICY IF EXISTS \"{table}_tenant_isolation\" ON \"{table}\";");
            }
            sb.AppendLine($"  END IF;");
            sb.AppendLine($"END $$;");
        }
        return sb.ToString();
    }

    public static async Task ApplyRlsAsync(DbContext db, CancellationToken ct = default, bool enable = true)
    {
        if (!db.Database.IsNpgsql()) return;
        var sql = GenerateRlsSql(enable);
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
