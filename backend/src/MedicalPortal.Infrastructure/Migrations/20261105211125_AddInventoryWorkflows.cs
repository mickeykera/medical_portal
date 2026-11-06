using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControlledDrugEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSignatureUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondSignatureUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlledDrugEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountSheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryRequisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    FromStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRequisitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CountSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemQuantity = table.Column<int>(type: "integer", nullable: false),
                    CountedQuantity = table.Column<int>(type: "integer", nullable: false),
                    VarianceReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_InventoryCountSheets_CountSheetId",
                        column: x => x.CountSheetId,
                        principalTable: "InventoryCountSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryRequisitionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    IssuedQuantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRequisitionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryRequisitionLines_InventoryRequisitions_Requisition~",
                        column: x => x.RequisitionId,
                        principalTable: "InventoryRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugEntries_TenantId",
                table: "ControlledDrugEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDrugEntries_TenantId_StoreId_CreatedAt",
                table: "ControlledDrugEntries",
                columns: new[] { "TenantId", "StoreId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_CountSheetId",
                table: "InventoryCountLines",
                column: "CountSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_TenantId",
                table: "InventoryCountLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_TenantId_CountSheetId_ItemId_BatchId",
                table: "InventoryCountLines",
                columns: new[] { "TenantId", "CountSheetId", "ItemId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountSheets_TenantId",
                table: "InventoryCountSheets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountSheets_TenantId_StoreId_CreatedAt",
                table: "InventoryCountSheets",
                columns: new[] { "TenantId", "StoreId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRequisitionLines_RequisitionId",
                table: "InventoryRequisitionLines",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRequisitionLines_TenantId",
                table: "InventoryRequisitionLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRequisitionLines_TenantId_RequisitionId_ItemId",
                table: "InventoryRequisitionLines",
                columns: new[] { "TenantId", "RequisitionId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRequisitions_TenantId",
                table: "InventoryRequisitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRequisitions_TenantId_Status_CreatedAt",
                table: "InventoryRequisitions",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ControlledDrugEntries");

            migrationBuilder.DropTable(
                name: "InventoryCountLines");

            migrationBuilder.DropTable(
                name: "InventoryRequisitionLines");

            migrationBuilder.DropTable(
                name: "InventoryCountSheets");

            migrationBuilder.DropTable(
                name: "InventoryRequisitions");
        }
    }
}
