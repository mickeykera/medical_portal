using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDietAndLinenSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DietOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DietType = table.Column<string>(type: "text", nullable: false),
                    Npo = table.Column<bool>(type: "boolean", nullable: false),
                    AllergiesAndRestrictions = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OrderedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinenLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinenItem = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    RecordedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinenLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DietOrders_TenantId",
                table: "DietOrders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DietOrders_TenantId_EncounterId_Status",
                table: "DietOrders",
                columns: new[] { "TenantId", "EncounterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LinenLedgerEntries_TenantId",
                table: "LinenLedgerEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LinenLedgerEntries_TenantId_DepartmentId_CreatedAt",
                table: "LinenLedgerEntries",
                columns: new[] { "TenantId", "DepartmentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DietOrders");

            migrationBuilder.DropTable(
                name: "LinenLedgerEntries");
        }
    }
}
