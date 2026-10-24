using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAuthorizationAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanSubmitForms",
                table: "ProxyInvites",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanSubmitForms",
                table: "ProxyGrants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConsumedPaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayPaymentIntentId = table.Column<string>(type: "text", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumedPaymentIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumedPaymentIntents_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumedPaymentIntents_GatewayPaymentIntentId",
                table: "ConsumedPaymentIntents",
                column: "GatewayPaymentIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumedPaymentIntents_PatientId_IdempotencyKey",
                table: "ConsumedPaymentIntents",
                columns: new[] { "PatientId", "IdempotencyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumedPaymentIntents");

            migrationBuilder.DropColumn(
                name: "CanSubmitForms",
                table: "ProxyInvites");

            migrationBuilder.DropColumn(
                name: "CanSubmitForms",
                table: "ProxyGrants");
        }
    }
}
