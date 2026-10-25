#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Error: .env file not found at $ENV_FILE" >&2
  echo "Please run ./scripts/dev-setup.sh first to configure local development." >&2
  exit 1
fi

# Guard against running in Production
ENV_MODE="${ASPNETCORE_ENVIRONMENT:-}"
if [[ -z "$ENV_MODE" ]]; then
  ENV_MODE=$(grep -E '^ASPNETCORE_ENVIRONMENT=' "$ENV_FILE" | cut -d '=' -f2- | tr -d ' "\r\n' || true)
fi

if [[ "$ENV_MODE" == "Production" ]]; then
  echo "Error: scripts/dev-logins.sh is strictly prohibited in Production environment." >&2
  exit 1
fi

# Safely extract DEV_SEED_PASSWORD and DEV_TOTP_SECRET from .env
DEV_SEED_PASSWORD=$(grep -E '^DEV_SEED_PASSWORD=' "$ENV_FILE" | cut -d '=' -f2- | tr -d ' "\r\n' || true)
DEV_TOTP_SECRET=$(grep -E '^DEV_TOTP_SECRET=' "$ENV_FILE" | cut -d '=' -f2- | tr -d ' "\r\n' || true)

if [[ -z "$DEV_SEED_PASSWORD" ]]; then
  echo "Error: DEV_SEED_PASSWORD is not set in $ENV_FILE." >&2
  echo "Please run ./scripts/dev-setup.sh to generate random development credentials." >&2
  exit 1
fi

# Compute current TOTP code and seconds remaining
MFA_DISPLAY="None"
if [[ -n "$DEV_TOTP_SECRET" ]]; then
  if command -v oathtool >/dev/null 2>&1; then
    NOW=$(date +%s)
    REMAINING=$((30 - (NOW % 30)))
    CODE=$(oathtool --totp -b "$DEV_TOTP_SECRET")
    MFA_DISPLAY="${CODE} (${REMAINING}s)"
  elif command -v python3 >/dev/null 2>&1; then
    MFA_DISPLAY=$(python3 - "$DEV_TOTP_SECRET" << 'EOF'
import sys, time, struct, hmac, hashlib, base64

secret = sys.argv[1].strip()
padding = (8 - len(secret) % 8) % 8
if padding:
    secret += '=' * padding

try:
    key = base64.b32decode(secret, casefold=True)
except Exception:
    sys.exit(1)

now = int(time.time())
step = 30
remaining = step - (now % step)
counter = now // step

msg = struct.pack(">Q", counter)
h = hmac.new(key, msg, hashlib.sha1).digest()
offset = h[-1] & 0x0F
truncated = struct.unpack(">I", h[offset:offset+4])[0] & 0x7FFFFFFF
code = str(truncated % 1_000_000).zfill(6)

print(f"{code} ({remaining}s)")
EOF
)
  else
    MFA_DISPLAY="Unavailable"
  fi
fi

echo "====================================================================================================================================================="
echo "                                                      MEDICAL PORTAL - DEVELOPMENT LOGINS                                                           "
echo "====================================================================================================================================================="
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Role" "Dashboard" "Email" "Password" "Current MFA Code"
echo "---------------------------+------------------------------+----------------------------------+--------------------------+-------------------------"

printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Patient" "Patient Portal" "almaz.bekele@clinic.example" "$DEV_SEED_PASSWORD" "None (Optional)"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Proxy / Guardian" "Patient Portal (Proxy)" "tigist.hailu@clinic.example" "$DEV_SEED_PASSWORD" "None (Optional)"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Clinician" "Clinical Cockpit" "apatel@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Nurse" "Clinical Cockpit (Triage)" "sister.bethlehem@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Front Desk (Scheduler)" "Scheduling Dashboard" "sara.tefera@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Billing Officer" "Billing Operations" "daniel.worku@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Lab Specialist" "Laboratory Diagnostics" "meron.assefa@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Pharmacist" "Pharmacy Dispensary" "eyob.getachew@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Dept Admin (Family Med)" "Hospital Admin (Dept Scope)" "deptadmin.fam@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Dept Admin (Cardiology)" "Hospital Admin (Dept Scope)" "deptadmin.card@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Hospital Admin" "Hospital Admin Console" "admin@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Compliance Auditor" "Compliance & Audit Console" "auditor.compliance@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Platform Admin" "Platform Admin (Zero PHI)" "platform.admin@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Integration Admin" "Integrations & System Admin" "system.admin@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Tenant 2 Admin (Adot)" "Hospital Admin (Adot)" "admin.adot@clinic.example" "$DEV_SEED_PASSWORD" "$MFA_DISPLAY"
printf "%-26s | %-28s | %-32s | %-24s | %-16s\n" "Tenant 2 Patient (Adot)" "Patient Portal (Adot)" "patient.adot@clinic.example" "$DEV_SEED_PASSWORD" "None (Optional)"

echo "====================================================================================================================================================="
echo "Note: Patient accounts do not require MFA unless configured. Staff and Admin accounts require the 6-digit TOTP code."
echo "Tenant 2 accounts are isolated to tenant 'adot-hospital' (header: X-Tenant-Slug: adot-hospital)."
