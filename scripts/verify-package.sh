#!/usr/bin/env bash
#
# Pack-consume smoke test (spec §6, ADR-0003 decision 5 and amendment 3).
#
#   verify-package.sh <path-to.nupkg>
#
# Analyzer unit tests test the analyzer; they cannot test the *package* — a package can build
# clean and enforce nothing (research produced exactly that). This gate takes an already-packed
# Aprbrown.Analyzers package, places it alone in a temporary feed, restores tests/Fixture.Consumer
# against it, builds, and asserts an exact diagnostic set. It runs on a dev machine and in CI alike,
# and exits non-zero on any failed assertion.
#
# It never packs. Whoever calls it packs, at whatever version they mean to ship, so release.yml
# gates the very file it then pushes rather than a sibling packed for the purpose (amendment 3). The
# version the fixture restores comes from the package's own .nuspec. On a dev machine:
#
#   dotnet pack src/Aprbrown.Analyzers.Package/Aprbrown.Analyzers.Package.csproj -c Release \
#     -o artifacts -p:Version=0.0.1-smoke
#   ./scripts/verify-package.sh artifacts/Aprbrown.Analyzers.0.0.1-smoke.nupkg
#
# The shipped config is complete as of spec §2.3 step 4 — APB0001-APB0004, the Meziantou tier, the
# SDK CA tier, StyleCop's four ordering rules and the IDE, naming and style tier, all under the
# blanket — so the assertions are:
#   - the given .nupkg matches the §4.2 allowlist exactly;
#   - both MSBuild property defaults (EnforceCodeStyleInBuild, TreatWarningsAsErrors) arrive —
#     a silent failure otherwise (#2 hazard H1);
#   - the packed config is exactly the expected ruleset (tests/expected-ruleset.txt), checked
#     against the packed file rather than the source tree so what is asserted is what ships, with
#     any difference named line by line — and it keeps the sealed allowlist's shape: the blanket
#     first, no category re-enable, no inline :severity, and no severity outside dotnet_diagnostic
#     but the blanket's and the naming rules';
#   - neither AnalysisMode nor AnalysisLevel is set anywhere in this repository or the package —
#     they cannot reach past the blanket, so setting one would document an intent the build does
#     not honour;
#   - the consumer fixture raises exactly the diagnostics its expectation markers declare — no
#     fewer and no more, by file, line, ID and severity (scripts/diagnostic-diff.sh) — in two
#     builds: once with the TreatWarningsAsErrors default, where every declared diagnostic must
#     arrive as an error and the build must fail, and once without it, where each must arrive at
#     the severity it ships at. A line or file with no marker must stay silent, so the seal's
#     silences (CA2007, CA1707, MA0004), the IDE-only IDE0003, and a failed analyzer load or
#     colliding global config are all asserted by the same comparison. Which file proves what,
#     and why, is written in the fixture.
#
# A new fixture case is a change to the fixture alone: the violating line and its marker.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURE_DIR="$REPO_ROOT/tests/Fixture.Consumer"
FIXTURE="$FIXTURE_DIR/Fixture.Consumer.csproj"

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
info() { echo "  smoke: $*"; }

# --- The package under test ------------------------------------------------------------------
# Required, with no fallback to packing one here: a fallback is exactly the sibling package
# amendment 3 removed, and it would be the path a forgotten argument quietly took.
[ $# -eq 1 ] || fail "usage: verify-package.sh <path-to.nupkg>"
GIVEN="$1"
[ -f "$GIVEN" ] || fail "no package at $GIVEN"

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

WORK="$(mktemp -d)"
FEED="$WORK/feed"
CONFIG="$WORK/nuget.config"
BUILD_LOG="$WORK/build.log"
# Isolate the package cache so a repacked package under the same version is never served stale.
export NUGET_PACKAGES="$WORK/packages"
trap 'rm -rf "$WORK"' EXIT

# The version is the package's own claim, read from its .nuspec — the same field NuGet resolves the
# fixture's reference against — rather than parsed out of a filename a caller is free to rename.
ENTRIES="$(list_entries "$GIVEN" 2>/dev/null)" || fail "$GIVEN is not a readable package"
NUSPEC_ENTRY="$(awk '/^[^\/]+\.nuspec$/ { print; exit }' <<< "$ENTRIES")"
[ -n "$NUSPEC_ENTRY" ] || fail "$GIVEN carries no .nuspec at its root"
NUSPEC="$WORK/package.nuspec"
read_entry "$GIVEN" "$NUSPEC_ENTRY" "$NUSPEC" || fail "could not read $NUSPEC_ENTRY out of $GIVEN"
VERSION="$(awk 'match($0, /<version>[^<]*<\/version>/) {
  v = substr($0, RSTART + 9, RLENGTH - 19); gsub(/[[:space:]]/, "", v); print v; exit
}' "$NUSPEC")"
[ -n "$VERSION" ] || fail "$NUSPEC_ENTRY in $GIVEN declares no <version>"

# Copied, not pointed at: a feed is a directory, and the caller's directory may hold other packages
# (a dev's stale artifacts/) or other files (release.yml's release notes). The copy is
# byte-for-byte, so the package asserted on below is the package the caller goes on to push.
mkdir -p "$FEED" || fail "could not create the temp feed at $FEED"
NUPKG="$FEED/Aprbrown.Analyzers.$VERSION.nupkg"
cp "$GIVEN" "$NUPKG" || fail "could not copy $GIVEN into the temp feed"
info "verifying $GIVEN (version $VERSION)"

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

# --- Assertion: packed contents match the §4.2 allowlist exactly ----------------------------
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

# --- Assertion: the packed config is exactly the expected ruleset ----------------------------
# Read out of the .nupkg, not out of src/: the source file is what was written, the packed one is
# what a consumer receives, and only the second is worth asserting on.
PACKED_CONFIG="$WORK/packed.globalconfig"
read_entry "$NUPKG" 'build/Aprbrown.Analyzers.globalconfig' "$PACKED_CONFIG" \
  || fail "could not read build/Aprbrown.Analyzers.globalconfig out of the package"
[ -s "$PACKED_CONFIG" ] || fail "the packed build/Aprbrown.Analyzers.globalconfig is empty"

# Which rules are on, at which severity, with which options: one fact, recorded once more in
# tests/expected-ruleset.txt so that it has an expectation independent of the file under test
# (#51). Every derivation behind it is frozen (spec §2.3), so a difference is either drift or a
# deliberate change under ADR-0004 that was made to one file and not the other — and either way the
# lines that differ are what a failure prints, not a count or a digest that only says something
# did. Why individual lines are pinned as they are — the vendor Errors kept at error, MA0004, CA2007
# and CA1707 kept out — is recorded beside those lines in the expected ruleset.
EXPECTED_RULESET="$REPO_ROOT/tests/expected-ruleset.txt"
[ -s "$EXPECTED_RULESET" ] || fail "the expected ruleset $EXPECTED_RULESET is missing or empty"
if ! RULESET_DIFF="$("$REPO_ROOT/scripts/ruleset-diff.sh" "$PACKED_CONFIG" "$EXPECTED_RULESET" 2>&1)"; then
  echo "--- + only in the packed config, - only in tests/expected-ruleset.txt ---" >&2
  echo "$RULESET_DIFF" >&2
  fail "the packed config is not the expected ruleset; if the change was deliberate, make it in tests/expected-ruleset.txt too"
fi
info "the packed config is exactly the expected ruleset"

# The diff above says the two files agree; what follows says the shape they agree on is still a
# sealed allowlist (ADR-0002). These are claims about the shape rather than about the ruleset, so a
# change made deliberately to both files can still break them, and these are what refuse it.

# The blanket has to come first. Global config precedence does not depend on line order today, but
# a reader of the file does: the sealed allowlist reads as "everything off, then these back on".
# One awk over the file rather than a pipe into head, which pipefail turns into a SIGPIPE failure.
FIRST_SETTINGS="$(awk '
  { sub(/^[[:space:]]+/, ""); sub(/[[:space:]]+$/, "") }
  /^(#|$)/ { next }
  { sub(/[[:space:]]*=[[:space:]]*/, " = "); printf "%s|", $0; if (++n == 2) exit }
' "$PACKED_CONFIG")"
[ "$FIRST_SETTINGS" = "is_global = true|dotnet_analyzer_diagnostic.severity = none|" ] \
  || fail "the shipped config does not open with is_global and the blanket; found: $FIRST_SETTINGS"
info "the shipped config opens with the blanket"

# A category re-enable admits default-off rules and rules upstream adds later, which is exactly
# what the seal exists to prevent (ADR-0002 decision 4). Nothing in the shipped config may use one.
if grep -qE '^[[:space:]]*dotnet_analyzer_diagnostic\.category-' "$PACKED_CONFIG"; then
  grep -nE '^[[:space:]]*dotnet_analyzer_diagnostic\.category-' "$PACKED_CONFIG" >&2
  fail "the shipped config re-enables by category; the seal only holds when every rule is enumerated by ID"
fi
info "no category re-enable in the shipped config"

# `option = value:severity` is the .editorconfig shorthand that sets both at once, and the source
# repository used it throughout. It is not used here: a reader looking for what a rule is set to
# should have one place to look, and a shorthand severity sitting beside an explicit
# dotnet_diagnostic entry can quietly disagree with it. Anchored on the option families where the
# shorthand is even possible rather than applied file-wide — the comments above them are prose and
# may contain anything.
#
# Indentation is allowed for here, as in the severity check below, because the comparison with the
# expected ruleset trims it: an indented line is a setting to that comparison, so it must be one to
# the shape checks too, or it could be added to both files and pass every check.
if grep -E '^[[:space:]]*(dotnet_style_|csharp_style_|dotnet_naming_style\.)' "$PACKED_CONFIG" | grep -q ':'; then
  grep -nE '^[[:space:]]*(dotnet_style_|csharp_style_|dotnet_naming_style\.)' "$PACKED_CONFIG" | grep ':' >&2
  fail "a style option carries an inline :severity; severities belong in dotnet_diagnostic entries only"
fi
info "no style option carries an inline :severity"

# The exceptions to "one place to look", pinned rather than waved at. The only severity keys in
# this file that are not dotnet_diagnostic entries are ones with no dotnet_diagnostic form at all:
#   - the blanket, which is the whole point of dotnet_analyzer_diagnostic — a per-ID entry cannot
#     switch off a rule that does not exist yet, which is what makes the allowlist sealed;
#   - naming rules, because Roslyn reports every dotnet_naming_rule violation under IDE1006 and
#     dotnet_naming_rule.<name>.severity is the only way to say what one of them is set to.
# Which naming rules exist is the expected ruleset's business; this asserts only the kinds of key,
# so a severity of any other kind cannot appear and be read as house style.
STRAY_SEVERITIES="$(grep -oE '^[[:space:]]*[^#[:space:]=]+\.severity' "$PACKED_CONFIG" \
  | sed -E 's/^[[:space:]]+//' | grep -vE '^dotnet_diagnostic\.' \
  | grep -vxE 'dotnet_analyzer_diagnostic\.severity|dotnet_naming_rule\.[^.]+\.severity' || true)"
[ -z "$STRAY_SEVERITIES" ] \
  || fail "the only severities outside dotnet_diagnostic should be the blanket and the naming rules'; found: $STRAY_SEVERITIES"
info "the only non-dotnet_diagnostic severities are the blanket and the naming rules'"

# The tier sizes, reported rather than asserted: the expected ruleset already pins every line, so a
# count here would be a third copy of the same fact. What it is for is the reader of a CI log.
TIER_COUNTS="$({ grep -oE '^[[:space:]]*dotnet_diagnostic\.[A-Za-z]+[0-9]+\.severity' "$PACKED_CONFIG" || true; } \
  | sed -E 's/^[[:space:]]*dotnet_diagnostic\.([A-Za-z]+)[0-9]+\.severity$/\1/' | LC_ALL=C sort | uniq -c \
  | awk '{ printf "%s%s %s", sep, $2, $1; sep = " · "; total += $1 } END { printf " · %d rules", total }')"
info "the shipped config enumerates $TIER_COUNTS under the blanket"

# --- Assertion: neither AnalysisMode nor AnalysisLevel is set anywhere -----------------------
# Measured on this SDK, neither reaches past the blanket at any value, All and latest-all included
# (ADR-0002 decision 4, spec §10 correction 1). The CA tier is on because it is enumerated, and
# setting either property would document an intent the build does not honour. Checked over the
# packed artifact and the whole repository, since the criterion is "not set anywhere" — a stray
# entry in a .csproj is exactly as misleading as one in the shipped config.
# Comments are exempt, and deliberately so: the config explains at length why it does not set
# these, and a check that forbade the words would forbid the explanation. Only settings count.
if grep -vE '^[[:space:]]*#' "$PACKED_CONFIG" | grep -qiE 'AnalysisMode|AnalysisLevel'; then
  grep -nvE '^[[:space:]]*#' "$PACKED_CONFIG" | grep -iE 'AnalysisMode|AnalysisLevel' >&2
  fail "the packed config sets AnalysisMode/AnalysisLevel; neither reaches past the blanket"
fi
# The trailing [[:space:]>/] rather than a bare > is load-bearing: an MSBuild property carrying a
# Condition attribute is still a property that is set, and <AnalysisMode Condition="…">All</…> is
# the idiom this repository already uses for its own defaults. A pattern anchored on > alone walks
# straight past it. Verified against both spellings before it was written this way.
#
# Only the file kinds that can carry an MSBuild property are searched. AnalyzerConfig files cannot —
# these are properties, not config keys — and the packed config is checked above on its own terms.
# obj/ and bin/ are excluded because the SDK writes a generated .editorconfig there carrying
# build_property.EffectiveAnalysisLevelStyle, which is the SDK describing its own defaults rather
# than anything this repository sets.
PROPS_HITS="$(grep -rniE '<(AnalysisMode|AnalysisLevel)[A-Za-z]*[[:space:]>/]' \
  --include='*.csproj' --include='*.props' --include='*.targets' \
  --exclude-dir=obj --exclude-dir=bin "$REPO_ROOT" 2>/dev/null || true)"
if [ -n "$PROPS_HITS" ]; then
  echo "$PROPS_HITS" >&2
  fail "AnalysisMode/AnalysisLevel is set in the repository; neither reaches past the blanket"
fi
info "AnalysisMode and AnalysisLevel are set nowhere in the package or the repository"

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

# --- Assertion: the fixture raises exactly what its markers declare -------------------------
# Every fixture line that should raise a diagnostic carries an expectation marker naming the rule and
# the severity it ships at, and every other line — every file without one — must stay silent.
# diagnostic-diff.sh compares a build log against those markers in both directions and names each row
# that differs. So this script knows no fixture file, rule or count: what each fixture file proves,
# which rules are a sample of their tier, and why a silence there means something rather than an
# absent analyzer, is written beside the line that proves it.
#
# Exact means every diagnostic in the log, including those no marker could declare because they sit
# on no fixture line. Two of those were assertions of their own here before #52, and the reasons are
# worth keeping, because in each every *other* row still matches:
#   - MultipleGlobalAnalyzerKeys. Since #12 the repository root's Directory.Build.props injects this
#     same config file into this repository's own projects (spec §7), and
#     tests/Fixture.Consumer/Directory.Build.props exists to keep the fixture out of it. If that
#     isolation ever breaks, both copies reach the consumer, every shared key is unset, and the
#     blanket goes with them — the seal disappears and third-party rules run at vendor defaults,
#     while the APB rules keep firing at their descriptor default severity. Measured, not theorised.
#   - CS8032/CS8034, a packed analyzer assembly that failed to load. The code fix assembly
#     references Workspaces, which a command-line build does not host, so "does it load outside the
#     IDE" is a real question — and the compiler answers it with a warning, the #2 hazard shape all
#     over again.
check_diagnostics() {
  local log="$1" build="$2"; shift 2
  local differences rc=0
  differences="$("$REPO_ROOT/scripts/diagnostic-diff.sh" "$log" "$FIXTURE_DIR" "$@" 2>&1)" || rc=$?
  # Exit 2 is the comparison refusing to run — a malformed marker, or a log line it cannot read — and
  # says nothing about the build, so it is not dressed up as a diagnostic mismatch.
  if [ "$rc" -eq 2 ]; then
    echo "$differences" >&2
    fail "the fixture build $build could not be compared with its expectation markers"
  elif [ "$rc" -ne 0 ]; then
    cat "$log" >&2
    echo "$differences" >&2
    fail "the fixture build $build did not raise exactly the diagnostics its expectation markers declare (+ raised but not declared, - declared but not raised)"
  fi
}

# Build #1, with the TreatWarningsAsErrors default the package ships. --getProperty above proves the
# property arrives; this proves it does something: every declared diagnostic must arrive promoted to
# an error, and the build must fail on them.
set +e
dotnet build "$FIXTURE" --no-restore \
  -p:AprbrownAnalyzersVersion="$VERSION" --nologo -v quiet > "$BUILD_LOG" 2>&1
BUILD_RC=$?
set -e

check_diagnostics "$BUILD_LOG" "with TreatWarningsAsErrors" --warnings-as-errors
info "the fixture raised exactly its declared diagnostics, every one promoted to an error"

if [ "$BUILD_RC" -eq 0 ]; then
  cat "$BUILD_LOG" >&2
  fail "fixture build succeeded but should have failed on the declared diagnostics as errors"
fi
info "TreatWarningsAsErrors failed the build as expected"

# Build #2, with the promotion off. Build #1 cannot tell severities apart, because TreatWarningsAsErrors
# flattens every diagnostic to "error": a rule the config had wrongly downgraded from error to warning
# would print identically to one it recorded faithfully. Here the compiler prints each rule at its
# configured severity, which is what the markers declare — so a vendor's Error flattened to warning
# is a difference. The build still fails, since some rules are errors in their own right, but nothing
# here depends on its exit code, only on what it printed.
SEVERITY_LOG="$WORK/severity.log"
set +e
dotnet build "$FIXTURE" --no-restore \
  -p:AprbrownAnalyzersVersion="$VERSION" -p:TreatWarningsAsErrors=false \
  -t:Rebuild --nologo -v quiet > "$SEVERITY_LOG" 2>&1
set -e

check_diagnostics "$SEVERITY_LOG" "without TreatWarningsAsErrors"
info "the fixture raised exactly its declared diagnostics, each at the severity it ships at"

echo "SMOKE PASS"
