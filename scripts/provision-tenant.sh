#!/usr/bin/env bash
# =============================================================================
# Turnkey Hospital Tenant Provisioning CLI
# Usage: ./scripts/provision-tenant.sh <config-file.json> [api-base-url] [token]
# =============================================================================
set -euo pipefail

CONFIG_FILE="${1:-docs/tenants/Adot-Hospital/tenant-config.json}"
API_BASE="${2:-http://localhost:5000}"
PLATFORM_TOKEN="${3:-${PLATFORM_ADMIN_TOKEN:-}}"

echo "====================================================================="
echo "  Hospital Platform: Tenant Provisioning Tool"
echo "====================================================================="
echo "Config File:  $CONFIG_FILE"
echo "Target Host:  $API_BASE"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "[-] Error: Configuration file '$CONFIG_FILE' not found." >&2
    exit 1
fi

# Verify jq is installed for JSON parsing
if ! command -v jq &> /dev/null; then
    echo "[-] Error: 'jq' is required to parse tenant configuration." >&2
    exit 1
fi

TENANT_NAME=$(jq -r '.name' "$CONFIG_FILE")
TENANT_SLUG=$(jq -r '.slug' "$CONFIG_FILE")
PRIMARY_COLOR=$(jq -r '.settings.branding.primaryColor // "#0284c7"' "$CONFIG_FILE")
ISOLATION=$(jq -r '.settings.hosting.isolation // "Shared database"' "$CONFIG_FILE")

echo "[+] Parsing tenant: '$TENANT_NAME' (Slug: $TENANT_SLUG)"
echo "[+] Primary Brand Color: $PRIMARY_COLOR"
echo "[+] Hosting Isolation:  $ISOLATION"

# Step 1: Pre-validation check via Platform API (if token provided)
if [ -n "$PLATFORM_TOKEN" ]; then
    echo "[+] Running pre-flight validation against Platform API..."
    VALIDATE_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_BASE/api/platform/tenants/validate" \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $PLATFORM_TOKEN" \
        -d @"$CONFIG_FILE")

    HTTP_CODE=$(echo "$VALIDATE_RESPONSE" | tail -n1)
    BODY=$(echo "$VALIDATE_RESPONSE" | sed '$d')

    if [ "$HTTP_CODE" -ne 200 ]; then
        echo "[-] Pre-flight validation failed with HTTP $HTTP_CODE: $BODY" >&2
        exit 1
    fi

    IS_VALID=$(echo "$BODY" | jq -r '.isValid')
    if [ "$IS_VALID" != "true" ]; then
        echo "[-] Tenant configuration validation errors:" >&2
        echo "$BODY" | jq -r '.errors[]' >&2
        exit 1
    fi

    echo "[✓] Configuration schema & WCAG contrast validated successfully."
    echo "[+] WCAG Evaluation: $(echo "$BODY" | jq -r '.wcag.summary // "Compliant"')"

    # Step 2: Submit Provisioning Request
    echo "[+] Submitting tenant provisioning request..."
    PROVISION_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_BASE/api/platform/tenants/provision" \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $PLATFORM_TOKEN" \
        -d @"$CONFIG_FILE")

    P_HTTP_CODE=$(echo "$PROVISION_RESPONSE" | tail -n1)
    P_BODY=$(echo "$PROVISION_RESPONSE" | sed '$d')

    if [ "$P_HTTP_CODE" -eq 201 ]; then
        echo "[✓] Successfully provisioned tenant '$TENANT_NAME'!"
        echo "$P_BODY" | jq '.'
    elif [ "$P_HTTP_CODE" -eq 409 ]; then
        echo "[!] Tenant '$TENANT_SLUG' is already provisioned (HTTP 409 Conflict)."
    else
        echo "[-] Provisioning failed with HTTP $P_HTTP_CODE: $P_BODY" >&2
        exit 1
    fi
else
    echo "[!] No PLATFORM_ADMIN_TOKEN provided. Configuration file syntax is valid."
    echo "    To provision via API, pass the bearer token: ./scripts/provision-tenant.sh $CONFIG_FILE $API_BASE <TOKEN>"
fi

echo "====================================================================="
echo "  Tenant Provisioning Complete"
echo "====================================================================="
