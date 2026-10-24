using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNationalInteroperability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacilityMrnUri",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NationalPatientId",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalPatientIdUri",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FacilityType",
                table: "Facilities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfrCode",
                table: "Facilities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfrLastSyncedAtUtc",
                table: "Facilities",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalStatus",
                table: "Facilities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Facilities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Woreda",
                table: "Facilities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Zone",
                table: "Facilities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Dhis2Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    DataSetId = table.Column<string>(type: "text", nullable: false),
                    Period = table.Column<string>(type: "text", nullable: false),
                    OrgUnitId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DataValuesJson = table.Column<string>(type: "text", nullable: false),
                    DryRunSummaryJson = table.Column<string>(type: "text", nullable: true),
                    ImportSummaryJson = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExportPayloadJson = table.Column<string>(type: "text", nullable: true),
                    ExportSignature = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dhis2Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EchisReferrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    ReferralNumber = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    NationalPatientId = table.Column<string>(type: "text", nullable: true),
                    PatientFullName = table.Column<string>(type: "text", nullable: false),
                    PatientPhone = table.Column<string>(type: "text", nullable: false),
                    ReferringUnit = table.Column<string>(type: "text", nullable: false),
                    ReceivingUnit = table.Column<string>(type: "text", nullable: false),
                    ReasonForReferral = table.Column<string>(type: "text", nullable: false),
                    Urgency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ClinicalSummaryJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TriagedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TriageNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EchisReferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EchisReferrals_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PatientDuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    PrimaryPatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateDuplicatePatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    MatchReasonsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDuplicateCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateCandidates_Patients_CandidateDuplicatePatie~",
                        column: x => x.CandidateDuplicatePatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateCandidates_Patients_PrimaryPatientId",
                        column: x => x.PrimaryPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateCandidates_UserAccounts_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_NationalPatientId",
                table: "Patients",
                columns: new[] { "TenantId", "NationalPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_TenantId_MfrCode",
                table: "Facilities",
                columns: new[] { "TenantId", "MfrCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Dhis2Reports_TenantId",
                table: "Dhis2Reports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Dhis2Reports_TenantId_DataSetId_Period_OrgUnitId",
                table: "Dhis2Reports",
                columns: new[] { "TenantId", "DataSetId", "Period", "OrgUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Dhis2Reports_TenantId_Status",
                table: "Dhis2Reports",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EchisReferrals_PatientId",
                table: "EchisReferrals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_EchisReferrals_TenantId",
                table: "EchisReferrals",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EchisReferrals_TenantId_ReferralNumber",
                table: "EchisReferrals",
                columns: new[] { "TenantId", "ReferralNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EchisReferrals_TenantId_Status",
                table: "EchisReferrals",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateCandidates_CandidateDuplicatePatientId",
                table: "PatientDuplicateCandidates",
                column: "CandidateDuplicatePatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateCandidates_PrimaryPatientId",
                table: "PatientDuplicateCandidates",
                column: "PrimaryPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateCandidates_ResolvedByUserId",
                table: "PatientDuplicateCandidates",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateCandidates_TenantId",
                table: "PatientDuplicateCandidates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateCandidates_TenantId_PrimaryPatientId_Candid~",
                table: "PatientDuplicateCandidates",
                columns: new[] { "TenantId", "PrimaryPatientId", "CandidateDuplicatePatientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dhis2Reports");

            migrationBuilder.DropTable(
                name: "EchisReferrals");

            migrationBuilder.DropTable(
                name: "PatientDuplicateCandidates");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_NationalPatientId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_TenantId_MfrCode",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "FacilityMrnUri",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "NationalPatientId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "NationalPatientIdUri",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "FacilityType",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "MfrCode",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "MfrLastSyncedAtUtc",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "OperationalStatus",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "Woreda",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "Zone",
                table: "Facilities");
        }
    }
}
