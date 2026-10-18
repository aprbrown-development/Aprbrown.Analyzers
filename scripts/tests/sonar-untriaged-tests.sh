#!/usr/bin/env bash
#
# Tests for the listing of firing SonarQube rules that have no Sonar verdict.
#
# sonar-untriaged.sh is a text parser over the spec's verdict table and a comparison against what
# the server reports, and a parser that quietly matches nothing reads every firing rule as triaged
# or every one as a newcomer. Each case runs the script whole, with a fake curl first on PATH that
# serves canned API responses, so this needs no network and no SonarQube.

# No `set -e`: several cases run the script expecting it to exit non-zero, and the exit code is read
# explicitly instead.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNTRIAGED="$REPO_ROOT/scripts/sonar-untriaged.sh"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASSED=0
FAILED=0

ok()      { PASSED=$((PASSED + 1)); echo "  ok: $1"; }
not_ok()  {
  FAILED=$((FAILED + 1))
  echo "NOT OK: $1" >&2
  if [ $# -gt 1 ]; then echo "        $2" >&2; fi
}

SPEC="$WORK/spec.md"
API="$WORK/api"
CURL_LOG="$WORK/curl.log"
TOKEN='squ_test_token_0123456789'
mkdir -p "$WORK/bin" "$API"

# The fake curl answers a request for <host>/api/<endpoint>?…componentKeys=<key>… from
# $API/<endpoint with / as ->[-<key>].json, fails as curl -f does on a missing file, and logs its
# arguments and stdin so a case can check where the token went.
cat > "$WORK/bin/curl" <<'FAKE'
#!/usr/bin/env bash
url=""
for arg in "$@"; do
  case "$arg" in http*) url="$arg" ;; esac
done
{ printf 'args: %s\n' "$*"; printf 'stdin: %s\n' "$(cat)"; } >> "$CURL_LOG"
case "$url" in "$SONAR_HOST_URL"/api/*) ;; *) echo "fake curl: unexpected host in $url" >&2; exit 6 ;; esac
endpoint="${url#"$SONAR_HOST_URL"/api/}"
query="${endpoint#*\?}"
endpoint="${endpoint%%\?*}"
file="$API/${endpoint//\//-}"
case "&$query" in *"&componentKeys="*) key="${query#*componentKeys=}"; file="$file-${key%%&*}" ;; esac
[ -f "$file.json" ] || { echo "fake curl: no canned response $file.json" >&2; exit 22; }
cat "$file.json"
FAKE
chmod +x "$WORK/bin/curl"

run_untriaged() {
  PATH="$WORK/bin:$PATH" CURL_LOG="$CURL_LOG" API="$API" \
    SONAR_HOST_URL='http://sonar.test' SONAR_TOKEN="$TOKEN" \
    "$UNTRIAGED" "$SPEC"
}

# Run the script and require the exit code given and every line given in its output, each exactly
# as it should be printed.
assert_run() {
  local want_rc="$1" name="$2"; shift 2
  local out rc want
  : > "$CURL_LOG"
  out="$(run_untriaged 2>&1)"; rc=$?
  if [ "$rc" -ne "$want_rc" ]; then
    not_ok "$name" "expected exit $want_rc, got $rc; output: $out"
    return
  fi
  for want in "$@"; do
    if ! grep -qxF -- "$want" <<< "$out"; then
      not_ok "$name" "exited $rc, but did not print [$want]; output: $out"
      return
    fi
  done
  ok "$name"
}

# Write the spec with a section 2.5 verdict table holding one row per `<rule> <verdict>` given,
# between sections whose own tables must not be read as verdicts.
write_spec() {
  {
    printf '%s\n' '### 2.4 Something else' '' '| Sonar rule | Verdict |' '|---|---|' \
      '| `S9001` Not a verdict | covered |' '' '### 2.5 SonarQube verdicts' '' \
      '| Sonar rule | Verdict | Covering rule or reason |' '|---|---|---|'
    local row
    for row in "$@"; do
      printf '| `%s` A rule | %s | A reason |\n' "${row% *}" "${row#* }"
    done
    printf '%s\n' '' '---' '' '## 3. The APB analyzers' '' '| Sonar rule | Verdict |' '|---|---|' \
      '| `S9002` Not a verdict either | covered |'
  } > "$SPEC"
}

# Serve the given project keys from the project listing.
write_projects() {
  jq -n -c '$ARGS.positional | {
      paging: {pageIndex: 1, pageSize: 500, total: length},
      components: map({key: ., name: ., qualifier: "TRK"})
    }' --args "$@" > "$API/components-search.json"
}

# Serve a project's open issues as a rules facet of `<rule key> <count> <name>` values.
write_issues() {
  local project="$1"; shift
  local value key count name
  for value in "$@"; do
    read -r key count name <<< "$value"
    jq -n -c --arg key "$key" --argjson count "$count" --arg name "$name" \
      '{key: $key, count: $count, name: $name}'
  done | jq -s -c '{
      total: (map(.count) | add // 0), p: 1, ps: 1, issues: [],
      rules: map({key, name, lang: "cs"}),
      facets: [{property: "rules", values: map({val: .key, count})}]
    }' > "$API/issues-search-$project.json"
}

# --- every firing rule has a verdict -------------------------------------------------------------
write_spec 'S107 rejected' 'S2699 signal'
write_projects Alpha
write_issues Alpha 'csharpsquid:S107 6 Methods should not have too many parameters' \
  'csharpsquid:S2699 2 Tests should include assertions'
assert_run 0 'no untriaged rule exits 0' 'Untriaged rules: none'

# --- a newcomer --------------------------------------------------------------------------------
write_projects Alpha Beta
write_issues Alpha 'csharpsquid:S107 6 Methods should not have too many parameters' \
  'csharpsquid:S4242 3 A brand new rule'
write_issues Beta 'csharpsquid:S4242 1 A brand new rule'
assert_run 1 'a firing rule with no row is named with its count per project' \
  'Untriaged rules:' \
  '  S4242 A brand new rule' \
  '    Alpha: 3' \
  '    Beta: 1'

# --- a covered rule that still fires -------------------------------------------------------------
write_spec 'S107 rejected' 'S3358 covered' 'S6966 covered'
write_projects Alpha
write_issues Alpha 'csharpsquid:S107 6 Methods should not have too many parameters' \
  'csharpsquid:S3358 3 Ternary operators should not be nested'
assert_run 0 'a covered rule with open issues is listed as a gap, not untriaged' \
  'Untriaged rules: none' \
  'Covered rules with open issues:' \
  '  S3358 Ternary operators should not be nested' \
  '    Alpha: 3'

# --- only C# rules count -----------------------------------------------------------------------
write_spec 'S107 rejected'
write_issues Alpha 'csharpsquid:S107 6 Methods should not have too many parameters' \
  'Web:S7927 5 Visible label text should be part of the accessible name' \
  'css:S4666 1 Selectors should not be duplicated'
assert_run 0 'a firing rule in another language is not untriaged' 'Untriaged rules: none'

# --- only section 2.5 is the table ----------------------------------------------------------------
# write_spec puts S9001 and S9002 rows in the sections either side; neither is a verdict.
write_issues Alpha 'csharpsquid:S9001 1 Before the table' 'csharpsquid:S9002 1 After the table'
assert_run 1 'a row outside section 2.5 is not a verdict' \
  '  S9001 Before the table' \
  '  S9002 After the table'

# --- the token -----------------------------------------------------------------------------------
write_issues Alpha 'csharpsquid:S107 6 Methods should not have too many parameters'
: > "$CURL_LOG"
out="$(run_untriaged 2>&1)"
if grep -qF -- "$TOKEN" <<< "$out"; then
  not_ok 'the token is not printed' "output: $out"
elif grep '^args: ' "$CURL_LOG" | grep -qF -- "$TOKEN"; then
  not_ok 'the token is not printed' "curl was handed it as an argument: $(cat "$CURL_LOG")"
elif ! grep '^stdin: ' "$CURL_LOG" | grep -qF -- "user = \"$TOKEN:\""; then
  not_ok 'the token is not printed' "curl was not handed it on stdin: $(cat "$CURL_LOG")"
else
  ok 'the token reaches curl on stdin and is neither an argument nor printed'
fi

# --- refusals ------------------------------------------------------------------------------------
printf '%s\n' '### 2.5 SonarQube verdicts' '' 'No table here.' > "$SPEC"
assert_run 2 'a spec with no verdict rows is refused'

write_spec 'S107 rejected'
write_projects Alpha Beta
write_issues Beta 'csharpsquid:S107 1 Methods should not have too many parameters'
rm "$API/issues-search-Alpha.json"
assert_run 2 'a failed request for a project that is not the last is refused'

write_projects Beta Alpha
assert_run 2 'a failed request for the last project is refused'

rm "$API/components-search.json"
assert_run 2 'a failed project listing is refused'

out="$(PATH="$WORK/bin:$PATH" SONAR_HOST_URL='http://sonar.test' "$UNTRIAGED" "$SPEC" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ]; then
  ok 'a missing SONAR_TOKEN is refused'
else
  not_ok 'a missing SONAR_TOKEN is refused' "expected exit 2, got $rc; output: $out"
fi

echo
echo "sonar-untriaged tests: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
