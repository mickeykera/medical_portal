using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHmsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSyncedDemographicsJson",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HmsDeadLetterRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    HmsMrn = table.Column<string>(type: "text", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    FirstFailedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    ResolvedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmsDeadLetterRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HmsDeadLetterRecords_UserAccounts_ResolvedByUserAccountId",
                        column: x => x.ResolvedByUserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HmsDemographicConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    HmsMrn = table.Column<string>(type: "text", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    LocalValue = table.Column<string>(type: "text", nullable: true),
                    HmsValue = table.Column<string>(type: "text", nullable: true),
                    LastSyncedValue = table.Column<string>(type: "text", nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResolvedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmsDemographicConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HmsDemographicConflicts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HmsDemographicConflicts_UserAccounts_ResolvedByUserAccountId",
                        column: x => x.ResolvedByUserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HmsProcessedWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    HmsMrn = table.Column<string>(type: "text", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmsProcessedWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HmsDeadLetterRecords_EventId",
                table: "HmsDeadLetterRecords",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDeadLetterRecords_HmsMrn",
                table: "HmsDeadLetterRecords",
                column: "HmsMrn");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDeadLetterRecords_ResolvedByUserAccountId",
                table: "HmsDeadLetterRecords",
                column: "ResolvedByUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDeadLetterRecords_Status",
                table: "HmsDeadLetterRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDemographicConflicts_HmsMrn",
                table: "HmsDemographicConflicts",
                column: "HmsMrn");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDemographicConflicts_PatientId",
                table: "HmsDemographicConflicts",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDemographicConflicts_ResolvedByUserAccountId",
                table: "HmsDemographicConflicts",
                column: "ResolvedByUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_HmsDemographicConflicts_Status",
                table: "HmsDemographicConflicts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HmsProcessedWebhookEvents_EventId",
                table: "HmsProcessedWebhookEvents",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HmsDeadLetterRecords");

            migrationBuilder.DropTable(
                name: "HmsDemographicConflicts");

            migrationBuilder.DropTable(
                name: "HmsProcessedWebhookEvents");

            migrationBuilder.DropColumn(
                name: "LastSyncedDemographicsJson",
                table: "Patients");
        }
    }
}
