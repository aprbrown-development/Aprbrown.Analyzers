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
# At this stage the shipped config carries APB0001-APB0003 and the Meziantou tier, so the
# assertions are:
#   - the produced .nupkg matches the §4.2 allowlist exactly;
#   - both MSBuild property defaults (EnforceCodeStyleInBuild, TreatWarningsAsErrors) arrive —
#     a silent failure otherwise (#2 hazard H1);
#   - the packed config's Meziantou tier matches the derivation in spec §2.3 step 1, checked
#     against the packed file rather than the source tree so what is asserted is what ships;
#   - every rule in EXPECTED_RULES fires against the fixture, and TreatWarningsAsErrors promotes
#     them to build errors;
#   - severities survive the trip: a rule Meziantou defaults to Error arrives as an error and a
#     Warning one as a warning, measured in a second build with TreatWarningsAsErrors=false where
#     the two are still distinguishable;
#   - MA0004 stays silent against a construct that violates it, with the assembly loaded;
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
# Read one entry out of the package to a file. Failure here is a real failure — under
# `set -e` an unguarded unzip would kill the script with its own exit code and no explanation —
# so every caller passes the message it wants on the way out.
read_entry() {
  local nupkg="$1" entry="$2" dest="$3"
  if command -v unzip >/dev/null 2>&1; then
    unzip -p "$nupkg" "$entry" > "$dest" 2>/dev/null
  else
    python3 -c "import zipfile,sys; open(sys.argv[3],'wb').write(zipfile.ZipFile(sys.argv[1]).read(sys.argv[2]))" \
      "$nupkg" "$entry" "$dest" 2>/dev/null
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

# --- Assertion: the packed config's Meziantou tier matches the derivation --------------------
# Read out of the .nupkg, not out of src/: the source file is what was written, the packed one is
# what a consumer receives, and only the second is worth asserting on. The derivation is spec §2.3
# step 1 — every Meziantou.Analyzer 3.0.123 rule that is enabled by default at a build-gating
# severity, minus MA0004, plus MA0032 — and it is frozen, so these numbers are constants rather
# than something to recompute. Changing one is a deliberate act under ADR-0004, and this assertion
# is what makes it deliberate.
PACKED_CONFIG="$WORK/packed.globalconfig"
read_entry "$NUPKG" 'build/Aprbrown.Analyzers.globalconfig' "$PACKED_CONFIG" \
  || fail "could not read build/Aprbrown.Analyzers.globalconfig out of the package"
[ -s "$PACKED_CONFIG" ] || fail "the packed build/Aprbrown.Analyzers.globalconfig is empty"

# A category re-enable admits default-off rules and rules upstream adds later, which is exactly
# what the seal exists to prevent (ADR-0002 decision 4). Nothing in the shipped config may use one.
if grep -qE '^[[:space:]]*dotnet_analyzer_diagnostic\.category-' "$PACKED_CONFIG"; then
  grep -nE '^[[:space:]]*dotnet_analyzer_diagnostic\.category-' "$PACKED_CONFIG" >&2
  fail "the shipped config re-enables by category; the seal only holds when every rule is enumerated by ID"
fi
info "no category re-enable in the shipped config"

MA_COUNT="$(grep -cE '^dotnet_diagnostic\.MA[0-9]+\.severity' "$PACKED_CONFIG" || true)"
[ "$MA_COUNT" -eq 103 ] \
  || fail "expected 103 Meziantou rules (103 default-on, minus MA0004, plus MA0032), found $MA_COUNT"
info "Meziantou tier enumerates 103 rules"

# The count alone is a weak seal: a mistyped or duplicated ID still totals 103 and passes every
# assertion around this one. So pin the tier's identity — which rules, at which severities — with a
# digest over the sorted severity lines. The derivation is frozen, so this is a constant; when a
# rule is deliberately added, removed or re-levelled under ADR-0004, regenerate it with
#   grep -E '^dotnet_diagnostic\.MA[0-9]+\.severity' <config> | LC_ALL=C sort | sha256sum
# and let the diff on this line be the thing a reviewer sees.
MA_DIGEST_EXPECTED="0731fdb59880741fcbe3c7d59bb919ee4e4350427f95bba553c86e62b21f2649"
MA_DIGEST_ACTUAL="$(grep -E '^dotnet_diagnostic\.MA[0-9]+\.severity' "$PACKED_CONFIG" \
  | LC_ALL=C sort | sha256sum | cut -d' ' -f1)"
if [ "$MA_DIGEST_ACTUAL" != "$MA_DIGEST_EXPECTED" ]; then
  echo "--- expected $MA_DIGEST_EXPECTED ---" >&2
  echo "--- actual   $MA_DIGEST_ACTUAL ---"   >&2
  fail "the Meziantou tier is not the frozen derivation; if the change was deliberate, update MA_DIGEST_EXPECTED"
fi
info "Meziantou tier matches the frozen derivation digest"

if grep -qE '^dotnet_diagnostic\.MA0004\.severity' "$PACKED_CONFIG"; then
  fail "MA0004 is enumerated; it is the sole universality-test exclusion and must stay out"
fi
grep -qE '^dotnet_diagnostic\.MA0032\.severity[[:space:]]*=[[:space:]]*warning$' "$PACKED_CONFIG" \
  || fail "MA0032 is missing or not at warning; it is default-off upstream and added deliberately"
info "MA0004 excluded, MA0032 present at warning"

# The three rules Meziantou defaults to Error. Recording them at warning would be a silent
# downgrade of the vendor's own judgement, which §2.3 forbids: read the severity, do not assume it.
for RULE in MA0037 MA0039 MA0049; do
  grep -qE "^dotnet_diagnostic\.$RULE\.severity[[:space:]]*=[[:space:]]*error$" "$PACKED_CONFIG" \
    || fail "$RULE is not recorded at error; the vendor defaults it to Error and the config must say so"
done
info "the three Error-severity Meziantou rules are recorded at error"

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
# The three Meziantou rows are a deliberate sample rather than all 103 — one ordinary member of the
# default-on Warning sweep (MA0026), the one rule added against a vendor default of off (MA0032),
# and one of the three the vendor defaults to Error (MA0037). They fire only because a config
# shipped by this package bound to an assembly this package does not depend on, which is the claim
# under test. What the other 100 get is weaker and worth naming: the digest above pins that they
# are present at the right severities, but nothing here observes them fire. Proving 103 rules
# behaviourally would mean 103 violations in the fixture, and the binding they would each
# re-demonstrate is the same one these three already establish.
EXPECTED_RULES=(APB0001 APB0002 APB0003 MA0026 MA0032 MA0037)

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

# --- Assertion: the packed config is the only global config in play -------------------------
# Since #12 the repository root's Directory.Build.props injects this same config file into this
# repository's own projects (spec §7), and tests/Fixture.Consumer/Directory.Build.props exists to
# keep the fixture out of it. If that isolation ever breaks, both copies reach the consumer, every
# shared key is unset with MultipleGlobalAnalyzerKeys, and the blanket goes with them — the seal
# disappears and third-party rules run at vendor defaults. Every assertion above still passes,
# because the APB rules keep firing at their descriptor default severity. Measured, not theorised.
if grep -q 'MultipleGlobalAnalyzerKeys' "$BUILD_LOG"; then
  grep 'MultipleGlobalAnalyzerKeys' "$BUILD_LOG" >&2
  fail "a second global analyzer config collided with the packed one; the shipped config's keys were unset"
fi
info "the packed config is the only global analyzer config in the consumer build"

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

# --- Assertion: severities survive the trip, and MA0004 stays silent ------------------------
# The build above cannot answer either question, because TreatWarningsAsErrors flattens every
# diagnostic to "error": a rule the config had wrongly downgraded from error to warning would
# print identically to one it recorded faithfully. So build once more with the promotion off,
# where the compiler prints each rule at its configured severity and the two are told apart. The
# build still fails — MA0037 is an error in its own right — but nothing here depends on its exit
# code, only on what it printed.
SEVERITY_LOG="$WORK/severity.log"
set +e
dotnet build "$FIXTURE" --no-restore \
  -p:AprbrownAnalyzersVersion="$VERSION" -p:TreatWarningsAsErrors=false \
  -t:Rebuild --nologo -v quiet > "$SEVERITY_LOG" 2>&1
set -e

# MA0037 is one of the three Meziantou defaults to Error; MA0026 is an ordinary Warning. Both come
# from the same fixture file, so "the config recorded what the vendor assigns" is the only thing
# that can explain the two landing differently.
grep -q 'error MA0037' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "MA0037 did not arrive as an error; the vendor's Error severity was flattened"; }
grep -q 'warning MA0026' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "MA0026 did not arrive as a warning"; }
info "vendor severities survived: MA0037 as error, MA0026 as warning"

# MA0004 is the sole universality-test exclusion (ADR-0002 decision 2). Kettle.BoilAsync awaits
# without ConfigureAwait, which is exactly what MA0004 reports, and MA0032 firing on that same
# await proves the assembly is loaded and analysing the expression. So this silence is the
# exclusion holding, not the analyzer being absent — which is the only way the assertion is worth
# anything.
grep -q 'MA0032' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "MA0032 did not fire, so the MA0004 assertion below would prove nothing"; }
if grep -q 'MA0004' "$SEVERITY_LOG"; then
  grep 'MA0004' "$SEVERITY_LOG" >&2
  fail "MA0004 fired; it is deliberately not enumerated and must stay off"
fi
info "MA0004 stayed silent against a violating await, with the analyzer loaded"

echo "SMOKE PASS"
