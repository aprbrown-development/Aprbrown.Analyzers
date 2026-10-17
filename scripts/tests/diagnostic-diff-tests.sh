#!/usr/bin/env bash
#
# Tests for the comparison between a fixture build's log and the fixture's expectation markers (#52).
#
# diagnostic-diff.sh is what verify-package.sh asks whether the consumer fixture raised exactly the
# diagnostics its source declares, so a comparison that passes something it should refuse would let
# a rule go quiet, or a new one start firing, with the smoke test still green. Every case here is a
# canned log beside a tiny canned fixture, so this runs in milliseconds and needs neither the SDK nor
# a package. The log lines are copied in shape from a real `dotnet build -v quiet`.

# No `set -e`, for the reason release-gate-tests.sh gives: several cases here run the script
# expecting it to exit non-zero, and the exit code is read explicitly instead.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIFF="$REPO_ROOT/scripts/diagnostic-diff.sh"

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

FIXTURE="$WORK/Fixture.Consumer"
LOG="$WORK/build.log"

# Start each case from an empty fixture and an empty log.
reset() {
  rm -rf "$FIXTURE"
  mkdir -p "$FIXTURE"
  : > "$LOG"
}

# One diagnostic line, as MSBuild prints it: absolute path, (line,col), severity, ID, message, the
# help link and the project in brackets.
diagnostic() {
  local file="$1" line="$2" col="$3" severity="$4" id="$5"
  echo "$FIXTURE/$file($line,$col): $severity $id: a message (https://example.test/$id) [$FIXTURE/Fixture.Consumer.csproj]"
}

# Run the comparison over $LOG and $FIXTURE, with any extra arguments, and require it to agree.
assert_same() {
  local name="$1"; shift
  local out rc
  out="$("$DIFF" "$LOG" "$FIXTURE" "$@" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "$name"
  else
    not_ok "$name" "expected exit 0, got $rc; output: $out"
  fi
}

# Run the comparison and require a refusal (exit 1) whose output names every row given, each exactly
# as it should be printed. Naming the row is the point: a refusal that only says "differs" sends the
# reader back to a build log to find out which line and which rule.
assert_differs() {
  local name="$1"; shift
  local args=()
  while [ "$1" != "--" ]; do args+=("$1"); shift; done
  shift
  local out rc want
  out="$("$DIFF" "$LOG" "$FIXTURE" "${args[@]+"${args[@]}"}" 2>&1)"; rc=$?
  if [ "$rc" -ne 1 ]; then
    not_ok "$name" "expected exit 1, got $rc; output: $out"
    return
  fi
  for want in "$@"; do
    if ! grep -qxF -- "$want" <<< "$out"; then
      not_ok "$name" "exited 1, but did not print [$want]; output: $out"
      return
    fi
  done
  ok "$name"
}

# The one-file fixture most cases start from: Widget.cs, whose marker on line 5 declares APB0001 on
# line 6.
widget() {
  printf '%s\n' \
    'namespace Fixture.Consumer;' \
    '' \
    '// Triggers APB0001.' \
    '' \
    '// expect: APB0001 warning' \
    'public class Widget(int size)' \
    '{' \
    '}' > "$FIXTURE/Widget.cs"
}

# --- a fixture that raises exactly what it declares ----------------------------------------------
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
assert_same 'a log holding exactly the declared diagnostics passes'

# --- a declared diagnostic the build did not raise -----------------------------------------------
# The rule went quiet: a config line lost, an analyzer that failed to bind. The failure the smoke test
# has always looked for, now named by the line that should have raised it.
reset; widget
assert_differs 'a declared diagnostic missing from the log is named with -' -- \
  '- Widget.cs:6 APB0001 warning'

# --- a diagnostic no marker declares -------------------------------------------------------------
# The direction the old substring greps could never see: a rule switched on by accident, or a
# detection fix firing somewhere new. Here it is on a line with a marker for a different rule, which
# must not excuse it.
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
diagnostic Widget.cs 6 14 warning IDE0040 >> "$LOG"
assert_differs 'an undeclared diagnostic is named with +' -- \
  '+ Widget.cs:6 IDE0040 warning'

# --- the right rule at the wrong severity --------------------------------------------------------
# A vendor's Error flattened to warning, or the reverse, shows as the pair: what the marker declares
# and what the build raised.
reset; widget
diagnostic Widget.cs 6 20 error APB0001 >> "$LOG"
assert_differs 'a severity mismatch is named as the declared row and the raised one' -- \
  '- Widget.cs:6 APB0001 warning' \
  '+ Widget.cs:6 APB0001 error'

# --- MSBuild's summary repeats every diagnostic --------------------------------------------------
# A failed build prints each diagnostic once as it is raised and again after "Build FAILED.". The
# copy is the same diagnostic, not a second one, or every real log would read as all rows doubled.
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
printf '%s\n' '' 'Build FAILED.' '' >> "$LOG"
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
printf '%s\n' '    1 Warning(s)' '    0 Error(s)' >> "$LOG"
assert_same "the summary's copy of a diagnostic is not a second diagnostic"

# --- one rule twice on one line ------------------------------------------------------------------
# Two diagnostics at different columns are two diagnostics, so the comparison is over a multiset:
# the line declares the rule twice, and declaring it once is a missing row.
reset
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  '// expect: APB0099 warning, APB0099 warning' \
  'public record Pair(int A, int B);' > "$FIXTURE/Pair.cs"
diagnostic Pair.cs 3 21 warning APB0099 >> "$LOG"
diagnostic Pair.cs 3 28 warning APB0099 >> "$LOG"
diagnostic Pair.cs 3 21 warning APB0099 >> "$LOG"
diagnostic Pair.cs 3 28 warning APB0099 >> "$LOG"
assert_same 'a rule raised twice on one line matches a marker declaring it twice'
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  '// expect: APB0099 warning' \
  'public record Pair(int A, int B);' > "$FIXTURE/Pair.cs"
assert_differs 'a rule raised twice on one line and declared once is named with +' -- \
  '+ Pair.cs:3 APB0099 warning'

# --- the marker declares the line below it -------------------------------------------------------
# A marker sits on its own line above the code it describes, so what it declares is raised on the
# line below. The same diagnostic raised on the marker's own line is a different row.
reset; widget
diagnostic Widget.cs 5 1 warning APB0001 >> "$LOG"
assert_differs "a diagnostic on the marker's own line is not the one it declares" -- \
  '+ Widget.cs:5 APB0001 warning' \
  '- Widget.cs:6 APB0001 warning'

# --- a trailing comment is not a marker ----------------------------------------------------------
# A marker shares its line with nothing. One trailing code would be read as declaring the line below
# it, which nobody writing it meant, so it declares nothing and the diagnostic it was meant for is
# undeclared.
reset
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  'public class Widget(int size) // expect: APB0001 warning' \
  '{' \
  '}' > "$FIXTURE/Widget.cs"
diagnostic Widget.cs 2 20 warning APB0001 >> "$LOG"
assert_differs 'a trailing expect: comment declares nothing' -- \
  '+ Widget.cs:2 APB0001 warning'

# --- an indented marker --------------------------------------------------------------------------
# Inside a type, a marker is indented like the member below it.
reset
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  'public static class Sprocket' \
  '{' \
  '    // expect: APB0002 warning' \
  '    public static Task SpinAsync(CancellationToken ct = default) => Task.CompletedTask;' \
  '}' > "$FIXTURE/Sprocket.cs"
diagnostic Sprocket.cs 5 34 warning APB0002 >> "$LOG"
assert_same 'an indented marker declares the line below it'

# --- a build with TreatWarningsAsErrors ----------------------------------------------------------
# Markers carry the shipped severity, which is what a build without the promotion prints. With it,
# every declared warning is raised as an error — so the same markers have to describe that build
# too, and a warning that escaped the promotion is a difference.
reset; widget
diagnostic Widget.cs 6 20 error APB0001 >> "$LOG"
assert_same 'with --warnings-as-errors a declared warning is expected as an error' --warnings-as-errors
: > "$LOG"
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
assert_differs 'with --warnings-as-errors a warning that escaped the promotion is a difference' \
  --warnings-as-errors -- \
  '- Widget.cs:6 APB0001 error' \
  '+ Widget.cs:6 APB0001 warning'

# --- diagnostics no marker could ever declare ----------------------------------------------------
# An analyzer assembly that failed to load, a second global config colliding with the shipped one, a
# restore warning: none of them sits on a fixture line, so none can carry a marker, and every one is
# unexpected. MultipleGlobalAnalyzerKeys is the case that matters most and the easiest to miss — its
# ID is not letters-then-digits, and the collision it reports takes the blanket with it.
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
printf '%s\n' \
  "CSC : warning CS8032: An instance of analyzer X cannot be created [$FIXTURE/Fixture.Consumer.csproj]" \
  "CSC : warning MultipleGlobalAnalyzerKeys: Multiple global analyzer config files set the same key [$FIXTURE/Fixture.Consumer.csproj]" \
  "$FIXTURE/Fixture.Consumer.csproj : warning NU1603: Meziantou.Analyzer 3.0.305 was not found [$FIXTURE/Fixture.Consumer.csproj]" \
  >> "$LOG"
assert_differs 'a diagnostic with no source location is always unexpected' -- \
  '+ CSC CS8032 warning' \
  '+ CSC MultipleGlobalAnalyzerKeys warning' \
  "+ $FIXTURE/Fixture.Consumer.csproj NU1603 warning"

# --- a diagnostic in a file outside the fixture --------------------------------------------------
# A generated file under obj/, say. No marker can be written there, so it is unexpected, and it is
# named by its full path because a path relative to the fixture would be a lie.
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
echo "$WORK/elsewhere/Gen.cs(3,1): warning CS1591: a message [$FIXTURE/Fixture.Consumer.csproj]" >> "$LOG"
assert_differs 'a diagnostic outside the fixture is named by its full path' -- \
  "+ $WORK/elsewhere/Gen.cs:3 CS1591 warning"

# --- a log written at normal verbosity -----------------------------------------------------------
# verify-package.sh builds with -v quiet, but at -v normal MSBuild prefixes each line with its node
# number. Someone rerunning a build to see more must not get a comparison that sees nothing.
reset; widget
echo "     1>$(diagnostic Widget.cs 6 20 warning APB0001)" >> "$LOG"
assert_same 'the node prefix of a normal-verbosity log is not part of the path'
: > "$LOG"
echo "     1>$(diagnostic Widget.cs 6 20 warning APB0001)" >> "$LOG"
echo "     1>$(diagnostic Widget.cs 6 14 warning IDE0040)" >> "$LOG"
assert_differs 'a normal-verbosity log is compared, not ignored' -- \
  '+ Widget.cs:6 IDE0040 warning'

# --- a path with a parenthesis in it -------------------------------------------------------------
# A checkout under "Projects (old)" is an ordinary directory name, and the (line,col) that follows
# the path is the only parenthesis that ends it.
FIXTURE="$WORK/Copy (2)/Fixture.Consumer"
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
assert_same 'a fixture under a path containing a parenthesis is still read'
FIXTURE="$WORK/Fixture.Consumer"

# --- a fixture reached through a symlink ---------------------------------------------------------
# The compiler writes the resolved path into the log, so a fixture named through a symlink — a
# symlinked home, macOS's /var — must still be recognised as the fixture.
reset; widget
ln -s "$FIXTURE" "$WORK/linked"
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
out="$("$DIFF" "$LOG" "$WORK/linked" 2>&1)"; rc=$?
if [ "$rc" -eq 0 ]; then ok 'a fixture named through a symlink matches the resolved paths in the log'
else not_ok 'a fixture named through a symlink matches the resolved paths in the log' "exit $rc; output: $out"; fi
rm "$WORK/linked"

# --- a diagnostic line the comparison cannot read -------------------------------------------------
# Every line that reports an error or a warning must become a row. One that matches neither shape
# would otherwise be dropped, and a build that raised it would pass — the comparison failing open.
# So it is refused (exit 2) and quoted, never skipped.
reset; widget
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
echo "something unforeseen: error XY0001: a shape no pattern reads" >> "$LOG"
out="$("$DIFF" "$LOG" "$FIXTURE" 2>&1)"; rc=$?
if [ "$rc" -eq 2 ] && grep -qF 'XY0001' <<< "$out"; then ok 'a diagnostic line that cannot be read is refused, not dropped'
else not_ok 'a diagnostic line that cannot be read is refused, not dropped' "exit $rc; output: $out"; fi

# --- the marker lives in a comment ---------------------------------------------------------------
# "expect:" in a string literal is code, not a declaration, and must not be read as one.
reset; widget
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  'public static class Words { public const string Prompt = "expect: anything"; }' > "$FIXTURE/Words.cs"
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
assert_same 'expect: outside a // comment is not a marker'

# --- a file with no markers ----------------------------------------------------------------------
# A file without a marker declares silence, and silence is asserted as exactly as firing — what
# Gadget.cs and Point.cs always said of themselves and nothing used to check.
reset; widget
printf '%s\n' \
  'namespace Fixture.Consumer;' \
  '' \
  '// Must stay silent: an explicit constructor is the endorsed shape.' \
  'public class Gadget' \
  '{' \
  '}' > "$FIXTURE/Gadget.cs"
diagnostic Widget.cs 6 20 warning APB0001 >> "$LOG"
assert_same 'a file with no markers that raises nothing passes'
diagnostic Gadget.cs 4 14 warning APB0001 >> "$LOG"
assert_differs 'a file with no markers that raises a diagnostic is named with +' -- \
  '+ Gadget.cs:4 APB0001 warning'

# --- markers that could never match --------------------------------------------------------------
# A marker the build can never satisfy is a silent bug in the thing meant to stop silent bugs, so it
# is refused outright (exit 2, as for any misuse) and named by file and line, never compared.
# Suggestion is refused with the rest: a suggestion never reaches a build log at any verbosity.
assert_malformed() {
  local name="$1" marker="$2" out rc
  reset; widget
  printf '%s\n' 'namespace Fixture.Consumer;' "// expect: $marker" 'public class Bad { }' > "$FIXTURE/Bad.cs"
  out="$("$DIFF" "$LOG" "$FIXTURE" 2>&1)"; rc=$?
  if [ "$rc" -ne 2 ]; then
    not_ok "$name" "expected exit 2, got $rc; output: $out"
  elif ! grep -qF 'Bad.cs:2' <<< "$out"; then
    not_ok "$name" "exited 2, but did not name Bad.cs:2; output: $out"
  else
    ok "$name"
  fi
}
assert_malformed 'a marker with no severity is refused'          'APB0001'
assert_malformed 'a marker at suggestion is refused'             'IDE0022 suggestion'
assert_malformed 'a marker with a misspelt severity is refused'  'APB0001 warnign'
assert_malformed 'a marker whose ID is not an ID is refused'     'primary-ctor warning'
assert_malformed 'an empty marker is refused'                    ''
assert_malformed 'a marker with a stray word is refused'         'APB0001 warning please'

echo
echo "diagnostic-diff tests: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
