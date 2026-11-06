using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticSafetyBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PacsStudyUid",
                table: "DiagnosticWorkItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PregnancySafetyAcknowledged",
                table: "DiagnosticWorkItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticWorkItems_TenantId_PacsStudyUid",
                table: "DiagnosticWorkItems",
                columns: new[] { "TenantId", "PacsStudyUid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiagnosticWorkItems_TenantId_PacsStudyUid",
                table: "DiagnosticWorkItems");

            migrationBuilder.DropColumn(
                name: "PacsStudyUid",
                table: "DiagnosticWorkItems");

            migrationBuilder.DropColumn(
                name: "PregnancySafetyAcknowledged",
                table: "DiagnosticWorkItems");
        }
    }
}
