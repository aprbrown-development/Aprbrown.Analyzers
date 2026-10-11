#!/usr/bin/env bash
#
# Compare a config against the expected ruleset (#51).
#
#   ruleset-diff.sh <config> <expected>
#
# Exits 0 when both files hold the same settings. Otherwise prints every line that differs and exits
# 1: `+ line` for a setting only the config has, `- line` for one only the expected ruleset has. A
# re-levelled rule therefore shows as a pair, the line it was meant to be and the line it is.
#
# A setting is any line that is not blank or a # comment, trimmed, with the spacing around its first
# = collapsed to ` = `, so the expected ruleset can carry its own grouping and commentary and the
# comparison still sees only what the config sets. The comparison is over a multiset, not a set: a
# line the config enumerates twice is a difference, as the digests it replaced (#51) also made it.

set -euo pipefail

[ $# -eq 2 ] || { echo "usage: ruleset-diff.sh <config> <expected>" >&2; exit 2; }

settings() {
  sed -E \
    -e 's/^[[:space:]]+//' -e 's/[[:space:]]+$//' \
    -e '/^(#|$)/d' \
    -e 's/[[:space:]]*=[[:space:]]*/ = /' \
    "$1" | LC_ALL=C sort
}

CONFIG_LINES="$(settings "$1")"
EXPECTED_LINES="$(settings "$2")"

EXTRA="$(LC_ALL=C comm -23 <(echo "$CONFIG_LINES") <(echo "$EXPECTED_LINES"))"
MISSING="$(LC_ALL=C comm -13 <(echo "$CONFIG_LINES") <(echo "$EXPECTED_LINES"))"

[ -z "$EXTRA" ] && [ -z "$MISSING" ] && exit 0
[ -z "$EXTRA" ] || sed 's/^/+ /' <<< "$EXTRA"
[ -z "$MISSING" ] || sed 's/^/- /' <<< "$MISSING"
exit 1
