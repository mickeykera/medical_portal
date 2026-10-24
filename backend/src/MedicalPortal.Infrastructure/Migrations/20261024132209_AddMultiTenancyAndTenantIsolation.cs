using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancyAndTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_Email",
                table: "UserAccounts");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "WeeklyScheduleTemplates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "UserSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "UserMemberships",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "UserAccounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Statements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ScheduleBlocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RoomResources",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RefillRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RecordShares",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RecordShareAccesses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProxyInvites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProxyGrants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Providers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProviderLocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProviderDepartments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProcessedWebhookEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Procedures",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Prescriptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Persons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PaymentPlans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PaymentInstallments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Patients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Organizations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Observations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NotificationPreferences",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NotificationDeliveryRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "MessageThreads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "MessageThreadParticipants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Medications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Locations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "HmsProcessedWebhookEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "HmsDemographicConflicts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "HmsDeadLetterRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "HealthSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FormResponses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FormDefinitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Facilities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Departments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ConsumedPaymentIntents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Conditions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CareTeams",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BreakGlassOverrides",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AppointmentTypes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Allergies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    HostDomain = table.Column<string>(type: "text", nullable: true),
                    Region = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    LanguagesJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    MasterKeyFingerprint = table.Column<string>(type: "text", nullable: false),
                    AuditChainHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleTemplates_TenantId",
                table: "WeeklyScheduleTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TenantId",
                table: "UserSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMemberships_TenantId",
                table: "UserMemberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_TenantId",
                table: "UserAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_TenantId_Email",
                table: "UserAccounts",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Statements_TenantId",
                table: "Statements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_TenantId",
                table: "ScheduleBlocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomResources_TenantId",
                table: "RoomResources",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RefillRequests_TenantId",
                table: "RefillRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordShares_TenantId",
                table: "RecordShares",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordShareAccesses_TenantId",
                table: "RecordShareAccesses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyInvites_TenantId",
                table: "ProxyInvites",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyGrants_TenantId",
                table: "ProxyGrants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Providers_TenantId",
                table: "Providers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderLocations_TenantId",
                table: "ProviderLocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDepartments_TenantId",
                table: "ProviderDepartments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_TenantId",
                table: "ProcessedWebhookEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Procedures_TenantId",
                table: "Procedures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId",
                table: "Prescriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId",
                table: "Persons",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_TenantId",
                table: "PaymentPlans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstallments_TenantId",
                table: "PaymentInstallments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId",
                table: "Patients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_TenantId",
                table: "Organizations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TenantId",
                table: "Observations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId",
                table: "NotificationPreferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryRecords_TenantId",
                table: "NotificationDeliveryRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreads_TenantId",
                table: "MessageThreads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreadParticipants_TenantId",
                table: "MessageThreadParticipants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TenantId",
                table: "Messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Medications_TenantId",
                table: "Medications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId",
                table: "Locations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsProcessedWebhookEvents_TenantId",
                table: "HmsProcessedWebhookEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDemographicConflicts_TenantId",
                table: "HmsDemographicConflicts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDeadLetterRecords_TenantId",
                table: "HmsDeadLetterRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_TenantId",
                table: "HealthSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FormResponses_TenantId",
                table: "FormResponses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_TenantId",
                table: "FormDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_TenantId",
                table: "Facilities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId",
                table: "Departments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumedPaymentIntents_TenantId",
                table: "ConsumedPaymentIntents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Conditions_TenantId",
                table: "Conditions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CareTeams_TenantId",
                table: "CareTeams",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BreakGlassOverrides_TenantId",
                table: "BreakGlassOverrides",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_TenantId",
                table: "AppointmentTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId",
                table: "Appointments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Allergies_TenantId",
                table: "Allergies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyScheduleTemplates_TenantId",
                table: "WeeklyScheduleTemplates");

            migrationBuilder.DropIndex(
                name: "IX_UserSessions_TenantId",
                table: "UserSessions");

            migrationBuilder.DropIndex(
                name: "IX_UserMemberships_TenantId",
                table: "UserMemberships");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_TenantId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_TenantId_Email",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Statements_TenantId",
                table: "Statements");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleBlocks_TenantId",
                table: "ScheduleBlocks");

            migrationBuilder.DropIndex(
                name: "IX_RoomResources_TenantId",
                table: "RoomResources");

            migrationBuilder.DropIndex(
                name: "IX_RefillRequests_TenantId",
                table: "RefillRequests");

            migrationBuilder.DropIndex(
                name: "IX_RecordShares_TenantId",
                table: "RecordShares");

            migrationBuilder.DropIndex(
                name: "IX_RecordShareAccesses_TenantId",
                table: "RecordShareAccesses");

            migrationBuilder.DropIndex(
                name: "IX_ProxyInvites_TenantId",
                table: "ProxyInvites");

            migrationBuilder.DropIndex(
                name: "IX_ProxyGrants_TenantId",
                table: "ProxyGrants");

            migrationBuilder.DropIndex(
                name: "IX_Providers_TenantId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_ProviderLocations_TenantId",
                table: "ProviderLocations");

            migrationBuilder.DropIndex(
                name: "IX_ProviderDepartments_TenantId",
                table: "ProviderDepartments");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedWebhookEvents_TenantId",
                table: "ProcessedWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Procedures_TenantId",
                table: "Procedures");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_TenantId",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentPlans_TenantId",
                table: "PaymentPlans");

            migrationBuilder.DropIndex(
                name: "IX_PaymentInstallments_TenantId",
                table: "PaymentInstallments");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_TenantId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Observations_TenantId",
                table: "Observations");

            migrationBuilder.DropIndex(
                name: "IX_NotificationPreferences_TenantId",
                table: "NotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveryRecords_TenantId",
                table: "NotificationDeliveryRecords");

            migrationBuilder.DropIndex(
                name: "IX_MessageThreads_TenantId",
                table: "MessageThreads");

            migrationBuilder.DropIndex(
                name: "IX_MessageThreadParticipants_TenantId",
                table: "MessageThreadParticipants");

            migrationBuilder.DropIndex(
                name: "IX_Messages_TenantId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Medications_TenantId",
                table: "Medications");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_HmsProcessedWebhookEvents_TenantId",
                table: "HmsProcessedWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_HmsDemographicConflicts_TenantId",
                table: "HmsDemographicConflicts");

            migrationBuilder.DropIndex(
                name: "IX_HmsDeadLetterRecords_TenantId",
                table: "HmsDeadLetterRecords");

            migrationBuilder.DropIndex(
                name: "IX_HealthSnapshots_TenantId",
                table: "HealthSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_FormResponses_TenantId",
                table: "FormResponses");

            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_TenantId",
                table: "FormDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_TenantId",
                table: "Facilities");

            migrationBuilder.DropIndex(
                name: "IX_Departments_TenantId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_ConsumedPaymentIntents_TenantId",
                table: "ConsumedPaymentIntents");

            migrationBuilder.DropIndex(
                name: "IX_Conditions_TenantId",
                table: "Conditions");

            migrationBuilder.DropIndex(
                name: "IX_CareTeams_TenantId",
                table: "CareTeams");

            migrationBuilder.DropIndex(
                name: "IX_BreakGlassOverrides_TenantId",
                table: "BreakGlassOverrides");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentTypes_TenantId",
                table: "AppointmentTypes");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Allergies_TenantId",
                table: "Allergies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WeeklyScheduleTemplates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserMemberships");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Statements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ScheduleBlocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RoomResources");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RefillRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RecordShares");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RecordShareAccesses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProxyInvites");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProxyGrants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProviderLocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProviderDepartments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProcessedWebhookEvents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PaymentInstallments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NotificationDeliveryRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MessageThreads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MessageThreadParticipants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Medications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HmsProcessedWebhookEvents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HmsDemographicConflicts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HmsDeadLetterRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HealthSnapshots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FormResponses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ConsumedPaymentIntents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Conditions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CareTeams");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BreakGlassOverrides");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppointmentTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Allergies");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Email",
                table: "UserAccounts",
                column: "Email",
                unique: true);
        }
    }
}
