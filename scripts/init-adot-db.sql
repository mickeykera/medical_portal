-- =============================================================================
-- Dedicated Database Initialization: Adot Internal Medicine (Adot-Hospital)
-- Reference: docs/tenants/Adot-Hospital/provisioning-plan.md
-- =============================================================================

-- 1. Create Dedicated Application Roles
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'adot_portal_app') THEN
        CREATE ROLE adot_portal_app WITH LOGIN PASSWORD 'adot_portal_secure_pass';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'adot_openmrs_app') THEN
        CREATE ROLE adot_openmrs_app WITH LOGIN PASSWORD 'adot_openmrs_secure_pass';
    END IF;
END $$;

-- 2. Create Isolated Databases for Portal and OpenMRS Clinical Core
SELECT 'CREATE DATABASE adot_portal_db OWNER adot_portal_app ENCODING ''UTF8'''
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'adot_portal_db')\gexec

SELECT 'CREATE DATABASE adot_openmrs_db OWNER adot_openmrs_app ENCODING ''UTF8'''
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'adot_openmrs_db')\gexec

-- 3. Revoke Public Privileges for Zero-Trust Tenant Isolation
REVOKE ALL ON DATABASE adot_portal_db FROM PUBLIC;
REVOKE ALL ON DATABASE adot_openmrs_db FROM PUBLIC;

-- 4. Grant Strict Connection & Usage Rights to Dedicated Service Roles
GRANT CONNECT ON DATABASE adot_portal_db TO adot_portal_app;
GRANT CONNECT ON DATABASE adot_openmrs_db TO adot_openmrs_app;

-- 5. Connect to adot_portal_db and configure schema permissions
\c adot_portal_db

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
GRANT ALL ON SCHEMA public TO adot_portal_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO adot_portal_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO adot_portal_app;

-- 6. Connect to adot_openmrs_db and configure schema permissions
\c adot_openmrs_db

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
GRANT ALL ON SCHEMA public TO adot_openmrs_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO adot_openmrs_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO adot_openmrs_app;
