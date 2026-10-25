#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

DEV_TOTP_SECRET="${DEV_TOTP_SECRET:-}"

if [[ -z "$DEV_TOTP_SECRET" && -f "$ENV_FILE" ]]; then
  # Parse DEV_TOTP_SECRET from .env without executing arbitrary code
  DEV_TOTP_SECRET=$(grep -E '^DEV_TOTP_SECRET=' "$ENV_FILE" | cut -d '=' -f2- | tr -d ' "\r\n' || true)
fi

if [[ -z "$DEV_TOTP_SECRET" ]]; then
  echo "Error: DEV_TOTP_SECRET is not set in environment or in $ENV_FILE." >&2
  echo "Please run ./scripts/dev-setup.sh to generate .env." >&2
  exit 1
fi

if command -v oathtool >/dev/null 2>&1; then
  NOW=$(date +%s)
  REMAINING=$((30 - (NOW % 30)))
  CODE=$(oathtool --totp -b "$DEV_TOTP_SECRET")
  echo "${CODE} (${REMAINING}s remaining)"
elif command -v python3 >/dev/null 2>&1; then
  python3 - "$DEV_TOTP_SECRET" << 'EOF'
import sys, time, struct, hmac, hashlib, base64

secret = sys.argv[1].strip()
padding = (8 - len(secret) % 8) % 8
if padding:
    secret += '=' * padding

try:
    key = base64.b32decode(secret, casefold=True)
except Exception as e:
    print(f"Error: Invalid Base32 DEV_TOTP_SECRET: {e}", file=sys.stderr)
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

print(f"{code} ({remaining}s remaining)")
EOF
else
  echo "Error: Neither oathtool nor python3 is available to generate TOTP code." >&2
  exit 1
fi
