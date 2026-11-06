using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderingUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformingDepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentGate = table.Column<int>(type: "integer", nullable: false),
                    ClinicalIndication = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ChargeAmountCents = table.Column<int>(type: "integer", nullable: true),
                    GateExceptionReason = table.Column<string>(type: "text", nullable: true),
                    GateApprovedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalOrders_Departments_PerformingDepartmentId",
                        column: x => x.PerformingDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalOrders_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalOrders_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOrders_EncounterId",
                table: "ClinicalOrders",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOrders_PatientId_CreatedAt",
                table: "ClinicalOrders",
                columns: new[] { "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOrders_PerformingDepartmentId",
                table: "ClinicalOrders",
                column: "PerformingDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOrders_TenantId",
                table: "ClinicalOrders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOrders_TenantId_Status_Priority",
                table: "ClinicalOrders",
                columns: new[] { "TenantId", "Status", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalOrders");
        }
    }
}
