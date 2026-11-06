using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgresApplicationRolesAndRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "UserInvites" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "Email" text NOT NULL,
                    "Role" integer NOT NULL,
                    "FirstName" text NOT NULL,
                    "LastName" text NOT NULL,
                    "DateOfBirth" timestamp without time zone NULL,
                    "FacilityId" uuid NULL,
                    "DepartmentId" uuid NULL,
                    "TokenHash" text NOT NULL,
                    "ExpiresAtUtc" timestamp without time zone NOT NULL,
                    "AcceptedAtUtc" timestamp without time zone NULL,
                    "CreatedUserAccountId" uuid NULL,
                    "InvitedByUserAccountId" uuid NOT NULL,
                    "CreatedAtUtc" timestamp without time zone NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_UserInvites_TokenHash" ON "UserInvites" ("TokenHash");
                CREATE INDEX IF NOT EXISTS "IX_UserInvites_TenantId" ON "UserInvites" ("TenantId");
                CREATE TABLE IF NOT EXISTS "RefundRequests" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "PaymentId" uuid NOT NULL,
                    "AmountCents" integer NOT NULL,
                    "Currency" text NOT NULL,
                    "Reason" text NOT NULL,
                    "Status" text NOT NULL,
                    "RequestedByUserAccountId" uuid NOT NULL,
                    "ReviewedByUserAccountId" uuid NULL,
                    "ReviewedAtUtc" timestamp without time zone NULL,
                    "ReviewNotes" text NULL,
                    "CreatedAtUtc" timestamp without time zone NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_RefundRequests_PaymentId" ON "RefundRequests" ("PaymentId");
                CREATE INDEX IF NOT EXISTS "IX_RefundRequests_TenantId" ON "RefundRequests" ("TenantId");

                DO $$
                DECLARE
                    table_name text;
                BEGIN
                    ALTER SCHEMA public OWNER TO portal_owner;
                    GRANT USAGE ON SCHEMA public TO portal_app;

                    FOR table_name IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relkind = 'r'
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I OWNER TO portal_owner', table_name);
                        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.%I TO portal_app', table_name);
                    END LOOP;

                    FOR table_name IN
                        SELECT sequence_name
                        FROM information_schema.sequences
                        WHERE sequence_schema = 'public'
                    LOOP
                        EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE public.%I TO portal_app', table_name);
                    END LOOP;

                    FOR table_name IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relkind = 'r'
                          AND EXISTS (
                              SELECT 1
                              FROM information_schema.columns col
                              WHERE col.table_schema = n.nspname
                                AND col.table_name = c.relname
                                AND col.column_name = 'TenantId'
                          )
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('DROP POLICY IF EXISTS %I ON public.%I', table_name || '_tenant_isolation', table_name);
                        EXECUTE format(
                            'CREATE POLICY %I ON public.%I FOR ALL TO portal_app USING ("TenantId" = NULLIF(current_setting(''app.current_tenant_id'', true), '''')::uuid) WITH CHECK ("TenantId" = NULLIF(current_setting(''app.current_tenant_id'', true), '''')::uuid)',
                            table_name || '_tenant_isolation', table_name);
                    END LOOP;

                    REVOKE UPDATE, DELETE ON TABLE public."AuditLogs" FROM portal_app;
                    FOREACH table_name IN ARRAY ARRAY['StockLedgerEntries', 'ControlledDrugEntries', 'LinenLedgerEntries', 'Payments', 'Receipts', 'MortuaryCustodyEvents']
                    LOOP
                        IF to_regclass('public.' || quote_ident(table_name)) IS NOT NULL THEN
                            EXECUTE format('REVOKE UPDATE, DELETE ON TABLE public.%I FROM portal_app', table_name);
                        END IF;
                    END LOOP;
                END
                $$;

                ALTER DEFAULT PRIVILEGES FOR ROLE portal_owner IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO portal_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE portal_owner IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO portal_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOR table_name IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relkind = 'r'
                          AND EXISTS (
                              SELECT 1 FROM information_schema.columns col
                              WHERE col.table_schema = n.nspname
                                AND col.table_name = c.relname
                                AND col.column_name = 'TenantId'
                          )
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I NO FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE public.%I DISABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('DROP POLICY IF EXISTS %I ON public.%I', table_name || '_tenant_isolation', table_name);
                    END LOOP;
                END
                $$;
                """);
        }
    }
}
