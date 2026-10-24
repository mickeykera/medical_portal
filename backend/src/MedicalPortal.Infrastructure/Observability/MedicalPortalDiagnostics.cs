using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MedicalPortal.Infrastructure.Observability;

public static class MedicalPortalDiagnostics
{
    public const string ServiceName = "MedicalPortal";
    public const string ActivitySourceName = "MedicalPortal.Tracing";
    public const string MeterName = "MedicalPortal.Metrics";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Metrics Instruments
    public static readonly Counter<long> LoginsTotal = Meter.CreateCounter<long>(
        "medical_portal.logins.total",
        unit: "{login}",
        description: "Total number of user login attempts recorded");

    public static readonly Counter<long> AppointmentsBookedTotal = Meter.CreateCounter<long>(
        "medical_portal.appointments.booked.total",
        unit: "{appointment}",
        description: "Total number of appointments successfully booked");

    public static readonly Counter<long> ClinicalSafetyChecksTotal = Meter.CreateCounter<long>(
        "medical_portal.clinical.safety_checks.total",
        unit: "{check}",
        description: "Total number of drug interaction and allergy contraindication checks evaluated");

    public static readonly Counter<long> PaymentsProcessedCents = Meter.CreateCounter<long>(
        "medical_portal.payments.processed_cents",
        unit: "{cents}",
        description: "Total volume of payment transactions processed in cents");

    public static readonly Counter<long> MessagesSentTotal = Meter.CreateCounter<long>(
        "medical_portal.messages.sent.total",
        unit: "{message}",
        description: "Total secure clinical and patient messages sent");

    public static readonly Histogram<double> HmsSyncDurationMs = Meter.CreateHistogram<double>(
        "medical_portal.hms.sync_duration_ms",
        unit: "ms",
        description: "Latency of institutional HMS bi-directional synchronization runs in milliseconds");

    public static readonly Counter<long> WorkerRunsTotal = Meter.CreateCounter<long>(
        "medical_portal.worker.runs.total",
        unit: "{run}",
        description: "Total background worker execution iterations");
}
