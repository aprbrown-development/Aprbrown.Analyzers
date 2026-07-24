#!/usr/bin/env bash
#
# Pack-consume smoke test (spec §6, ADR-0003 decision 5).
#
# Analyzer unit tests test the analyzer; they cannot test the *package* — a package can build
# clean and enforce nothing (research produced exactly that). This gate packs Aprbrown.Analyzers
# to a temporary feed, restores tests/Fixture.Consumer against it, builds, and asserts an exact
# diagnostic set. It runs on a dev machine and in CI alike, and exits non-zero on any failed
# assertion.
#
# At this stage the shipped config carries APB0001, APB0002 and APB0003, so the assertions are:
#   - the produced .nupkg matches the §4.2 allowlist exactly;
#   - both MSBuild property defaults (EnforceCodeStyleInBuild, TreatWarningsAsErrors) arrive —
#     a silent failure otherwise (#2 hazard H1);
#   - every rule in EXPECTED_RULES fires against the fixture, and TreatWarningsAsErrors promotes
#     them to build errors;
#   - every packed analyzer assembly loads in the consumer. The code fix assembly references
#     Workspaces, which a command-line build does not host, so "does it load outside the IDE" is a
#     real question and CS8032/CS8034 is how the compiler answers it — as a warning, which is the
#     #2 hazard shape all over again.
# Later tickets add rows to EXPECTED_RULES as the config enumerates more rules back on.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_PROJECT="$REPO_ROOT/src/Aprbrown.Analyzers.Package/Aprbrown.Analyzers.Package.csproj"
FIXTURE="$REPO_ROOT/tests/Fixture.Consumer/Fixture.Consumer.csproj"
VERSION="0.0.1-smoke"

WORK="$(mktemp -d)"
FEED="$WORK/feed"
CONFIG="$WORK/nuget.config"
BUILD_LOG="$WORK/build.log"
# Isolate the package cache so a rebuilt package under the same version is never served stale.
export NUGET_PACKAGES="$WORK/packages"
trap 'rm -rf "$WORK"' EXIT

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
info() { echo "  smoke: $*"; }

mkdir -p "$FEED"

cat > "$CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smoke-feed" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

# --- Pack -----------------------------------------------------------------------------------
info "packing $VERSION -> temp feed"
dotnet pack "$PKG_PROJECT" -c Release -o "$FEED" -p:Version="$VERSION" --nologo -v quiet \
  || fail "pack failed"

NUPKG="$FEED/Aprbrown.Analyzers.$VERSION.nupkg"
[ -f "$NUPKG" ] || fail "expected package not produced: $NUPKG"

# --- Assertion: packed contents match the §4.2 allowlist exactly ----------------------------
list_entries() {
  if command -v unzip >/dev/null 2>&1; then
    unzip -Z1 "$1"
  else
    python3 -c "import zipfile,sys; print('\n'.join(zipfile.ZipFile(sys.argv[1]).namelist()))" "$1"
  fi
}
# EXPECTED is the §4.2 allowlist. It grows in lockstep with what the package ships: add a row here
# whenever the package gains a file, or this exact-match assertion will fail.
# Drop the standard OPC/NuGet metadata; what remains is the content allowlist.
ACTUAL="$(list_entries "$NUPKG" \
  | grep -vE '^(_rels/|package/|\[Content_Types\]\.xml$)' \
  | grep -vE '\.nuspec$' \
  | LC_ALL=C sort)"
EXPECTED="$(printf '%s\n' \
  'README.md' \
  'analyzers/dotnet/cs/Aprbrown.Analyzers.dll' \
  'analyzers/dotnet/cs/Aprbrown.Analyzers.CodeFixes.dll' \
  'build/Aprbrown.Analyzers.globalconfig' \
  'build/Aprbrown.Analyzers.props' \
  | LC_ALL=C sort)"
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "--- expected allowlist ---" >&2; echo "$EXPECTED" >&2
  echo "--- actual contents ---"    >&2; echo "$ACTUAL"   >&2
  fail "packed contents do not match the §4.2 allowlist"
fi
info "package contents match the §4.2 allowlist"

# --- Restore fixture against the temp feed --------------------------------------------------
info "restoring fixture against temp feed"
dotnet restore "$FIXTURE" \
  --configfile "$CONFIG" \
  -p:AprbrownAnalyzersVersion="$VERSION" \
  --nologo -v quiet \
  || fail "fixture restore failed"

# --- Assertion: both MSBuild property defaults arrive ---------------------------------------
# --getProperty evaluates without running the build, so APB0001-as-error does not interfere.
get_prop() {
  dotnet build "$FIXTURE" --no-restore --getProperty:"$1" \
    -p:AprbrownAnalyzersVersion="$VERSION" 2>/dev/null | tr -d '[:space:]'
}
EIB="$(get_prop EnforceCodeStyleInBuild)"
TWAE="$(get_prop TreatWarningsAsErrors)"
info "EnforceCodeStyleInBuild=$EIB  TreatWarningsAsErrors=$TWAE"
[ "$EIB" = "true" ]  || fail "EnforceCodeStyleInBuild did not arrive as true (got '$EIB')"
[ "$TWAE" = "true" ] || fail "TreatWarningsAsErrors did not arrive as true (got '$TWAE')"

# --- Assertion: every enumerated rule fires and fails the build ------------------------------
# One row per rule the shipped config switches on, each with a matching case in the fixture.
EXPECTED_RULES=(APB0001 APB0002 APB0003)

set +e
dotnet build "$FIXTURE" --no-restore \
  -p:AprbrownAnalyzersVersion="$VERSION" --nologo -v quiet > "$BUILD_LOG" 2>&1
BUILD_RC=$?
set -e

for RULE in "${EXPECTED_RULES[@]}"; do
  if ! grep -q "$RULE" "$BUILD_LOG"; then
    cat "$BUILD_LOG" >&2
    fail "$RULE did not fire in the fixture build"
  fi
  info "$RULE fired"
done
# --- Assertion: no packed analyzer assembly failed to load ----------------------------------
if grep -qE 'CS8032|CS8034' "$BUILD_LOG"; then
  grep -E 'CS8032|CS8034' "$BUILD_LOG" >&2
  fail "an analyzer assembly failed to load in the consumer build"
fi
info "every packed analyzer assembly loaded"

if [ "$BUILD_RC" -eq 0 ]; then
  cat "$BUILD_LOG" >&2
  fail "fixture build succeeded but should have failed on the enumerated rules as errors"
fi
info "TreatWarningsAsErrors failed the build as expected"

echo "SMOKE PASS"
