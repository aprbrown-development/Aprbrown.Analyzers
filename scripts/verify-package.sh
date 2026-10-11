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
# The shipped config is complete as of spec §2.3 step 4 — APB0001-APB0003, the Meziantou tier, the
# SDK CA tier, StyleCop's four ordering rules and the IDE, naming and style tier, all under the
# blanket — so the assertions are:
#   - the given .nupkg matches the §4.2 allowlist exactly;
#   - the fragment every APB rule's help link deep-links to is still a heading in the packed
#     README — a renamed heading breaks all three links with no 404 to notice;
#   - both MSBuild property defaults (EnforceCodeStyleInBuild, TreatWarningsAsErrors) arrive —
#     a silent failure otherwise (#2 hazard H1);
#   - the packed config is exactly the expected ruleset (tests/expected-ruleset.txt), checked
#     against the packed file rather than the source tree so what is asserted is what ships, with
#     any difference named line by line — and it keeps the sealed allowlist's shape: the blanket
#     first, no category re-enable, no inline :severity, and no severity outside dotnet_diagnostic
#     but the blanket's and the naming rules';
#   - every rule in EXPECTED_RULES fires against the fixture, and TreatWarningsAsErrors promotes
#     them to build errors;
#   - severities survive the trip: a rule Meziantou defaults to Error arrives as an error and a
#     Warning one as a warning, measured in a second build with TreatWarningsAsErrors=false where
#     the two are still distinguishable;
#   - MA0004 stays silent against a construct that violates it, with the assembly loaded;
#   - neither AnalysisMode nor AnalysisLevel is set anywhere in this repository or the package —
#     they cannot reach past the blanket, so setting one would document an intent the build does
#     not honour;
#   - IDE1006 and IDE0040 fire in the fixture, twice each. These are the rows that prove the most:
#     under the blanket an IDE naming rule whose severity is not set explicitly is *silently*
#     unenforced (#4), so their firing is the only available evidence that enumeration-by-ID
#     reached them, and the second of each pair pins a specific choice — naming rules resolving by
#     specificity rather than declaration order, and `always` over `for_non_interface_members`;
#   - the seven expression-bodied preferences never fail the build, measured in the build that
#     promotes warnings to errors, with IDE0040 and IDE1006 erroring in that same file as control;
#   - CA2007 and CA1707 stay silent against constructs that violate them, with the SDK analyzers
#     loaded and firing in the same file — the seal holding against a default-off rule and against
#     the category re-enable that would admit it;
#   - every packed analyzer assembly loads in the consumer. The code fix assembly references
#     Workspaces, which a command-line build does not host, so "does it load outside the IDE" is a
#     real question and CS8032/CS8034 is how the compiler answers it — as a warning, which is the
#     #2 hazard shape all over again.
# Later tickets add rows to EXPECTED_RULES as the config enumerates more rules back on.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURE="$REPO_ROOT/tests/Fixture.Consumer/Fixture.Consumer.csproj"

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

# --- Assertion: the descriptors' help link points at a heading that ships --------------------
# Every APB rule's helpLinkUri deep-links to a heading in docs/consuming.md, which this package
# ships as README.md (#42). The C# constant and the prose are joined by nothing but the coincidence
# of a slug, in two files no reviewer would open together — and a reworded heading breaks the IDE
# help link for all three rules silently. There is no 404 to notice: GitHub serves the page and
# ignores an unknown fragment, so a consumer clicking APB0001 on a squiggle simply lands at the top
# of a long document with no sign they were meant to be 200 lines down (#43).
#
# The fragments are derived from Descriptors.cs rather than written out here, which would only move
# the coupling into this script. Every help link in the file is checked, not just today's shared
# constant, so this also starts failing the day someone gives a rule its own link without adding a
# heading for it — which is what would make splitting the one anchor into three safe.
#
# The edge, stated so the next reader knows where the assertion stops, the way the MA0004 and
# CA2007 silences state theirs. Three things are outside it:
#   - this checks the *fragment against the shipped document*, never that the URL resolves. The
#     host, the blob/main path and the repository name are unverified — the smoke test does no
#     network I/O and should not start — so a typo in the domain would pass;
#   - a link is found by the two shapes a descriptor can carry one in, `helpLinkUri:` and a
#     `const string HelpLink…`. A URL reached by neither is not seen;
#   - the slugifier is GitHub's for ASCII headings. It does not model the `-1`/`-2` suffixes
#     GitHub appends to a duplicate heading, and it drops accented letters GitHub keeps. Both
#     would fail a link that in fact works, which is the harmless direction.
#
# No fixture test in scripts/tests/ accompanies it, unlike the release gates' Markdown parsers.
# Those fail open: a parser that quietly matches nothing looks exactly like a gate that passed.
# This one is a required positive match, so a slugifier that produced nothing would fail the build
# rather than wave it through.
DESCRIPTORS="$REPO_ROOT/src/Aprbrown.Analyzers/Descriptors.cs"
[ -f "$DESCRIPTORS" ] || fail "$DESCRIPTORS not found; it is where every APB rule's help link comes from"
# One awk pass rather than grep piped into head: under `set -o pipefail` a reader that exits early
# can take the pipeline's status with it.
#
# Arm on either shape a help link can arrive in, then take the next URL and disarm. Arming and
# matching are separate rules rather than one pattern because the string may sit on the
# declaration's line or, as the shared constant does today, wrap onto the next. The reference
# `helpLinkUri: HelpLinkUri` arms without matching and stays armed until the constant supplies the
# URL further down the file, which is why the three descriptors sharing one constant yield one link
# rather than three.
HELP_LINKS="$(awk '
  /helpLinkUri:/ || /const string HelpLink[A-Za-z0-9]*[[:space:]]*=/ { armed = 1 }
  armed && match($0, /https?:\/\/[^"]+/) { print substr($0, RSTART, RLENGTH); armed = 0 }
' "$DESCRIPTORS" | LC_ALL=C sort -u)" \
  || fail "could not read the help links out of src/Aprbrown.Analyzers/Descriptors.cs"
[ -n "$HELP_LINKS" ] \
  || fail "no help link found in src/Aprbrown.Analyzers/Descriptors.cs; every APB rule's helpLinkUri comes from it"

PACKED_README="$WORK/packed-README.md"
read_entry "$NUPKG" 'README.md' "$PACKED_README" \
  || fail "could not read README.md out of the package"
[ -s "$PACKED_README" ] || fail "the packed README.md is empty"

# GitHub's slug for an ATX heading: lowercase, drop anything that is not alphanumeric, space,
# hyphen or underscore — backticks and periods go, underscores survive as they do on GitHub — then
# spaces to hyphens.
#
# Fenced blocks are skipped, and that narrows what this accepts rather than widening it:
# consuming.md is largely .editorconfig samples, whose comment lines all begin with `#`. Admitted
# as headings, they would be extra slugs for a broken fragment to match by accident.
HEADING_SLUGS="$(awk '
  /^ {0,3}(```|~~~)/ { in_fence = !in_fence; next }
  !in_fence && /^ {0,3}#{1,6}[[:space:]]/ {
    sub(/^ *#+[[:space:]]+/, "")     # the leading hashes and the space after them
    sub(/[[:space:]]+#+[[:space:]]*$/, "")  # a closed ATX heading trailing its hashes back
    sub(/[[:space:]]+$/, "")         # trailing space, which GitHub trims and a slug would keep
    print
  }
' "$PACKED_README" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9 _-]//g; s/ /-/g')" \
  || fail "could not slugify the headings of the packed README.md"

# One assertion per link, so a future per-rule link names itself rather than hiding behind whichever
# of them happens to be checked first.
while IFS= read -r HELP_LINK; do
  # A missing fragment is a silent regression to a document- or repository-level link — the state
  # #42 moved away from, and one that no other assertion here would notice.
  case "$HELP_LINK" in
    *#?*) HELP_FRAGMENT="${HELP_LINK##*#}" ;;
    *) fail "the help link '$HELP_LINK' carries no #fragment; it has regressed to a link that lands a consumer at the top of the document rather than at the rule. Restore the fragment in src/Aprbrown.Analyzers/Descriptors.cs" ;;
  esac

  # A here-string rather than a pipe into grep -q, for the pipefail reason given above.
  if ! grep -qxF "$HELP_FRAGMENT" <<< "$HEADING_SLUGS"; then
    echo "--- headings in the packed README.md ---" >&2; echo "$HEADING_SLUGS" >&2
    fail "the help link fragment '#$HELP_FRAGMENT' matches no heading in the packed README.md, so an APB squiggle's help link now lands at the top of the document. Either restore the heading in docs/consuming.md or point that link in src/Aprbrown.Analyzers/Descriptors.cs at one that is there"
  fi
  info "the help link fragment '#$HELP_FRAGMENT' is a heading in the packed README.md"
done <<< "$HELP_LINKS"

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
if grep -E '^(dotnet_style_|csharp_style_|dotnet_naming_style\.)' "$PACKED_CONFIG" | grep -q ':'; then
  grep -nE '^(dotnet_style_|csharp_style_|dotnet_naming_style\.)' "$PACKED_CONFIG" | grep ':' >&2
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
STRAY_SEVERITIES="$(grep -oE '^[^#[:space:]=]+\.severity' "$PACKED_CONFIG" \
  | grep -vE '^dotnet_diagnostic\.' \
  | grep -vxE 'dotnet_analyzer_diagnostic\.severity|dotnet_naming_rule\.[^.]+\.severity' || true)"
[ -z "$STRAY_SEVERITIES" ] \
  || fail "the only severities outside dotnet_diagnostic should be the blanket and the naming rules'; found: $STRAY_SEVERITIES"
info "the only non-dotnet_diagnostic severities are the blanket and the naming rules'"

# The tier sizes, reported rather than asserted: the expected ruleset already pins every line, so a
# count here would be a third copy of the same fact. What it is for is the reader of a CI log.
TIER_COUNTS="$(grep -oE '^dotnet_diagnostic\.[A-Za-z]+[0-9]+\.severity' "$PACKED_CONFIG" \
  | sed -E 's/^dotnet_diagnostic\.([A-Za-z]+)[0-9]+\.severity$/\1/' | LC_ALL=C sort | uniq -c \
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

# --- Assertion: every enumerated rule fires and fails the build ------------------------------
# One row per rule the shipped config switches on, each with a matching case in the fixture.
# The three Meziantou rows are a deliberate sample rather than all 103 — one ordinary member of the
# default-on Warning sweep (MA0026), the one rule added against a vendor default of off (MA0032),
# and one of the three the vendor defaults to Error (MA0037). They fire only because a config
# shipped by this package bound to an assembly this package does not depend on, which is the claim
# under test. What the other 100 get is weaker and worth naming: the expected ruleset above pins
# that they are present at the right severities, but nothing here observes them fire. Proving 103 rules
# behaviourally would mean 103 violations in the fixture, and the binding they would each
# re-demonstrate is the same one these three already establish.
#
# The CA rows are the same kind of sample over the 29-rule CA tier: CA2200 for the Warning sweep
# and CA2252 for the two the SDK defaults to Error. They need no package reference in the fixture —
# the SDK carries them — so what they prove is narrower than the Meziantou rows and worth naming:
# that enumeration by ID reaches past the blanket at all, which nothing else here shows for the CA
# tier, since AnalysisMode cannot.
#
# The four SA rows are the whole StyleCop tier rather than a sample, because four is small enough
# to prove exhaustively — and they carry the Meziantou rows' stronger claim too, binding to an
# assembly the package does not depend on.
#
# The two IDE rows are the ones the tier exists for, and they prove the most of any row here.
# IDE1006 and IDE0040 have no vendor default to fall back on: under the blanket a naming rule whose
# dotnet_diagnostic severity is not set explicitly is *silently unenforced*, firing nothing and
# warning about nothing (#4). So unlike CA2200, which would at least be reachable some other way,
# these two firing is the only available evidence that enumeration-by-ID reached them at all — and
# their silence would have looked exactly like a clean file. Lathe.cs is where they are measured.
EXPECTED_RULES=(
  APB0001 APB0002 APB0003
  MA0026 MA0032 MA0037
  CA2200 CA2252
  SA1201 SA1202 SA1203 SA1204
  IDE1006 IDE0040
)

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

# --- Assertion: the expression-bodied preferences cannot fail a build -----------------------
# This is the build with TreatWarningsAsErrors=true, which is the only build where the claim means
# anything: it promotes every warning to an error, so a preference recorded at warning rather than
# suggestion would show up here and nowhere else. Lathe.Describe wraps a single expression in a
# block body, which is exactly what IDE0022 reports, and IDE0040 and IDE1006 erroring in that same
# file above is the control — the IDE analyzers are demonstrably running over it.
for RULE in IDE0021 IDE0022 IDE0025 IDE0026 IDE0027 IDE0053 IDE0061; do
  if grep -q "error $RULE" "$BUILD_LOG"; then
    grep "error $RULE" "$BUILD_LOG" >&2
    fail "$RULE failed the build; the seven expression-bodied preferences ship at suggestion and must never gate"
  fi
done
info "no expression-bodied preference failed the build, with the IDE analyzers running"

# --- Assertion: both naming rules fire, and the specific one is not swallowed ----------------
# Two IDE1006 diagnostics in Lathe.cs, not one. `count` violates the general private-fields rule;
# `_created` satisfies that same rule and violates only the specific private-statics one — so the
# second diagnostic exists only if Roslyn resolved the more specific rule. It does: naming rules
# are ordered by specificity, not by declaration order (#2, verified twice), and `_created` is
# declared *first*, which under the declaration-order story the source repository's comment told
# would have had the general rule swallow it. That comment is not carried into the shipped config
# and this count is why it did not have to be taken on trust.
#
# Counted per source location and deduplicated, because MSBuild prints each diagnostic twice — once
# as the compiler emits it and again in the error summary at the end. Counting raw lines would read
# 2 as 4 and make the assertion a test of MSBuild's log format rather than of the ruleset.
#
# Both counts in this section want the same thing — "this rule fired exactly N times in this file"
# — so it is a function. The reason is the argument because it is
# what a failure has to explain: the count alone says nothing about which of the two was lost.
assert_diagnostic_count() {
  local file="$1" rule="$2" expected="$3" reason="$4"
  local actual
  actual="$(grep -o "$file([0-9]*,[0-9]*): [a-z]* $rule" "$BUILD_LOG" | LC_ALL=C sort -u | wc -l)"
  # Numeric compare, not string: wc -l pads its output with spaces on some platforms and this
  # script is meant to run on a dev machine as well as in CI.
  if [ "$actual" -ne "$expected" ]; then
    grep "$rule" "$BUILD_LOG" >&2
    # The file arrives as a grep pattern; unescape it so the message reads as a filename.
    fail "expected $expected $rule diagnostics in ${file//\\/} ($reason), found $actual"
  fi
}

assert_diagnostic_count 'Lathe\.cs' IDE1006 2 "the general and the specific naming rule"
info "both naming rules fired; the specific rule was not swallowed by the general one"

# Two IDE0040 diagnostics too, and the second is the one `always` buys: an interface member, which
# `for_non_interface_members` would have let through. The spec picks `always` deliberately, so the
# difference between the two values has to be observable rather than merely written down.
assert_diagnostic_count 'Lathe\.cs' IDE0040 2 "a member and an interface member"
info "IDE0040 fired on an interface member too; 'always' rather than 'for_non_interface_members'"

# --- Assertion: IDE0003 is silent at build time, and that is a limitation -------------------
# Lathe.Measure writes `this._size` with dotnet_style_qualification_for_field = false, which is
# precisely what IDE0003 reports, and IDE1006 and IDE0040 erroring in that same file above is the
# control — the IDE analyzers are running over it. It still does not fire: IDE0003 is one of the
# Simplify-family rules Roslyn runs only in the IDE. Measured on SDK 10.0.110, where IDE0009 — the
# add-qualification half, driven by the same options — does fire in a command-line build.
#
# So this is the one house convention the build does not gate, and it is pinned here rather than
# left as a gap someone rediscovers. The assertion is deliberately the wrong way round from the
# CA2007 and MA0004 silences above: those are permanent by design, this one should one day break.
if grep -q 'IDE0003' "$BUILD_LOG"; then
  grep 'IDE0003' "$BUILD_LOG" >&2
  fail "IDE0003 fired at build time — good news, and this assertion is now stale. Move IDE0003 into EXPECTED_RULES and update spec §10 correction 12, which records it as IDE-only"
fi
info "IDE0003 stayed silent; it is IDE-only, with IDE1006 and IDE0040 as the control"

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

# The same measurement for the CA tier, and it carries more weight here: CA1856 and CA2252 landing
# at error is the whole reason this tier ships 29 rules rather than the 27 spec §2.3 step 2 states.
# If either had been flattened to warning, dropping it to reach 27 would have cost nothing — so the
# distinction has to be observable, not just written down.
grep -q 'error CA2252' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "CA2252 did not arrive as an error; the SDK's Error severity was flattened"; }
grep -q 'warning CA2200' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "CA2200 did not arrive as a warning"; }
info "SDK severities survived: CA2252 as error, CA2200 as warning"

# CA2007 and CA1707 are the seal's behavioural test, and the control is CA2200 firing in Furnace.cs
# — the same file both violating constructs live in, so the SDK analyzers are demonstrably loaded
# and analysing it. Without that control a silence proves only that nothing ran.
#
# CA2007 is the load-bearing one: enabled-by-default *false* at Warning, so it is a genuine
# default-off rule that a category re-enable would admit and the blanket must keep out.
# CA1707 is the rule ADR-0002 decision 4 demonstrated the hazard with, kept as a regression test
# for the category re-enable — with the caveat that it is default-on at Hidden rather than
# default-off (spec §10), so its silence alone is weaker than that passage assumed.
grep -q 'CA2200' "$SEVERITY_LOG" \
  || { cat "$SEVERITY_LOG" >&2; fail "CA2200 did not fire, so the CA2007/CA1707 assertions below would prove nothing"; }
for RULE in CA2007 CA1707; do
  if grep -q "$RULE" "$SEVERITY_LOG"; then
    grep "$RULE" "$SEVERITY_LOG" >&2
    fail "$RULE fired; it is deliberately not enumerated and must stay off under the blanket"
  fi
done
info "CA2007 and CA1707 stayed silent against violating code, with the SDK analyzers loaded"

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
