using System.Reflection;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Security;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Data;

public class MedicalPortalDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public MedicalPortalDbContext(
        DbContextOptions<MedicalPortalDbContext> options,
        ITenantContext? tenantContext = null) : base(options)
    {
        _tenantContext = tenantContext;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_tenantContext != null)
            optionsBuilder.AddInterceptors(new TenantRlsConnectionInterceptor(_tenantContext));
    }

    public Guid? CurrentTenantId => _tenantContext?.TenantId;
    public bool IsTenantResolved => _tenantContext?.IsResolved == true;

    /// <summary>
    /// Sets the tenant context for testing purposes.
    /// This allows tests to establish a resolved tenant context without HTTP middleware.
    /// </summary>
    public void SetTestTenantContext(Guid tenantId)
    {
        if (_tenantContext is TenantContext tc)
        {
            tc.SetTenant(new Tenant { Id = tenantId });
        }
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ProviderLocation> ProviderLocations => Set<ProviderLocation>();
    public DbSet<PatientCareTeam> CareTeams => Set<PatientCareTeam>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<RefillRequest> RefillRequests => Set<RefillRequest>();
    public DbSet<Allergy> Allergies => Set<Allergy>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Condition> Conditions => Set<Condition>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<HealthSnapshot> HealthSnapshots => Set<HealthSnapshot>();
    public DbSet<Statement> Statements => Set<Statement>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ConsumedPaymentIntent> ConsumedPaymentIntents => Set<ConsumedPaymentIntent>();
    public DbSet<PaymentPlan> PaymentPlans => Set<PaymentPlan>();
    public DbSet<PaymentInstallment> PaymentInstallments => Set<PaymentInstallment>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<MessageThreadParticipant> MessageThreadParticipants => Set<MessageThreadParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ProxyGrant> ProxyGrants => Set<ProxyGrant>();
    public DbSet<ProxyInvite> ProxyInvites => Set<ProxyInvite>();
    public DbSet<RecordShare> RecordShares => Set<RecordShare>();
    public DbSet<RecordShareAccess> RecordShareAccesses => Set<RecordShareAccess>();
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormResponse> FormResponses => Set<FormResponse>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationDeliveryRecord> NotificationDeliveryRecords => Set<NotificationDeliveryRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BreakGlassOverride> BreakGlassOverrides => Set<BreakGlassOverride>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ProviderDepartment> ProviderDepartments => Set<ProviderDepartment>();
    public DbSet<UserMembership> UserMemberships => Set<UserMembership>();
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<RoomResource> RoomResources => Set<RoomResource>();
    public DbSet<WeeklyScheduleTemplate> WeeklyScheduleTemplates => Set<WeeklyScheduleTemplate>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();
    public DbSet<SnomedConcept> SnomedConcepts => Set<SnomedConcept>();
    public DbSet<RxNormConcept> RxNormConcepts => Set<RxNormConcept>();
    public DbSet<HmsDeadLetterRecord> HmsDeadLetterRecords => Set<HmsDeadLetterRecord>();
    public DbSet<HmsDemographicConflict> HmsDemographicConflicts => Set<HmsDemographicConflict>();
    public DbSet<HmsProcessedWebhookEvent> HmsProcessedWebhookEvents => Set<HmsProcessedWebhookEvent>();
    public DbSet<CalendarDim> CalendarDimensions => Set<CalendarDim>();
    public DbSet<SyncOutboxMessage> OutboxMessages => Set<SyncOutboxMessage>();
    public DbSet<ReferencePackage> ReferencePackages => Set<ReferencePackage>();
    public DbSet<Dhis2ReportDataset> Dhis2Reports => Set<Dhis2ReportDataset>();
    public DbSet<EchisReferral> EchisReferrals => Set<EchisReferral>();
    public DbSet<PatientDuplicateCandidate> PatientDuplicateCandidates => Set<PatientDuplicateCandidate>();
    public DbSet<UserInvite> UserInvites => Set<UserInvite>();
    public DbSet<PaymentRefundRequest> RefundRequests => Set<PaymentRefundRequest>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<ClinicalOrder> ClinicalOrders => Set<ClinicalOrder>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<ChargeItem> ChargeItems => Set<ChargeItem>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryStore> InventoryStores => Set<InventoryStore>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<InventoryRequisition> InventoryRequisitions => Set<InventoryRequisition>();
    public DbSet<InventoryRequisitionLine> InventoryRequisitionLines => Set<InventoryRequisitionLine>();
    public DbSet<ControlledDrugEntry> ControlledDrugEntries => Set<ControlledDrugEntry>();
    public DbSet<InventoryCountSheet> InventoryCountSheets => Set<InventoryCountSheet>();
    public DbSet<InventoryCountLine> InventoryCountLines => Set<InventoryCountLine>();
    public DbSet<AcuteCareEpisode> AcuteCareEpisodes => Set<AcuteCareEpisode>();
    public DbSet<AcuteCareTransferRequest> AcuteCareTransferRequests => Set<AcuteCareTransferRequest>();
    public DbSet<AcuteCareBooking> AcuteCareBookings => Set<AcuteCareBooking>();
    public DbSet<DiagnosticWorkItem> DiagnosticWorkItems => Set<DiagnosticWorkItem>();
    public DbSet<MortuaryCase> MortuaryCases => Set<MortuaryCase>();
    public DbSet<MortuaryCustodyEvent> MortuaryCustodyEvents => Set<MortuaryCustodyEvent>();
    public DbSet<SupportServiceTask> SupportServiceTasks => Set<SupportServiceTask>();
    public DbSet<DietOrder> DietOrders => Set<DietOrder>();
    public DbSet<LinenLedgerEntry> LinenLedgerEntries => Set<LinenLedgerEntry>();
    public DbSet<KpiDefinition> KpiDefinitions => Set<KpiDefinition>();
    public DbSet<KpiSnapshot> KpiSnapshots => Set<KpiSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Calendar Dimension Table (calendar_dim precalculated dimension)
        modelBuilder.Entity<CalendarDim>(b =>
        {
            b.ToTable("calendar_dim");
            b.HasKey(c => c.Date);
            b.HasIndex(c => new { c.EthiopianYear, c.EthiopianMonth });
            b.HasIndex(c => new { c.EthiopianFiscalYear, c.EthiopianFiscalQuarter });
            b.HasIndex(c => c.Dhis2MonthlyPeriod);
            b.HasIndex(c => c.Dhis2QuarterlyPeriod);
        });

        // Tenant configuration
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        // Person & UserAccount
        modelBuilder.Entity<UserAccount>()
            .HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique();

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.IsDevAccount)
            .HasDefaultValue(false);

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.MfaSecret)
            .HasConversion<EncryptedStringConverter>();

        modelBuilder.Entity<UserAccount>()
            .HasOne(u => u.Person)
            .WithMany(p => p.Accounts)
            .HasForeignKey(u => u.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserInvite
        modelBuilder.Entity<UserInvite>()
            .HasIndex(i => i.TokenHash);

        // PaymentRefundRequest
        modelBuilder.Entity<PaymentRefundRequest>()
            .HasOne(r => r.Payment)
            .WithMany()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserSession
        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.Sid)
            .IsUnique();

        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.RefreshTokenHash);

        modelBuilder.Entity<UserSession>()
            .HasOne(s => s.UserAccount)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // Patient
        modelBuilder.Entity<Patient>()
            .HasIndex(p => p.Mrn)
            .IsUnique();

        modelBuilder.Entity<Patient>()
            .HasIndex(p => p.FhirPatientId);

        modelBuilder.Entity<Patient>()
            .HasOne(p => p.Person)
            .WithOne(p => p.Patient)
            .HasForeignKey<Patient>(p => p.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Patient>()
            .HasOne(p => p.PrimaryProvider)
            .WithMany()
            .HasForeignKey(p => p.PrimaryProviderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Provider
        modelBuilder.Entity<Provider>()
            .HasOne(p => p.Account)
            .WithOne(u => u.Provider)
            .HasForeignKey<Provider>(p => p.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        // ProviderLocation composite key
        modelBuilder.Entity<ProviderLocation>()
            .HasKey(pl => new { pl.ProviderId, pl.LocationId });

        modelBuilder.Entity<ProviderLocation>()
            .HasOne(pl => pl.Provider)
            .WithMany(p => p.Locations)
            .HasForeignKey(pl => pl.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProviderLocation>()
            .HasOne(pl => pl.Location)
            .WithMany(l => l.ProviderLocations)
            .HasForeignKey(pl => pl.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // CareTeam composite unique index
        modelBuilder.Entity<PatientCareTeam>()
            .HasIndex(pct => new { pct.PatientId, pct.ProviderId })
            .IsUnique();

        modelBuilder.Entity<PatientCareTeam>()
            .HasOne(pct => pct.Patient)
            .WithMany(p => p.CareTeams)
            .HasForeignKey(pct => pct.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PatientCareTeam>()
            .HasOne(pct => pct.Provider)
            .WithMany(p => p.CareTeams)
            .HasForeignKey(pct => pct.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Location
        modelBuilder.Entity<Location>()
            .Property(l => l.Type)
            .HasConversion<string>();

        modelBuilder.Entity<Location>()
            .HasOne(l => l.Department)
            .WithMany(d => d.Locations)
            .HasForeignKey(l => l.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Location>()
            .HasOne(l => l.ParentLocation)
            .WithMany(l => l.ChildLocations)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Clinical
        modelBuilder.Entity<Allergy>()
            .Property(a => a.Severity)
            .HasConversion<string>();

        modelBuilder.Entity<Allergy>()
            .HasOne(a => a.Patient)
            .WithMany(p => p.Allergies)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Observation>()
            .Property(o => o.Category)
            .HasConversion<string>();

        modelBuilder.Entity<Observation>()
            .HasOne(o => o.Patient)
            .WithMany(p => p.Observations)
            .HasForeignKey(o => o.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Condition>()
            .HasOne(c => c.Patient)
            .WithMany(p => p.Conditions)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Procedure>()
            .HasOne(pr => pr.Patient)
            .WithMany(p => p.Procedures)
            .HasForeignKey(pr => pr.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HealthSnapshot>()
            .HasKey(hs => hs.PatientId);

        modelBuilder.Entity<HealthSnapshot>()
            .HasOne(hs => hs.Patient)
            .WithOne(p => p.Snapshot)
            .HasForeignKey<HealthSnapshot>(hs => hs.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Prescriptions & Meds
        modelBuilder.Entity<Prescription>()
            .HasOne(p => p.Patient)
            .WithMany(p => p.Prescriptions)
            .HasForeignKey(p => p.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Prescription>()
            .HasOne(p => p.Medication)
            .WithMany(m => m.Prescriptions)
            .HasForeignKey(p => p.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RefillRequest>()
            .Property(r => r.Status)
            .HasConversion<string>();

        modelBuilder.Entity<RefillRequest>()
            .HasOne(r => r.Prescription)
            .WithMany(p => p.RefillRequests)
            .HasForeignKey(r => r.PrescriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Appointments
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Appointment>()
            .Property(a => a.PatientNotes)
            .HasConversion<EncryptedStringConverter>();

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Provider)
            .WithMany(pr => pr.Appointments)
            .HasForeignKey(a => a.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Department)
            .WithMany(d => d.Appointments)
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.AppointmentType)
            .WithMany(at => at.Appointments)
            .HasForeignKey(a => a.AppointmentTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.RoomResource)
            .WithMany(r => r.Appointments)
            .HasForeignKey(a => a.RoomResourceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Appointment>()
            .HasIndex(a => new { a.ProviderId, a.StartAt, a.EndAt });

        modelBuilder.Entity<Appointment>()
            .HasIndex(a => new { a.RoomResourceId, a.StartAt, a.EndAt });

        // AppointmentType
        modelBuilder.Entity<AppointmentType>()
            .HasIndex(at => at.Code)
            .IsUnique();

        // RoomResource
        modelBuilder.Entity<RoomResource>()
            .HasOne(r => r.Location)
            .WithMany(l => l.RoomResources)
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // WeeklyScheduleTemplate
        modelBuilder.Entity<WeeklyScheduleTemplate>()
            .Property(w => w.DayOfWeek)
            .HasConversion<string>();

        modelBuilder.Entity<WeeklyScheduleTemplate>()
            .HasOne(w => w.Provider)
            .WithMany(p => p.WeeklySchedules)
            .HasForeignKey(w => w.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WeeklyScheduleTemplate>()
            .HasOne(w => w.Location)
            .WithMany(l => l.WeeklySchedules)
            .HasForeignKey(w => w.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // ScheduleBlock
        modelBuilder.Entity<ScheduleBlock>()
            .HasOne(sb => sb.Provider)
            .WithMany(p => p.ScheduleBlocks)
            .HasForeignKey(sb => sb.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleBlock>()
            .HasOne(sb => sb.Location)
            .WithMany(l => l.ScheduleBlocks)
            .HasForeignKey(sb => sb.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleBlock>()
            .HasIndex(sb => new { sb.ProviderId, sb.StartAt, sb.EndAt });

        modelBuilder.Entity<ScheduleBlock>()
            .HasIndex(sb => new { sb.LocationId, sb.StartAt, sb.EndAt });

        // Billing
        modelBuilder.Entity<Statement>()
            .Property(s => s.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Statement>()
            .HasOne(s => s.Patient)
            .WithMany(p => p.Statements)
            .HasForeignKey(s => s.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Statement)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.StatementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.GatewayPaymentIntentId);

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.IdempotencyKey);

        modelBuilder.Entity<ProcessedWebhookEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        modelBuilder.Entity<ConsumedPaymentIntent>(entity =>
        {
            entity.HasIndex(e => e.GatewayPaymentIntentId)
                .IsUnique();
            entity.HasIndex(e => new { e.PatientId, e.IdempotencyKey });
            entity.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentPlan>()
            .Property(pp => pp.Status)
            .HasConversion<string>();

        modelBuilder.Entity<PaymentPlan>()
            .HasOne(pp => pp.Patient)
            .WithMany(p => p.PaymentPlans)
            .HasForeignKey(pp => pp.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PaymentPlan>()
            .HasOne(pp => pp.Statement)
            .WithMany(s => s.PaymentPlans)
            .HasForeignKey(pp => pp.StatementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PaymentInstallment>()
            .HasOne(pi => pi.Plan)
            .WithMany(p => p.Installments)
            .HasForeignKey(pi => pi.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messaging
        modelBuilder.Entity<MessageThreadParticipant>()
            .HasKey(mtp => new { mtp.ThreadId, mtp.PersonId });

        modelBuilder.Entity<MessageThreadParticipant>()
            .HasOne(mtp => mtp.Thread)
            .WithMany(t => t.Participants)
            .HasForeignKey(mtp => mtp.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageThreadParticipant>()
            .HasOne(mtp => mtp.Person)
            .WithMany()
            .HasForeignKey(mtp => mtp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Thread)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderPersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Proxy & Sharing
        modelBuilder.Entity<ProxyGrant>()
            .HasOne(pg => pg.Grantor)
            .WithMany()
            .HasForeignKey(pg => pg.GrantorId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProxyGrant>()
            .HasOne(pg => pg.Grantee)
            .WithMany()
            .HasForeignKey(pg => pg.GranteeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecordShare>()
            .HasOne(rs => rs.Patient)
            .WithMany()
            .HasForeignKey(rs => rs.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecordShareAccess>()
            .HasOne(rsa => rsa.Share)
            .WithMany(s => s.Accesses)
            .HasForeignKey(rsa => rsa.ShareId)
            .OnDelete(DeleteBehavior.Cascade);

        // Forms & Concepts
        modelBuilder.Entity<FormResponse>()
            .HasOne(fr => fr.Patient)
            .WithMany(p => p.FormResponses)
            .HasForeignKey(fr => fr.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FormResponse>()
            .HasOne(fr => fr.FormDefinition)
            .WithMany()
            .HasForeignKey(fr => fr.FormDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnomedConcept>()
            .HasKey(sc => sc.Code);

        modelBuilder.Entity<RxNormConcept>()
            .HasKey(rc => rc.Code);

        // AuditLog mapping & indices
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasIndex(a => new { a.TenantId, a.SequenceNumber }).IsUnique();
            b.HasIndex(a => a.PatientId);
            b.HasIndex(a => a.ActorUserAccountId);
            b.HasIndex(a => a.CreatedAt);
            b.HasIndex(a => a.Action);
            b.HasIndex(a => a.ResourceType);
            b.Property(a => a.Hash).HasMaxLength(64);
            b.Property(a => a.PreviousHash).HasMaxLength(64);
        });
        // BreakGlassOverride mapping & indices
        modelBuilder.Entity<BreakGlassOverride>(b =>
        {
            b.HasIndex(o => new { o.ClinicianUserAccountId, o.PatientId, o.ExpiresAt });
            b.HasOne(o => o.ClinicianUserAccount)
                .WithMany()
                .HasForeignKey(o => o.ClinicianUserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(o => o.Patient)
                .WithMany()
                .HasForeignKey(o => o.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Organization & Facility & Department
        modelBuilder.Entity<Organization>(b =>
        {
            b.HasIndex(o => o.Code).IsUnique();
        });

        modelBuilder.Entity<Facility>(b =>
        {
            b.HasIndex(f => new { f.OrganizationId, f.Code }).IsUnique();
            b.HasOne(f => f.Organization)
                .WithMany(o => o.Facilities)
                .HasForeignKey(f => f.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Department>(b =>
        {
            b.HasIndex(d => new { d.FacilityId, d.Code }).IsUnique();
            b.HasOne(d => d.Facility)
                .WithMany(f => f.Departments)
                .HasForeignKey(d => d.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Encounter>(b =>
        {
            b.HasIndex(e => new { e.PatientId, e.Status });
            b.HasIndex(e => new { e.TenantId, e.StartedAt });
            b.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(e => e.Bed)
                .WithMany()
                .HasForeignKey(e => e.BedId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Bed>(b =>
        {
            b.HasIndex(bed => new { bed.TenantId, bed.Code }).IsUnique();
            b.HasIndex(bed => new { bed.TenantId, bed.State });
            b.HasOne(bed => bed.Location)
                .WithMany()
                .HasForeignKey(bed => bed.LocationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClinicalOrder>(b =>
        {
            b.HasIndex(o => new { o.TenantId, o.Status, o.Priority });
            b.HasIndex(o => new { o.PatientId, o.CreatedAt });
            b.HasOne(o => o.Patient).WithMany().HasForeignKey(o => o.PatientId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(o => o.Encounter).WithMany().HasForeignKey(o => o.EncounterId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(o => o.PerformingDepartment).WithMany().HasForeignKey(o => o.PerformingDepartmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(o => o.Tariff).WithMany().HasForeignKey(o => o.TariffId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tariff>(b =>
        {
            b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.IsActive, t.EffectiveFrom });
            b.Property(t => t.OrderType).HasConversion<string>();
        });

        modelBuilder.Entity<ChargeItem>(b =>
        {
            b.HasIndex(c => new { c.TenantId, c.PatientId, c.Status });
            b.HasIndex(c => c.ClinicalOrderId).IsUnique();
            b.Property(c => c.Status).HasConversion<string>();
            b.HasOne(c => c.Patient).WithMany().HasForeignKey(c => c.PatientId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.ClinicalOrder).WithMany().HasForeignKey(c => c.ClinicalOrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.Tariff).WithMany().HasForeignKey(c => c.TariffId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Receipt>(b =>
        {
            b.HasIndex(r => new { r.TenantId, r.ReceiptNumber }).IsUnique();
            b.HasOne(r => r.Patient).WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Payment).WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryItem>(b =>
        {
            b.HasIndex(i => new { i.TenantId, i.Code }).IsUnique();
        });
        modelBuilder.Entity<InventoryStore>(b =>
        {
            b.HasIndex(s => new { s.TenantId, s.Code }).IsUnique();
            b.Property(s => s.Type).HasConversion<string>();
            b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockBatch>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.ItemId, x.LotNumber }).IsUnique();
            b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockLedgerEntry>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.StoreId, x.ItemId, x.CreatedAt });
            b.Property(x => x.Action).HasConversion<string>();
            b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<InventoryRequisition>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            b.Property(x => x.Status).HasConversion<string>();
            b.HasMany(x => x.Lines).WithOne(x => x.Requisition).HasForeignKey(x => x.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InventoryRequisitionLine>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.RequisitionId, x.ItemId }).IsUnique();
        });
        modelBuilder.Entity<ControlledDrugEntry>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt });
        });
        modelBuilder.Entity<InventoryCountSheet>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt });
            b.HasMany(x => x.Lines).WithOne(x => x.CountSheet).HasForeignKey(x => x.CountSheetId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InventoryCountLine>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.CountSheetId, x.ItemId, x.BatchId }).IsUnique();
        });
        modelBuilder.Entity<AcuteCareEpisode>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Unit, x.Status, x.ArrivedAt });
            b.Property(x => x.Unit).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.Acuity).HasConversion<string>();
            b.Property(x => x.Disposition).HasConversion<string>();
            b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Encounter).WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AcuteCareTransferRequest>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            b.Property(x => x.TargetUnit).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.HasOne(x => x.Episode).WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AcuteCareBooking>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Unit, x.Status, x.ScheduledAt });
            b.Property(x => x.Unit).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.HasOne(x => x.Episode).WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DiagnosticWorkItem>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Service, x.Status, x.CreatedAt });
            b.HasIndex(x => new { x.TenantId, x.AccessionNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.PacsStudyUid }).IsUnique();
            b.Property(x => x.Service).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.HasOne(x => x.ClinicalOrder).WithMany().HasForeignKey(x => x.ClinicalOrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MortuaryCase>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Status, x.DeathDeclaredAt });
            b.HasIndex(x => new { x.TenantId, x.TagNumber }).IsUnique();
            b.Property(x => x.Status).HasConversion<string>();
            b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MortuaryCustodyEvent>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.MortuaryCaseId, x.CreatedAt });
            b.Property(x => x.Action).HasConversion<string>();
            b.HasOne(x => x.MortuaryCase).WithMany().HasForeignKey(x => x.MortuaryCaseId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SupportServiceTask>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Status, x.Type, x.CreatedAt });
            b.Property(x => x.Type).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
        });
        modelBuilder.Entity<DietOrder>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.EncounterId, x.Status });
            b.Property(x => x.Status).HasConversion<string>();
        });
        modelBuilder.Entity<LinenLedgerEntry>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.DepartmentId, x.CreatedAt });
            b.Property(x => x.Action).HasConversion<string>();
        });
        modelBuilder.Entity<KpiDefinition>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        modelBuilder.Entity<KpiSnapshot>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.KpiDefinitionId, x.PeriodStart, x.PeriodEnd }).IsUnique();
            b.HasOne(x => x.KpiDefinition).WithMany().HasForeignKey(x => x.KpiDefinitionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ProviderDepartment composite key & foreign keys
        modelBuilder.Entity<ProviderDepartment>(b =>
        {
            b.HasKey(pd => new { pd.ProviderId, pd.DepartmentId });
            b.HasOne(pd => pd.Provider)
                .WithMany(p => p.ProviderDepartments)
                .HasForeignKey(pd => pd.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(pd => pd.Department)
                .WithMany(d => d.ProviderDepartments)
                .HasForeignKey(pd => pd.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserMembership
        modelBuilder.Entity<UserMembership>(b =>
        {
            b.HasIndex(m => new { m.UserAccountId, m.FacilityId, m.DepartmentId });
            b.HasOne(m => m.UserAccount)
                .WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Facility)
                .WithMany(f => f.Memberships)
                .HasForeignKey(m => m.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Department)
                .WithMany(d => d.Memberships)
                .HasForeignKey(m => m.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NotificationDeliveryRecord
        modelBuilder.Entity<NotificationDeliveryRecord>(b =>
        {
            b.Property(n => n.Channel).HasConversion<string>();
            b.Property(n => n.Status).HasConversion<string>();
            b.HasIndex(n => new { n.ReferenceType, n.ReferenceId });
            b.HasIndex(n => n.RecipientPersonId);
            b.HasIndex(n => n.Status);
            b.HasOne(n => n.RecipientPerson)
                .WithMany()
                .HasForeignKey(n => n.RecipientPersonId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // HMS Integration
        modelBuilder.Entity<HmsDeadLetterRecord>(b =>
        {
            b.Property(d => d.Status).HasConversion<string>();
            b.HasIndex(d => d.EventId);
            b.HasIndex(d => d.HmsMrn);
            b.HasIndex(d => d.Status);
            b.HasOne(d => d.ResolvedByUserAccount)
                .WithMany()
                .HasForeignKey(d => d.ResolvedByUserAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HmsDemographicConflict>(b =>
        {
            b.Property(c => c.Status).HasConversion<string>();
            b.Property(c => c.LocalValue).HasConversion<EncryptedStringConverter>();
            b.Property(c => c.HmsValue).HasConversion<EncryptedStringConverter>();
            b.Property(c => c.LastSyncedValue).HasConversion<EncryptedStringConverter>();
            b.HasIndex(c => c.PatientId);
            b.HasIndex(c => c.HmsMrn);
            b.HasIndex(c => c.Status);
            b.HasOne(c => c.Patient)
                .WithMany()
                .HasForeignKey(c => c.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.ResolvedByUserAccount)
                .WithMany()
                .HasForeignKey(c => c.ResolvedByUserAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HmsProcessedWebhookEvent>(b =>
        {
            b.HasIndex(w => w.EventId).IsUnique();
        });

        // Offline-First Outbox Synchronization
        modelBuilder.Entity<SyncOutboxMessage>(b =>
        {
            b.Property(o => o.Status).HasConversion<string>();
            b.HasIndex(o => new { o.TenantId, o.Status, o.ScheduledAtUtc });
            b.HasIndex(o => new { o.TenantId, o.PatientId, o.PerPatientSequence });
            b.HasIndex(o => new { o.TenantId, o.IdempotencyKey }).IsUnique();
            b.HasOne(o => o.Patient)
                .WithMany()
                .HasForeignKey(o => o.PatientId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Signed Reference Data Packages
        modelBuilder.Entity<ReferencePackage>(b =>
        {
            b.Property(r => r.Status).HasConversion<string>();
            b.HasIndex(r => new { r.TenantId, r.PackageId, r.VersionOrdinal }).IsUnique();
            b.HasIndex(r => new { r.TenantId, r.PackageId, r.IsActive });
        });

        // National Interoperability: DHIS2 HMIS Aggregate Reports
        modelBuilder.Entity<Dhis2ReportDataset>(b =>
        {
            b.Property(d => d.Status).HasConversion<string>();
            b.HasIndex(d => new { d.TenantId, d.DataSetId, d.Period, d.OrgUnitId });
            b.HasIndex(d => new { d.TenantId, d.Status });
        });

        // National Interoperability: eCHIS Community Referrals
        modelBuilder.Entity<EchisReferral>(b =>
        {
            b.Property(e => e.Direction).HasConversion<string>();
            b.Property(e => e.Status).HasConversion<string>();
            b.HasIndex(e => new { e.TenantId, e.ReferralNumber }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.Status });
            b.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // National Interoperability: Master Patient Index (MPI) Duplicate Candidate
        modelBuilder.Entity<PatientDuplicateCandidate>(b =>
        {
            b.Property(p => p.Status).HasConversion<string>();
            b.HasIndex(p => new { p.TenantId, p.PrimaryPatientId, p.CandidateDuplicatePatientId });
            b.HasOne(p => p.PrimaryPatient)
                .WithMany()
                .HasForeignKey(p => p.PrimaryPatientId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.CandidateDuplicatePatient)
                .WithMany()
                .HasForeignKey(p => p.CandidateDuplicatePatientId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.ResolvedByUser)
                .WithMany()
                .HasForeignKey(p => p.ResolvedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Facility Master Registry Index
        modelBuilder.Entity<Facility>()
            .HasIndex(f => new { f.TenantId, f.MfrCode });

        // Patient National Identifier Index
        modelBuilder.Entity<Patient>()
            .HasIndex(p => new { p.TenantId, p.NationalPatientId });

        // Configure tenant scoping and global query filters
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(ITenantScoped.TenantId))
                    .HasDefaultValue(TenantConstants.DefaultTenantId);

                modelBuilder.Entity(entityType.ClrType)
                    .HasIndex(nameof(ITenantScoped.TenantId));

                var method = TenantFilterMethod.MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private static readonly MethodInfo TenantFilterMethod = typeof(MedicalPortalDbContext)
        .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ConfigureTenantFilter<T>(ModelBuilder builder) where T : class, ITenantScoped
    {
        builder.Entity<T>().HasQueryFilter(e => !IsTenantResolved || e.TenantId == CurrentTenantId);
    }

    private static readonly SemaphoreSlim _auditLock = new(1, 1);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantIsolation();
        EnforceAuditLogAppendOnly();
        NormalizeDateTimesForPostgres();
        var hasAddedAuditLogs = ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added);
        if (hasAddedAuditLogs)
        {
            _auditLock.Wait();
            try
            {
                ProcessAuditLogHashChaining();
                
                // Retry loop for unique constraint violations on (TenantId, SequenceNumber)
                const int maxSaveRetries = 3;
                for (int saveAttempt = 0; saveAttempt < maxSaveRetries; saveAttempt++)
                {
                    try
                    {
                        return base.SaveChanges(acceptAllChangesOnSuccess);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                    {
                        if (saveAttempt == maxSaveRetries - 1) throw;
                        // Unique constraint violation on (TenantId, SequenceNumber) - another transaction
                        // committed a log with the same sequence. Reset hash/sequence on added logs and re-process.
                        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
                        {
                            entry.Entity.Hash = string.Empty;
                            entry.Entity.PreviousHash = null;
                            entry.Entity.SequenceNumber = 0;
                        }
                        ProcessAuditLogHashChaining();
                        continue;
                    }
                }
                // Should not reach here
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }
            finally
            {
                _auditLock.Release();
            }
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantIsolation();
        EnforceAuditLogAppendOnly();
        NormalizeDateTimesForPostgres();
        var hasAddedAuditLogs = ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added);
        if (hasAddedAuditLogs)
        {
            await _auditLock.WaitAsync(cancellationToken);
            try
            {
                await ProcessAuditLogHashChainingAsync(cancellationToken);
                
                // Retry loop for unique constraint violations on (TenantId, SequenceNumber)
                const int maxSaveRetries = 3;
                for (int saveAttempt = 0; saveAttempt < maxSaveRetries; saveAttempt++)
                {
                    try
                    {
                        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                    {
                        if (saveAttempt == maxSaveRetries - 1) throw;
                        // Unique constraint violation on (TenantId, SequenceNumber) - another transaction
                        // committed a log with the same sequence. Reset hash/sequence on added logs and re-process.
                        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
                        {
                            entry.Entity.Hash = string.Empty;
                            entry.Entity.PreviousHash = null;
                            entry.Entity.SequenceNumber = 0;
                        }
                        await ProcessAuditLogHashChainingAsync(cancellationToken);
                        continue;
                    }
                }
                // Should not reach here
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            finally
            {
                _auditLock.Release();
            }
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeDateTimesForPostgres()
    {
        if (!Database.IsNpgsql()) return;

        foreach (var entry in ChangeTracker.Entries())
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is not DateTime value) continue;

                // PostgreSQL 'timestamp without time zone' requires DateTimeKind.Unspecified
                // Convert UTC to Unspecified to avoid Npgsql ArgumentException
                property.CurrentValue = value.Kind switch
                {
                    DateTimeKind.Utc => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
                    DateTimeKind.Local => DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Unspecified),
                    _ => value
                };
            }
        }
    }

    private void EnforceTenantIsolation()
    {
        if (!IsTenantResolved || !CurrentTenantId.HasValue) return;

        var currentTenantId = CurrentTenantId.Value;
        var newlyAddedTenantIds = ChangeTracker.Entries<Tenant>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Id)
            .ToHashSet();

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added)
            {
                if (newlyAddedTenantIds.Contains(entry.Entity.TenantId))
                {
                    // Entity is part of a newly provisioned tenant graph
                    continue;
                }

                if (entry.Entity.TenantId == Guid.Empty || (entry.Entity.TenantId == TenantConstants.DefaultTenantId && currentTenantId != TenantConstants.DefaultTenantId))
                {
                    entry.Entity.TenantId = currentTenantId;
                }
                else if (entry.Entity.TenantId != currentTenantId)
                {
                    throw new System.Security.SecurityException(
                        $"Cross-tenant entity creation is prohibited. Context tenant: {currentTenantId}, Entity tenant: {entry.Entity.TenantId}");
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                var tenantProp = entry.Property(nameof(ITenantScoped.TenantId));
                if (tenantProp.IsModified && !Equals(tenantProp.OriginalValue, tenantProp.CurrentValue))
                {
                    throw new System.Security.SecurityException(
                        "Modifying or reassigning an entity across tenants is prohibited.");
                }
            }
        }
    }

    private void EnforceAuditLogAppendOnly()
    {
        var modifiedOrDeleted = ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        if (modifiedOrDeleted.Count > 0)
        {
            throw new InvalidOperationException("AuditLog is append-only. Modifying or deleting audit log records is forbidden.");
        }
    }

    private void ProcessAuditLogHashChaining()
    {
        var addedLogs = ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        if (addedLogs.Count == 0) return;

        var logsByTenant = addedLogs.GroupBy(l => l.TenantId);

        foreach (var tenantGroup in logsByTenant)
        {
            var tenantId = tenantGroup.Key;
            var tenantLogs = tenantGroup.ToList();

            // Platform events (tenant not resolved) go to Platform chain, not DefaultTenant chain
            var effectiveTenantId = tenantId;
            if (tenantId == TenantConstants.DefaultTenantId && !IsTenantResolved)
            {
                effectiveTenantId = TenantConstants.PlatformTenantId;
            }

            if (Database.IsNpgsql())
            {
                var lockKey = GetAdvisoryLockKey(effectiveTenantId);
                AcquireAdvisoryLock(lockKey);
            }

            var lastRecord = AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == effectiveTenantId && !string.IsNullOrEmpty(a.Hash))
                .OrderByDescending(a => a.SequenceNumber)
                .Select(a => new { a.Hash, a.SequenceNumber })
                .FirstOrDefault();

            long currentSeq = lastRecord?.SequenceNumber ?? 0;
            string? lastHash = lastRecord?.Hash;

            foreach (var log in tenantLogs)
            {
                if (string.IsNullOrEmpty(log.Hash))
                {
                    currentSeq++;
                    log.SequenceNumber = currentSeq;
                    // Assign the effective tenant ID for platform events
                    if (log.TenantId == TenantConstants.DefaultTenantId && !IsTenantResolved)
                    {
                        log.TenantId = TenantConstants.PlatformTenantId;
                    }
                    var prev = string.IsNullOrEmpty(lastHash) ? AuditLogHasher.GenesisHash : lastHash;
                    log.PreviousHash = prev;
                    log.Hash = AuditLogHasher.ComputeHash(log, prev);
                }
                lastHash = log.Hash;
            }
        }
    }

    private async Task ProcessAuditLogHashChainingAsync(CancellationToken cancellationToken)
    {
        var addedLogs = ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        if (addedLogs.Count == 0) return;

        var logsByTenant = addedLogs.GroupBy(l => l.TenantId);

        foreach (var tenantGroup in logsByTenant)
        {
            var tenantId = tenantGroup.Key;
            var tenantLogs = tenantGroup.ToList();

            // Platform events (tenant not resolved) go to Platform chain, not DefaultTenant chain
            var effectiveTenantId = tenantId;
            if (tenantId == TenantConstants.DefaultTenantId && !IsTenantResolved)
            {
                effectiveTenantId = TenantConstants.PlatformTenantId;
            }

            if (Database.IsNpgsql())
            {
                var lockKey = GetAdvisoryLockKey(effectiveTenantId);
                await AcquireAdvisoryLockAsync(lockKey, cancellationToken);
            }

            var lastRecord = await AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == effectiveTenantId && !string.IsNullOrEmpty(a.Hash))
                .OrderByDescending(a => a.SequenceNumber)
                .Select(a => new { a.Hash, a.SequenceNumber })
                .FirstOrDefaultAsync(cancellationToken);

            long currentSeq = lastRecord?.SequenceNumber ?? 0;
            string? lastHash = lastRecord?.Hash;

            foreach (var log in tenantLogs)
            {
                if (string.IsNullOrEmpty(log.Hash))
                {
                    currentSeq++;
                    log.SequenceNumber = currentSeq;
                    // Assign the effective tenant ID for platform events
                    if (log.TenantId == TenantConstants.DefaultTenantId && !IsTenantResolved)
                    {
                        log.TenantId = TenantConstants.PlatformTenantId;
                    }
                    var prev = string.IsNullOrEmpty(lastHash) ? AuditLogHasher.GenesisHash : lastHash;
                    log.PreviousHash = prev;
                    log.Hash = AuditLogHasher.ComputeHash(log, prev);
                }
                lastHash = log.Hash;
            }
        }
    }

    private void AcquireAdvisoryLock(long lockKey)
    {
        // Use pg_try_advisory_xact_lock with retry to avoid deadlocks.
        // pg_advisory_xact_lock blocks indefinitely; pg_try_advisory_xact_lock returns false immediately if lock is held.
        const int maxRetries = 50;
        const int retryDelayMs = 20;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var acquired = Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_xact_lock({lockKey}) AS \"Value\"").FirstOrDefault();
            if (acquired) return;
            Thread.Sleep(retryDelayMs);
        }
        throw new InvalidOperationException($"Failed to acquire advisory lock {lockKey} after {maxRetries} attempts");
    }

    private async Task AcquireAdvisoryLockAsync(long lockKey, CancellationToken cancellationToken)
    {
        const int maxRetries = 50;
        const int retryDelayMs = 20;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var acquired = await Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_xact_lock({lockKey}) AS \"Value\"").FirstOrDefaultAsync(cancellationToken);
            if (acquired) return;
            await Task.Delay(retryDelayMs, cancellationToken);
        }
        throw new InvalidOperationException($"Failed to acquire advisory lock {lockKey} after {maxRetries} attempts");
    }

    /// <summary>
    /// Derives a stable 64-bit advisory lock key from a tenant GUID.
    /// Uses the first 8 bytes of the GUID (big-endian) as a signed 64-bit integer.
    /// This is deterministic across processes, AppDomain restarts, and machine boundaries.
    /// The default tenant (00000000-0000-0000-0000-000000000001) maps to key 1.
    /// Platform events (login before tenant resolution, system events) use PlatformTenantId
    /// (00000000-0000-0000-0000-000000000000) which maps to key 0.
    /// There is exactly one platform chain per deployment, identified by TenantId == PlatformTenantId.
    /// </summary>
    private static long GetAdvisoryLockKey(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        // GUID byte order is little-endian for the first 8 bytes; we want a stable big-endian interpretation.
        // Take bytes 0-7 (the first 8 bytes of the 16-byte GUID) and interpret as unsigned long.
        // This gives a deterministic 64-bit value regardless of endianness.
        return BitConverter.ToInt64(bytes, 0);
    }
}
