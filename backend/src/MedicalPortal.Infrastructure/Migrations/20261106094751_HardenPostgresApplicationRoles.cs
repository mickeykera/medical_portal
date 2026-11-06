using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenPostgresApplicationRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY['AuditLogs', 'StockLedgerEntries', 'ControlledDrugEntries', 'LinenLedgerEntries', 'Payments', 'Receipts', 'MortuaryCustodyEvents']
                    LOOP
                        IF to_regclass('public.' || quote_ident(table_name)) IS NOT NULL THEN
                            EXECUTE format('REVOKE UPDATE, DELETE ON TABLE public.%I FROM portal_app', table_name);
                        END IF;
                    END LOOP;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
