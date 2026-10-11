#!/usr/bin/env bash
#
# Tests for the comparison between a config and the expected ruleset (#51).
#
# ruleset-diff.sh is what verify-package.sh asks whether the packed config is the house ruleset, so
# a comparison that passes something it should refuse would wave a wrong ruleset through every gate
# downstream of it. Every case here is a canned pair of small files, so this runs in milliseconds
# and needs neither the SDK nor a package.

# No `set -e`, for the reason release-gate-tests.sh gives: several cases here run the script
# expecting it to exit non-zero, and the exit code is read explicitly instead.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIFF="$REPO_ROOT/scripts/ruleset-diff.sh"

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

CONFIG="$WORK/config"
EXPECTED="$WORK/expected"

# Run the comparison over $CONFIG and $EXPECTED and require it to agree they are the same ruleset.
assert_same() {
  local name="$1" out rc
  out="$("$DIFF" "$CONFIG" "$EXPECTED" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "$name"
  else
    not_ok "$name" "expected exit 0, got $rc; output: $out"
  fi
}

# Run the comparison and require a refusal whose output names every line given, each exactly as it
# should be printed. Naming the line is the point of the module: a refusal that only says "differs"
# is the digest mismatch it replaced.
assert_differs() {
  local name="$1"; shift
  local out rc want
  out="$("$DIFF" "$CONFIG" "$EXPECTED" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ]; then
    not_ok "$name" "expected a refusal, but the script exited 0; output: $out"
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

# --- the same ruleset ----------------------------------------------------------------------------
printf '%s\n' \
  'is_global = true' \
  'dotnet_analyzer_diagnostic.severity = none' \
  'dotnet_diagnostic.MA0001.severity = warning' > "$CONFIG"
cp "$CONFIG" "$EXPECTED"
assert_same 'a config identical to the expected ruleset passes'

# --- a rule the expected ruleset does not name ---------------------------------------------------
# The change ADR-0004 calls a minor bump: one rule switched on. Made to the config alone, it is the
# drift this comparison exists to catch, and the refusal has to say which rule.
cp "$EXPECTED" "$CONFIG"
echo 'dotnet_diagnostic.MA0099.severity = warning' >> "$CONFIG"
assert_differs 'a rule in the config but not expected is named with +' \
  '+ dotnet_diagnostic.MA0099.severity = warning'

# --- an expected rule the config has lost ---------------------------------------------------------
# The direction ADR-0004 calls major: a rule a consumer may hold a deviation for, gone.
printf '%s\n' \
  'is_global = true' \
  'dotnet_analyzer_diagnostic.severity = none' > "$CONFIG"
assert_differs 'an expected rule missing from the config is named with -' \
  '- dotnet_diagnostic.MA0001.severity = warning'

# --- a rule re-levelled --------------------------------------------------------------------------
# The silent downgrade §2.3 forbids — a vendor's Error recorded at warning — shows as the pair, so
# a reader sees both what the line was meant to say and what it says.
printf '%s\n' \
  'is_global = true' \
  'dotnet_analyzer_diagnostic.severity = none' \
  'dotnet_diagnostic.MA0001.severity = error' > "$CONFIG"
assert_differs 'a re-levelled rule is named as the expected line and the actual one' \
  '- dotnet_diagnostic.MA0001.severity = warning' \
  '+ dotnet_diagnostic.MA0001.severity = error'

# --- a line enumerated twice ---------------------------------------------------------------------
# A set comparison would swallow this, and so would a count that a second typo happened to balance.
# The comparison is over a multiset, so the second copy is a line the expected ruleset lacks.
cp "$EXPECTED" "$CONFIG"
echo 'dotnet_diagnostic.MA0001.severity = warning' >> "$CONFIG"
assert_differs 'a line enumerated twice is named with +' \
  '+ dotnet_diagnostic.MA0001.severity = warning'

# --- only the settings count ---------------------------------------------------------------------
# The expected ruleset is grouped and explained by comments the shipped config does not share, and
# hand-written spacing around = varies. Neither is a difference in the ruleset.
printf '%s\n' \
  '# a comment the expected ruleset does not carry' \
  'is_global=true' \
  '' \
  '   dotnet_analyzer_diagnostic.severity   =   none   ' \
  '  # an indented comment' \
  'dotnet_diagnostic.MA0001.severity= warning' > "$CONFIG"
printf '%s\n' \
  '# --- the blanket ---' \
  'is_global = true' \
  'dotnet_analyzer_diagnostic.severity = none' \
  '' \
  '# --- Meziantou ---' \
  'dotnet_diagnostic.MA0001.severity = warning' > "$EXPECTED"
assert_same 'comments, blank lines and spacing around = are not differences'

echo
echo "ruleset-diff tests: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
