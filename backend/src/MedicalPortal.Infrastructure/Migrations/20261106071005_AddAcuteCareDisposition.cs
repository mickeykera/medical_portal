using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcuteCareDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                table: "AcuteCareEpisodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispositionReason",
                table: "AcuteCareEpisodes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "AcuteCareEpisodes");

            migrationBuilder.DropColumn(
                name: "DispositionReason",
                table: "AcuteCareEpisodes");
        }
    }
}
