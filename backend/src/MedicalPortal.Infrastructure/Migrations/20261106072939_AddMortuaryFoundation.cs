using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMortuaryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MortuaryCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeathDeclaredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeclaredByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeathCertificateReference = table.Column<string>(type: "text", nullable: true),
                    TagNumber = table.Column<string>(type: "text", nullable: true),
                    StorageLocation = table.Column<string>(type: "text", nullable: true),
                    ForensicFlag = table.Column<bool>(type: "boolean", nullable: false),
                    InfectiousFlag = table.Column<bool>(type: "boolean", nullable: false),
                    ReleaseAuthorizationReference = table.Column<string>(type: "text", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortuaryCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MortuaryCases_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MortuaryCustodyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    MortuaryCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    FromLocation = table.Column<string>(type: "text", nullable: true),
                    ToLocation = table.Column<string>(type: "text", nullable: true),
                    IdentityVerificationReference = table.Column<string>(type: "text", nullable: true),
                    RecordedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortuaryCustodyEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MortuaryCustodyEvents_MortuaryCases_MortuaryCaseId",
                        column: x => x.MortuaryCaseId,
                        principalTable: "MortuaryCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCases_PatientId",
                table: "MortuaryCases",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCases_TenantId",
                table: "MortuaryCases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCases_TenantId_Status_DeathDeclaredAt",
                table: "MortuaryCases",
                columns: new[] { "TenantId", "Status", "DeathDeclaredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCases_TenantId_TagNumber",
                table: "MortuaryCases",
                columns: new[] { "TenantId", "TagNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCustodyEvents_MortuaryCaseId",
                table: "MortuaryCustodyEvents",
                column: "MortuaryCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCustodyEvents_TenantId",
                table: "MortuaryCustodyEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MortuaryCustodyEvents_TenantId_MortuaryCaseId_CreatedAt",
                table: "MortuaryCustodyEvents",
                columns: new[] { "TenantId", "MortuaryCaseId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MortuaryCustodyEvents");

            migrationBuilder.DropTable(
                name: "MortuaryCases");
        }
    }
}
