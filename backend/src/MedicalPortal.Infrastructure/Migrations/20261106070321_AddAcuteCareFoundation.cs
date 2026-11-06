using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcuteCareFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcuteCareEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Acuity = table.Column<string>(type: "text", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TriagedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ChiefComplaint = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuteCareEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuteCareEpisodes_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcuteCareEpisodes_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcuteCareBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuteCareBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuteCareBookings_AcuteCareEpisodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "AcuteCareEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcuteCareTransferRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUnit = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcuteCareTransferRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcuteCareTransferRequests_AcuteCareEpisodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "AcuteCareEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareBookings_EpisodeId",
                table: "AcuteCareBookings",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareBookings_TenantId",
                table: "AcuteCareBookings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareBookings_TenantId_Unit_Status_ScheduledAt",
                table: "AcuteCareBookings",
                columns: new[] { "TenantId", "Unit", "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareEpisodes_EncounterId",
                table: "AcuteCareEpisodes",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareEpisodes_PatientId",
                table: "AcuteCareEpisodes",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareEpisodes_TenantId",
                table: "AcuteCareEpisodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareEpisodes_TenantId_Unit_Status_ArrivedAt",
                table: "AcuteCareEpisodes",
                columns: new[] { "TenantId", "Unit", "Status", "ArrivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareTransferRequests_EpisodeId",
                table: "AcuteCareTransferRequests",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareTransferRequests_TenantId",
                table: "AcuteCareTransferRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AcuteCareTransferRequests_TenantId_Status_CreatedAt",
                table: "AcuteCareTransferRequests",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcuteCareBookings");

            migrationBuilder.DropTable(
                name: "AcuteCareTransferRequests");

            migrationBuilder.DropTable(
                name: "AcuteCareEpisodes");
        }
    }
}
