#!/usr/bin/env bash
#
# Compare the diagnostics a consumer fixture build raised against the fixture's expectation markers
# (#52).
#
#   diagnostic-diff.sh <build-log> <fixture-dir> [--warnings-as-errors]
#
# Exits 0 when the log holds exactly the diagnostics the fixture's markers declare. Otherwise prints
# every row that differs and exits 1: `+ row` for a diagnostic the build raised and no marker
# declares, `- row` for one a marker declares and the build did not raise. A row is
# `<file>:<line> <ID> <severity>`, with the file relative to the fixture directory.
#
# An expectation marker is a comment on the line the diagnostic is reported on:
#
#   public class Widget(int size) // expect: APB0001 warning
#
# A marker carries the shipped severity: what a build without TreatWarningsAsErrors prints. Pass
# --warnings-as-errors for a build with it, and every declared severity is expected as `error`.

set -euo pipefail

usage() { echo "usage: diagnostic-diff.sh <build-log> <fixture-dir> [--warnings-as-errors]" >&2; exit 2; }

PROMOTED=false
case $# in
  2) ;;
  3) [ "$3" = "--warnings-as-errors" ] || usage; PROMOTED=true ;;
  *) usage ;;
esac

LOG="$1"
FIXTURE="$(cd "$2" && pwd)"

# Every diagnostic the build printed, one row each. A failed build prints each one twice — as it is
# raised and again in the summary — so the copies collapse on the full location, column included,
# before the column is dropped: two diagnostics of one rule on one line are still two.
#
# A diagnostic with no source location (`CSC : warning CS8032: …`, a project-level NU warning) is a
# row too, named by whatever stands before the ` : `. No marker can declare one, so it is always a
# difference — which is how a failed analyzer load or a second global config fails the comparison.
# The ID pattern is wider than an analyzer's letters-then-digits for that reason:
# MultipleGlobalAnalyzerKeys is one of these, and the one that matters most.
#
# The optional `N>` is the node number MSBuild prefixes at -v normal and above.
actual() {
  local id='([A-Za-z][A-Za-z0-9_]*)'
  sed -nE \
    -e "s/^[[:space:]]*([0-9]+>)?([^(]+)\(([0-9]+),([0-9]+)\): (error|warning) $id: .*\$/\2|\3|\4|\5|\6/p" \
    -e "s/^[[:space:]]*([0-9]+>)?(.*[^[:space:]]) : (error|warning) $id: .*\$/\2|||\3|\4/p" \
    "$LOG" \
    | LC_ALL=C sort -u \
    | awk -F'|' -v root="$FIXTURE/" '{
        if ($2 == "") { print $1 " " $5 " " $4; next }
        file = $1
        if (index(file, root) == 1) file = substr(file, length(root) + 1)
        print file ":" $2 " " $5 " " $4
      }' \
    | LC_ALL=C sort
}

# Every diagnostic the fixture's markers declare, one row each.
#
# A marker that could never match is refused rather than compared: exit 2, naming the file and line.
# Each row must be exactly an analyzer ID and `error` or `warning` — not `suggestion`, which never
# reaches a build log, so a marker for one would be a missing row no fixture change could fix.
expected() {
  local path
  ( cd "$FIXTURE" && find . -name '*.cs' -not -path './obj/*' -not -path './bin/*' | LC_ALL=C sort ) \
    | while read -r path; do
        awk -v file="${path#./}" -v promoted="$PROMOTED" '{
          at = index($0, "expect:")
          if (!at) next
          n = split(substr($0, at + 7), rows, ",")
          for (k = 1; k <= n; k++) {
            if (split(rows[k], word, " ") != 2 || word[1] !~ /^[A-Za-z]+[0-9]+$/ || word[2] !~ /^(error|warning)$/) {
              print "diagnostic-diff: malformed expectation marker at " file ":" NR ": [" rows[k] "]; want <ID> error|warning" > "/dev/stderr"
              exit 2
            }
            print file ":" NR " " word[1] " " (promoted == "true" ? "error" : word[2])
          }
        }' "$FIXTURE/$path" || exit 2
      done \
    | LC_ALL=C sort
}

ACTUAL_ROWS="$(actual)"
EXPECTED_ROWS="$(expected)"

EXTRA="$(LC_ALL=C comm -23 <(echo "$ACTUAL_ROWS") <(echo "$EXPECTED_ROWS"))"
MISSING="$(LC_ALL=C comm -13 <(echo "$ACTUAL_ROWS") <(echo "$EXPECTED_ROWS"))"

[ -z "$EXTRA" ] && [ -z "$MISSING" ] && exit 0
[ -z "$EXTRA" ] || sed 's/^/+ /' <<< "$EXTRA"
[ -z "$MISSING" ] || sed 's/^/- /' <<< "$MISSING"
exit 1
