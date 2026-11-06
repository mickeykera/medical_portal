using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    ClinicalOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AccessionNumber = table.Column<string>(type: "text", nullable: true),
                    SpecimenType = table.Column<string>(type: "text", nullable: true),
                    Modality = table.Column<string>(type: "text", nullable: true),
                    ResultText = table.Column<string>(type: "text", nullable: true),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    CriticalCallbackNote = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticWorkItems_ClinicalOrders_ClinicalOrderId",
                        column: x => x.ClinicalOrderId,
                        principalTable: "ClinicalOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiagnosticWorkItems_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_ClinicalOrderId",
                table: "DiagnosticWorkItems",
                column: "ClinicalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_PatientId",
                table: "DiagnosticWorkItems",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_TenantId",
                table: "DiagnosticWorkItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_TenantId_AccessionNumber",
                table: "DiagnosticWorkItems",
                columns: new[] { "TenantId", "AccessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_TenantId_Service_Status_CreatedAt",
                table: "DiagnosticWorkItems",
                columns: new[] { "TenantId", "Service", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticWorkItems");
        }
    }
}
