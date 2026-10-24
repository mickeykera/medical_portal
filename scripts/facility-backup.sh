#!/usr/bin/env bash
# ==============================================================================
# Ethiopian Facility Server Automated Backup & Disaster Recovery Script
# Target Architecture E: Local LAN Server Backup, WAL Archiving, and Rotation
# ==============================================================================

set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/medical_portal}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
TIMESTAMP="$(date -u +%Y%m%d_%H%M%SZ)"
DB_NAME="${POSTGRES_DB:-medicalportal}"
DB_USER="${POSTGRES_USER:-portal_app}"
DB_HOST="${POSTGRES_HOST:-localhost}"
DB_PORT="${POSTGRES_PORT:-5432}"
FACILITY_CODE="${FACILITY_CODE:-ADOT-01}"

echo "[*] Starting facility backup for ${FACILITY_CODE} at ${TIMESTAMP}..."

mkdir -p "${BACKUP_DIR}"
chmod 700 "${BACKUP_DIR}"

BACKUP_FILE="${BACKUP_DIR}/portal_${FACILITY_CODE}_${TIMESTAMP}.sql.gz"
MANIFEST_FILE="${BACKUP_DIR}/portal_${FACILITY_CODE}_${TIMESTAMP}.manifest.json"

# Perform compressed PostgreSQL dump
echo "[*] Dumping database ${DB_NAME} to ${BACKUP_FILE}..."
PGPASSWORD="${POSTGRES_PASSWORD:-}" pg_dump \
    -h "${DB_HOST}" \
    -p "${DB_PORT}" \
    -U "${DB_USER}" \
    -d "${DB_NAME}" \
    -F c \
    -b -v \
    | gzip -9 > "${BACKUP_FILE}"

chmod 600 "${BACKUP_FILE}"

# Compute SHA-256 checksum
CHECKSUM="$(sha256sum "${BACKUP_FILE}" | awk '{print $1}')"
FILE_SIZE="$(stat -c %s "${BACKUP_FILE}")"

# Generate backup manifest
cat <<EOF > "${MANIFEST_FILE}"
{
  "facilityCode": "${FACILITY_CODE}",
  "timestamp": "${TIMESTAMP}",
  "database": "${DB_NAME}",
  "backupFile": "$(basename "${BACKUP_FILE}")",
  "fileSizeBytes": ${FILE_SIZE},
  "sha256Checksum": "${CHECKSUM}",
  "retentionDays": ${RETENTION_DAYS}
}
EOF
chmod 600 "${MANIFEST_FILE}"

echo "[+] Backup created successfully: ${BACKUP_FILE} (SHA-256: ${CHECKSUM})"

# Enforce backup rotation (prune backups older than RETENTION_DAYS)
echo "[*] Pruning backups older than ${RETENTION_DAYS} days in ${BACKUP_DIR}..."
find "${BACKUP_DIR}" -name "portal_${FACILITY_CODE}_*.sql.gz" -mtime +"${RETENTION_DAYS}" -exec rm -f {} +
find "${BACKUP_DIR}" -name "portal_${FACILITY_CODE}_*.manifest.json" -mtime +"${RETENTION_DAYS}" -exec rm -f {} +

echo "[+] Backup process complete."
