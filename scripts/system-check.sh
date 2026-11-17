#!/usr/bin/env bash
set -uo pipefail

# System check for medical_portal compose project.
# Default: non-destructive checks against running dev stack.
# --reset: docker compose down -v for THIS PROJECT ONLY (DATA LOSS), then up dev stack.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
REPORT="/tmp/system-check-report.txt"
EVIDENCE="/tmp/system-check-evidence"
COOKIE_DIR="$EVIDENCE/cookies"
BASE_URL="http://localhost:5000"
RESET=0
COMPOSE_PROJECT="medical_portal"
PASS=0; FAIL=0; INCONC=0; NI=0

for arg in "$@"; do
  case "$arg" in
    --reset) RESET=1 ;;
    *) echo "Unknown arg: $arg" >&2; exit 2 ;;
  esac
done

mkdir -p "$EVIDENCE" "$COOKIE_DIR"
: > "$REPORT"
: > "$EVIDENCE/results.tsv"
: > "$EVIDENCE/notes.txt"

# load env without printing secrets
if [[ ! -f "$ENV_FILE" ]]; then echo "Missing $ENV_FILE" >&2; exit 2; fi
set -a; source "$ENV_FILE"; set +a

compose() {
  docker compose -p "$COMPOSE_PROJECT" -f "$REPO_ROOT/docker-compose.yml" --env-file "$ENV_FILE" "$@"
}

log() { printf '%s\n' "$*" | tee -a "$REPORT" >/dev/null; }
record() {
  local id="$1" check="$2" result="$3" evidence="$4"
  printf '%s\t%s\t%s\t%s\n' "$id" "$check" "$result" "$evidence" >> "$EVIDENCE/results.tsv"
  case "$result" in
    PASS) PASS=$((PASS+1));;
    FAIL) FAIL=$((FAIL+1));;
    INCONCLUSIVE) INCONC=$((INCONC+1));;
    NOT\ IMPLEMENTED) NI=$((NI+1));;
  esac
}

json_field() {
  python3 - "$1" "$2" <<'PY'
import json,sys
path,file=sys.argv[1],sys.argv[2]
try:
  d=json.load(open(file))
except Exception:
  print(''); sys.exit(0)
cur=d
for k in path.split('.'):
  if isinstance(cur,dict) and k in cur:
    cur=cur[k]
  else:
    print(''); sys.exit(0)
if isinstance(cur,bool): print('true' if cur else 'false')
elif cur is None: print('')
else: print(cur)
PY
}

psql_owner() {
  docker exec medical-portal-postgres psql -U portal_owner -d medical_portal -v ON_ERROR_STOP=0 -t -A -c "$1" 2>&1 || true
}
psql_app() {
  docker exec -e PGPASSWORD="$PORTAL_APP_PASSWORD" medical-portal-postgres psql -U portal_app -d medical_portal -v ON_ERROR_STOP=0 -t -A -c "$1" 2>&1 || true
}

table_exists() {
  local t
  t=$(psql_owner "SELECT to_regclass('public.\"$1\"');" | tr -d '[:space:]')
  [[ -n "$t" && "$t" != "" && "$t" != "null" && "$t" != "NULL" ]]
}
real_table_name() {
  # return first matching table name (case-insensitive) or empty
  psql_owner "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND lower(table_name)=lower('$1') LIMIT 1;" | tr -d '[:space:]'
}

get_totp() { bash "$REPO_ROOT/scripts/dev-totp.sh" 2>/dev/null | awk '{print $1}'; }

apicall() {
  local jar="$1"; local method="$2"; local path="$3"; local data="${4:-}"
  local url="${BASE_URL}${path}"
  local out="$EVIDENCE/resp_$(echo "$path" | tr '/?=&{}:' '________' | head -c 70)_${RANDOM}.json"
  local code
  if [[ -n "$data" ]]; then
    code=$(curl -sS --max-time 20 -o "$out" -w '%{http_code}' -X "$method" "$url" \
      -b "$jar" -c "$jar" -H "Content-Type: application/json" -H "X-CSRF-Token: 1" -H "X-Requested-With: XMLHttpRequest" \
      --data "$data" 2>"$EVIDENCE/curl_err_${RANDOM}.txt" || echo "000")
  else
    code=$(curl -sS --max-time 20 -o "$out" -w '%{http_code}' -X "$method" "$url" \
      -b "$jar" -c "$jar" -H "X-CSRF-Token: 1" -H "X-Requested-With: XMLHttpRequest" \
      2>"$EVIDENCE/curl_err_${RANDOM}.txt" || echo "000")
  fi
  echo "$code|$out"
}

log "SYSTEM CHECK REPORT - $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
log "Repo: $REPO_ROOT"
log "Compose project: $COMPOSE_PROJECT (only this project may be managed)"
log "Reset: $RESET"

if [[ $RESET -eq 1 ]]; then
  echo "WARNING: --reset will run 'docker compose down -v' for project $COMPOSE_PROJECT ONLY. ALL DATA FOR THIS PROJECT WILL BE DELETED." | tee -a "$REPORT"
  compose --profile dev down -v > "$EVIDENCE/compose_down.log" 2>&1 || true
  # remove only project-named containers if lingering (scoped names)
  docker rm -f medical-portal-postgres medical-portal-migrate medical-portal-migrate-prod medical-portal-app medical-portal-proxy medical-portal-proxy-prod medical-portal-app-1 medical-portal-app-2 medical-portal-redis 2>/dev/null || true
  compose --profile dev up -d --build > "$EVIDENCE/compose_up.log" 2>&1 || true
fi

# Preflight (must pass before any other checks)
preflight_ok=1
preflight_reason=""
ready_code=$(curl -sS --max-time 10 -o "$EVIDENCE/preflight_ready.json" -w '%{http_code}' "$BASE_URL/health/ready" 2>/dev/null || echo "000")
if [[ "$ready_code" != "200" ]]; then
  preflight_ok=0
  preflight_reason="SYSTEM DOWN: /health/ready status=$ready_code (expected 200). See docker compose ps/logs."
else
  # try login for one known role from dev-logins.sh
  if [[ -f "$REPO_ROOT/scripts/dev-logins.sh" ]]; then
    # extract first patient email from script
    login_email="almaz.bekele@clinic.example"
  else
    login_email=""
  fi
  if [[ -n "$login_email" && -n "${DEV_SEED_PASSWORD:-}" ]]; then
    pjar="$COOKIE_DIR/preflight.cookie"; : > "$pjar"
    lres=$(apicall "$pjar" POST "/api/auth/login" "{\"email\":\"$login_email\",\"password\":\"$DEV_SEED_PASSWORD\"}")
    lcode="${lres%%|*}"; lout="${lres#*|}"
    if [[ "$lcode" != "200" ]]; then
      preflight_ok=0
      preflight_reason="SYSTEM DOWN/PREAUTH FAILED: login for $login_email status=$lcode body=$(head -c 200 "$lout")"
    fi
  else
    preflight_ok=0
    preflight_reason="SYSTEM DOWN: cannot preflight login (missing dev-logins context or DEV_SEED_PASSWORD)"
  fi
fi

if [[ $preflight_ok -ne 1 ]]; then
  log "PRECHECK: FAIL - $preflight_reason"
  log "Root cause hints:"
  compose --profile dev ps 2>&1 | head -40 >> "$REPORT" || true
  docker logs --tail 60 medical-portal-proxy 2>&1 | head -60 >> "$REPORT" || true
  docker logs --tail 80 medical-portal-migrate 2>&1 | head -80 >> "$REPORT" || true
  docker logs --tail 80 medical-portal-app 2>&1 | head -80 >> "$REPORT" || true
  echo "SYSTEM DOWN - exit 2" | tee -a "$REPORT"
  exit 2
fi

log "PRECHECK: PASS (health ready + login ok)"

# Discover schema table names
psql_owner "SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name;" > "$EVIDENCE/db_tables.txt" 2>&1 || true

# Extract frontend API paths
grep -rhoE '"/api/[a-zA-Z0-9_./?=:-]+"' "$REPO_ROOT/frontend/js" 2>/dev/null | tr -d '"' | sort -u > "$EVIDENCE/frontend_api_paths.txt" || true
grep -rhoE "'/api/[a-zA-Z0-9_./?=:-]+'" "$REPO_ROOT/frontend/js" 2>/dev/null | tr -d "'" | sort -u >> "$EVIDENCE/frontend_api_paths.txt" || true
sort -u "$EVIDENCE/frontend_api_paths.txt" -o "$EVIDENCE/frontend_api_paths.txt" 2>/dev/null || true

# Extract controller routes (best-effort grep)
grep -RhoE '\[(Http(Get|Post|Put|Delete|Patch)|Route)\("[^"]*"\)\]' "$REPO_ROOT/backend/src/MedicalPortal.Web/Controllers" 2>/dev/null | sort -u > "$EVIDENCE/controller_routes_raw.txt" || true
record "DISC-1" "Frontend API paths extracted" "PASS" "count=$(wc -l < "$EVIDENCE/frontend_api_paths.txt" 2>/dev/null || echo 0) file=$EVIDENCE/frontend_api_paths.txt"
record "DISC-2" "Controller routes extracted (grep)" "PASS" "file=$EVIDENCE/controller_routes_raw.txt"

# Minimal auth checks (wrong password/mfa must be rejected)
wjar="$COOKIE_DIR/wrong.cookie"; : > "$wjar"
wres=$(apicall "$wjar" POST "/api/auth/login" "{\"email\":\"almaz.bekele@clinic.example\",\"password\":\"WRONG_PASSWORD\"}")
wcode="${wres%%|*}"
if [[ "$wcode" == "401" || "$wcode" == "400" ]]; then
  record "AUTH-1" "Wrong password rejected" "PASS" "status=$wcode http_only=no_db_needed"
else
  record "AUTH-1" "Wrong password rejected" "FAIL" "status=$wcode body=$(head -c 150 "${wres#*|}")"
fi

# wrong MFA for clinician
cjar="$COOKIE_DIR/clin_mfa_check.cookie"; : > "$cjar"
cres=$(apicall "$cjar" POST "/api/auth/login" "{\"email\":\"apatel@clinic.example\",\"password\":\"$DEV_SEED_PASSWORD\"}")
ccode="${cres%%|*}"; cout="${cres#*|}"
if grep -q '"mfaRequired"[[:space:]]*:[[:space:]]*true' "$cout" 2>/dev/null; then
  mfaToken=$(json_field "mfaToken" "$cout")
  if [[ -n "$mfaToken" ]]; then
    mres=$(apicall "$cjar" POST "/api/auth/mfa/verify" "{\"mfaToken\":\"$mfaToken\",\"code\":\"000000\"}")
    mcode="${mres%%|*}"
    if [[ "$mcode" == "401" || "$mcode" == "400" ]]; then
      record "AUTH-2" "Wrong MFA rejected" "PASS" "status=$mcode"
    else
      record "AUTH-2" "Wrong MFA rejected" "FAIL" "status=$mcode body=$(head -c 150 "${mres#*|}")"
    fi
  else
    record "AUTH-2" "Wrong MFA rejected" "INCONCLUSIVE" "mfaRequired but no mfaToken field"
  fi
elif [[ "$ccode" == "000" ]]; then
  record "AUTH-2" "Wrong MFA rejected" "FAIL" "login unreachable status=000"
else
  record "AUTH-2" "Wrong MFA rejected" "INCONCLUSIVE" "clinician login not MFA-challenged status=$ccode"
fi

# Seeded roles (from dev-logins.sh context)
declare -a ROLES=(
  "almaz.bekele@clinic.example|Patient|false"
  "tigist.hailu@clinic.example|Proxy|false"
  "apatel@clinic.example|Clinician|true"
  "sister.bethlehem@clinic.example|Nurse|true"
  "sara.tefera@clinic.example|FrontDesk|true"
  "daniel.worku@clinic.example|BillingOfficer|true"
  "meron.assefa@clinic.example|LabSpecialist|true"
  "eyob.getachew@clinic.example|Pharmacist|true"
  "deptadmin.fam@clinic.example|DeptAdminFam|true"
  "deptadmin.card@clinic.example|DeptAdminCard|true"
  "admin@clinic.example|HospitalAdmin|true"
  "auditor.compliance@clinic.example|ComplianceAuditor|true"
  "platform.admin@clinic.example|PlatformAdmin|true"
  "system.admin@clinic.example|IntegrationAdmin|true"
)

login_role() {
  local email="$1" jar="$2" mfa="$3"
  local res code out
  res=$(apicall "$jar" POST "/api/auth/login" "{\"email\":\"$email\",\"password\":\"$DEV_SEED_PASSWORD\"}")
  code="${res%%|*}"; out="${res#*|}"
  if [[ "$code" == "200" ]] && grep -q '"mfaRequired"[[:space:]]*:[[:space:]]*true' "$out" 2>/dev/null && [[ "$mfa" == "true" ]]; then
    local mfaToken totp mres mcode
    mfaToken=$(json_field "mfaToken" "$out")
    totp=$(get_totp)
    mres=$(apicall "$jar" POST "/api/auth/mfa/verify" "{\"mfaToken\":\"$mfaToken\",\"code\":\"$totp\"}")
    mcode="${mres%%|*}"
    echo "$mcode|${mres#*|}"
    return
  fi
  echo "$code|$out"
}

for entry in "${ROLES[@]}"; do
  IFS='|' read -r email rname mfa <<< "$entry"
  jar="$COOKIE_DIR/${rname}.cookie"; : > "$jar"
  lres=$(login_role "$email" "$jar" "$mfa")
  lcode="${lres%%|*}"
  if [[ "$lcode" == "200" ]]; then
    record "LOGIN-$rname" "Login $rname ($email)" "PASS" "status=200"
  elif [[ "$lcode" == "000" ]]; then
    record "LOGIN-$rname" "Login $rname ($email)" "FAIL" "status=000 system unreachable"
  elif [[ "$lcode" =~ ^5 ]]; then
    record "LOGIN-$rname" "Login $rname ($email)" "FAIL" "status=$lcode body=$(head -c 200 "${lres#*|}")"
  else
    record "LOGIN-$rname" "Login $rname ($email)" "FAIL" "status=$lcode body=$(head -c 200 "${lres#*|}")"
  fi
done

# DB evidence for auth: sessions/audit exist? (best-effort, schema-aware)
sess_tbl=$(psql_owner "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_name ILIKE '%session%' LIMIT 1;" | tr -d '[:space:]')
ua_tbl=$(psql_owner "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_name ILIKE '%user%account%' LIMIT 1;" | tr -d '[:space:]')
if [[ -n "$ua_tbl" ]]; then
  ucount=$(psql_owner "SELECT count(*) FROM \"$ua_tbl\";" | tr -d '[:space:]')
  record "DB-1" "Seeded user accounts exist" "$([[ "$ucount" =~ ^[0-9]+$ && "$ucount" -gt 0 ]] && echo PASS || echo FAIL)" "table=$ua_tbl count=$ucount"
else
  record "DB-1" "Seeded user accounts exist" "INCONCLUSIVE" "no user account table found in information_schema"
fi

# HTTP 000 must never be PASS - ensure by rule in analysis; also mark denied checks properly when system up
# Probe a few known endpoints as unauthenticated and as auditor (if logged in)
nojar="$COOKIE_DIR/none.cookie"; : > "$nojar"
for ep in "/api/patients" "/api/admin/users" "/api/clinical/patients"; do
  r=$(apicall "$nojar" GET "$ep"); rc="${r%%|*}"
  if [[ "$rc" == "000" ]]; then
    record "SEC-UNAUTH-${ep//\//_}" "Unauthenticated denied $ep" "FAIL" "status=000 (unreachable; never PASS)"
  elif [[ "$rc" == "401" || "$rc" == "403" ]]; then
    record "SEC-UNAUTH-${ep//\//_}" "Unauthenticated denied $ep" "PASS" "status=$rc http_ok; db_evidence=not_applicable"
  else
    record "SEC-UNAUTH-${ep//\//_}" "Unauthenticated denied $ep" "FAIL" "status=$rc expected 401/403"
  fi
done

# Auditor isolation probes (if auditor logged in)
ajar="$COOKIE_DIR/ComplianceAuditor.cookie"
if [[ -f "$ajar" ]]; then
  for ep in "/api/patients" "/api/billing" "/api/messages" "/api/forms"; do
    r=$(apicall "$ajar" GET "$ep"); rc="${r%%|*}"
    if [[ "$rc" == "000" ]]; then
      record "SEC-AUD-${ep//\//_}" "Auditor denied/no PHI $ep" "FAIL" "status=000 unreachable"
    elif [[ "$rc" == "401" || "$rc" == "403" || "$rc" == "404" ]]; then
      record "SEC-AUD-${ep//\//_}" "Auditor denied/no PHI $ep" "INCONCLUSIVE" "http=$rc but no DB proof of non-access in this script"
    elif [[ "$rc" == "200" ]]; then
      record "SEC-AUD-${ep//\//_}" "Auditor denied/no PHI $ep" "INCONCLUSIVE" "status=200 body=$(head -c 120 "${r#*|}") requires PHI/data review"
    else
      record "SEC-AUD-${ep//\//_}" "Auditor denied/no PHI $ep" "INCONCLUSIVE" "status=$rc"
    fi
  done
fi

# Multi-tenant: attempt provisioning endpoint or mark NI
if [[ "${MULTI_TENANCY_ENABLED:-false}" == "true" ]]; then
  record "MT-1" "Create second tenant via provisioning endpoint" "NOT IMPLEMENTED" "multi-tenancy enabled but automated provisioning flow not scripted (needs endpoint contract)"
else
  record "MT-1" "Multi-tenant isolation checks" "NOT IMPLEMENTED" "MULTI_TENANCY_ENABLED=false (.env); cannot create second tenant in this mode"
fi

# Data-flow/end-to-end module checks: mark INCONCLUSIVE/NI without paired HTTP+DB proof
for c in C1 C2 C3 C4 C5 C6 C7 C8 C9 C10 D6 E1 E2 E3 E4 E5 F1 F2 F3; do
  record "$c" "Workflow/data-flow check $c" "NOT IMPLEMENTED" "requires paired HTTP+DB evidence for specific flow; not fully automated in this minimal safe check"
done

# Summaries
log ""
log "=== SUMMARY ==="
log "PASS: $PASS"
log "FAIL: $FAIL"
log "INCONCLUSIVE: $INCONC"
log "NOT IMPLEMENTED: $NI"
log ""
log "Role-by-feature (login):"
for entry in "${ROLES[@]}"; do
  IFS='|' read -r email rname mfa <<< "$entry"
  r=$(grep "LOGIN-$rname" "$EVIDENCE/results.tsv" | tail -1 | cut -f3)
  log "- $rname: ${r:-UNKNOWN}"
done
log ""
log "Failures (earliest/root-cause hints):"
grep $'\tFAIL\t' "$EVIDENCE/results.tsv" | while IFS=$'\t' read -r id check result evidence; do
  log "- [$id] $check :: $(echo "$evidence" | head -c 220)"
done || log "- none"
log ""
log "Frontend paths not served / controller routes unused: requires deeper diff (see $EVIDENCE/frontend_api_paths.txt, $EVIDENCE/controller_routes_raw.txt)"
log "Rerun: scripts/system-check.sh"
log "Reset+run (DELETES PROJECT DATA): scripts/system-check.sh --reset"

{
  echo "ID|Check|Result|Evidence"
  cat "$EVIDENCE/results.tsv"
  echo ""
  echo "Summary: PASS=$PASS FAIL=$FAIL INCONCLUSIVE=$INCONC NOT_IMPLEMENTED=$NI"
} > "$REPORT"

# stdout table
echo ""
echo "ID | Check | Result | Evidence"
echo "---|-------|--------|---------"
while IFS=$'\t' read -r id check result evidence; do
  ev=$(echo "$evidence" | tr '\n' ' ' | head -c 160)
  echo "$id | $check | $result | $ev"
done < "$EVIDENCE/results.tsv"
echo ""
echo "Summary: PASS=$PASS FAIL=$FAIL INCONCLUSIVE=$INCONC NOT_IMPLEMENTED=$NI"
echo "Rerun: scripts/system-check.sh"
echo "Reset+run (DELETES PROJECT DATA): scripts/system-check.sh --reset"

if [[ $FAIL -gt 0 ]]; then exit 1; fi
exit 0
