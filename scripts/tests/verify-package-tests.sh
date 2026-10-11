#!/usr/bin/env bash
#
# Tests for the pack-consume smoke test's interface (ADR-0003 amendment 3).
#
# verify-package.sh inspects the package it is handed rather than packing its own, which is what
# makes the package a release pushes the package that was gated. It also makes the script testable
# for the first time: it can be handed a package that is deliberately broken, and must refuse it.
#
# Only the cheap refusals are pinned here — the ones that fail before any fixture build, so this
# file runs in seconds and needs no SDK. The packages are synthetic zips rather than real packs:
# every case below is refused on entry names or packed config text, which a synthetic package carries as
# faithfully as a real one. A synthetic package would of course fail *some* later assertion too,
# so each case checks the failure message as well as the exit code — a refusal for the wrong
# reason is not a pass. The positive case is the full run against a real package, which ci.yml
# makes in its smoke step.

# No `set -e`, for the reason release-gate-tests.sh gives: every case here runs the script expecting
# it to exit non-zero, and the exit code is read explicitly instead.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERIFY="$REPO_ROOT/scripts/verify-package.sh"

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

# Run the script and require both a non-zero exit and a failure message naming the check that was
# meant to refuse the package.
assert_refused() {
  local want_message="$1" name="$2"; shift 2
  local out rc
  out="$("$VERIFY" "$@" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ]; then
    not_ok "$name" "expected a refusal, but the script exited 0; output: $out"
  elif ! grep -qF "$want_message" <<< "$out"; then
    not_ok "$name" "exited $rc, but not with [$want_message]; output: $out"
  else
    ok "$name"
  fi
}

# --- the argument ------------------------------------------------------------------------------
assert_refused 'usage: verify-package.sh <path-to.nupkg>' \
  'no package argument is refused rather than falling back to packing one'

assert_refused 'no package at' \
  'a path with no file behind it is refused' \
  "$WORK/does-not-exist.nupkg"

# --- synthetic packages --------------------------------------------------------------------------
# make_nupkg <out.nupkg> <entry=source>... zips each source file in under its entry name. Python
# rather than zip(1), whose entry names follow the working directory; the smoke test itself already
# falls back to python3's zipfile, so this asks for nothing new of a dev machine.
make_nupkg() {
  local out="$1"; shift
  python3 -I -c '
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w") as z:
    for pair in sys.argv[2:]:
        entry, source = pair.split("=", 1)
        z.write(source, entry)
' "$out" "$@"
}

NUSPEC="$WORK/Aprbrown.Analyzers.nuspec"
printf '%s\n' \
  '<?xml version="1.0" encoding="utf-8"?>' \
  '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">' \
  '  <metadata>' \
  '    <id>Aprbrown.Analyzers</id>' \
  '    <version>0.0.1-test</version>' \
  '  </metadata>' \
  '</package>' > "$NUSPEC"
STUB="$WORK/stub"
echo 'stub' > "$STUB"

# The README a real package ships: docs/consuming.md, packed under that name (#42).
GOOD_README="$REPO_ROOT/docs/consuming.md"

# Every entry the §4.2 allowlist names, so a case can add one too many or swap one entry's content
# and be refused for exactly that.
ALLOWLISTED=(
  'Aprbrown.Analyzers.nuspec='"$NUSPEC"
  'analyzers/dotnet/cs/Aprbrown.Analyzers.dll='"$STUB"
  'analyzers/dotnet/cs/Aprbrown.Analyzers.CodeFixes.dll='"$STUB"
  'build/Aprbrown.Analyzers.globalconfig='"$STUB"
  'build/Aprbrown.Analyzers.props='"$STUB"
)

# --- the allowlist ------------------------------------------------------------------------------
# The shape #2 produced in the other direction: a pack glob that quietly widens. Nothing downstream
# of the allowlist would notice an extra file, so it is the allowlist's alone to refuse.
EXTRA_ENTRY="$WORK/extra-entry.nupkg"
make_nupkg "$EXTRA_ENTRY" "${ALLOWLISTED[@]}" "README.md=$GOOD_README" "build/stray.txt=$STUB"
assert_refused 'packed contents do not match the §4.2 allowlist' \
  'a package carrying a file the allowlist does not name is refused' \
  "$EXTRA_ENTRY"

# --- the expected ruleset --------------------------------------------------------------------------
# One rule switched on in the shipped config and not in the expected ruleset (#51): the change
# ADR-0004 calls a versioned act, made to one file only. MA0004 because it is the rule most worth
# refusing — the sole universality-test exclusion — and because it is not enumerated, so the line
# is new rather than a duplicate of one already there. The package carries the real config and
# README so every earlier check passes, and the refusal must name the rule rather than say "differs".
SHIPPED_CONFIG="$REPO_ROOT/src/Aprbrown.Analyzers.Package/build/Aprbrown.Analyzers.globalconfig"
if grep -qE '^[[:space:]]*dotnet_diagnostic\.MA0004\.' "$SHIPPED_CONFIG"; then
  not_ok 'the drifted config fixture adds a rule the shipped config does not enumerate' \
    "MA0004 is already enumerated in $SHIPPED_CONFIG; pick another rule for this case"
fi
DRIFTED_CONFIG="$WORK/drifted.globalconfig"
cp "$SHIPPED_CONFIG" "$DRIFTED_CONFIG"
echo 'dotnet_diagnostic.MA0004.severity = warning' >> "$DRIFTED_CONFIG"
DRIFTED_ENTRIES=(
  'Aprbrown.Analyzers.nuspec='"$NUSPEC"
  'analyzers/dotnet/cs/Aprbrown.Analyzers.dll='"$STUB"
  'analyzers/dotnet/cs/Aprbrown.Analyzers.CodeFixes.dll='"$STUB"
  'build/Aprbrown.Analyzers.globalconfig='"$DRIFTED_CONFIG"
  'build/Aprbrown.Analyzers.props='"$STUB"
  'README.md='"$GOOD_README"
)
DRIFTED="$WORK/drifted.nupkg"
make_nupkg "$DRIFTED" "${DRIFTED_ENTRIES[@]}"
assert_refused '+ dotnet_diagnostic.MA0004.severity = warning' \
  'a package whose config enumerates a rule the expected ruleset does not is refused, naming it' \
  "$DRIFTED"

echo
echo "verify-package tests: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
