using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RebuildSchedulingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Locations_LocationId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ProviderId",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Locations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentTypeId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoomResourceId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomResources_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    IsHoliday = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleBlocks_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleBlocks_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyScheduleTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DayOfWeek = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyScheduleTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyScheduleTemplates_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WeeklyScheduleTemplates_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentTypeId",
                table: "Appointments",
                column: "AppointmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ProviderId_StartAt_EndAt",
                table: "Appointments",
                columns: new[] { "ProviderId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RoomResourceId_StartAt_EndAt",
                table: "Appointments",
                columns: new[] { "RoomResourceId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_Code",
                table: "AppointmentTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomResources_LocationId",
                table: "RoomResources",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_LocationId_StartAt_EndAt",
                table: "ScheduleBlocks",
                columns: new[] { "LocationId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_ProviderId_StartAt_EndAt",
                table: "ScheduleBlocks",
                columns: new[] { "ProviderId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleTemplates_LocationId",
                table: "WeeklyScheduleTemplates",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyScheduleTemplates_ProviderId",
                table: "WeeklyScheduleTemplates",
                column: "ProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppointmentTypes_AppointmentTypeId",
                table: "Appointments",
                column: "AppointmentTypeId",
                principalTable: "AppointmentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Locations_LocationId",
                table: "Appointments",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_RoomResources_RoomResourceId",
                table: "Appointments",
                column: "RoomResourceId",
                principalTable: "RoomResources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppointmentTypes_AppointmentTypeId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Locations_LocationId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_RoomResources_RoomResourceId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentTypes");

            migrationBuilder.DropTable(
                name: "RoomResources");

            migrationBuilder.DropTable(
                name: "ScheduleBlocks");

            migrationBuilder.DropTable(
                name: "WeeklyScheduleTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_AppointmentTypeId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ProviderId_StartAt_EndAt",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RoomResourceId_StartAt_EndAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AppointmentTypeId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RoomResourceId",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ProviderId",
                table: "Appointments",
                column: "ProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Locations_LocationId",
                table: "Appointments",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id");
        }
    }
}
