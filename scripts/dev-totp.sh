#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Generate current dev clinician TOTP code
dotnet run --project "$REPO_ROOT/backend/src/MedicalPortal.Web" --no-build -- --totp
