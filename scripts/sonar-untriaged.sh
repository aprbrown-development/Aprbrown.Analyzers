#!/usr/bin/env bash
#
# List the SonarQube C# rules firing in the fleet that have no Sonar verdict yet.
#
#   SONAR_HOST_URL=<server> SONAR_TOKEN=<token> sonar-untriaged.sh [spec]
#
# Asks the server for every project's open csharpsquid issues and compares the rules they fire
# against the verdict table in section 2.5 of the spec (docs/spec.md unless another path is given).
# Prints each firing rule with no row, with its name and open count per project, then each covered
# rule that still has open issues, since a covered rule should never fire. Exits 1 when any rule is
# untriaged, 0 when none is, and 2 when it cannot ask the server or read the table.
#
# The token reaches curl as a config file on stdin, never as an argument, so it shows in neither
# the process list nor the output.

set -euo pipefail
shopt -s inherit_errexit

usage() { echo "usage: SONAR_HOST_URL=<server> SONAR_TOKEN=<token> sonar-untriaged.sh [spec]" >&2; exit 2; }

[ $# -le 1 ] || usage
[ -n "${SONAR_HOST_URL:-}" ] && [ -n "${SONAR_TOKEN:-}" ] || usage

SPEC="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/docs/spec.md}"
HOST="${SONAR_HOST_URL%/}"

sonar_get() {
  curl -fsS -K - "$HOST/api/$1" <<< "user = \"$SONAR_TOKEN:\"" || {
    echo "sonar-untriaged: request for api/${1%%\?*} failed" >&2
    exit 2
  }
}

# One `<rule> TAB <verdict>` line per row of the section 2.5 table.
verdicts() {
  awk -F'|' '
    /^### 2\.5 / { section = 1; next }
    section && /^#/ { exit }
    section && $2 ~ /^ *`S[0-9]+`/ {
      split($2, cell, "`")
      verdict = $3
      gsub(/^ +| +$/, "", verdict)
      print cell[2] "\t" verdict
    }
  ' "$1"
}

# One `<rule> TAB <name> TAB <project> TAB <open count>` line per csharpsquid rule firing in a project.
firing() {
  local projects key encoded
  projects="$(sonar_get 'components/search?qualifiers=TRK&ps=500')"
  if jq -e '.paging.total > (.components | length)' <<< "$projects" > /dev/null; then
    echo "sonar-untriaged: more projects than one page lists" >&2
    exit 2
  fi
  while IFS=$'\t' read -r key encoded; do
    sonar_get "issues/search?componentKeys=$encoded&issueStatuses=OPEN,CONFIRMED&languages=cs&facets=rules&additionalFields=rules&ps=1" |
      jq -r --arg project "$key" '
        (.rules | map({(.key): .name}) | add // {}) as $names
        | .facets[] | select(.property == "rules") | .values[]
        | select(.val | startswith("csharpsquid:"))
        | [(.val | ltrimstr("csharpsquid:")), ($names[.val] // "(name unknown)"), $project, .count]
        | @tsv'
  done < <(jq -r '.components[] | [.key, (.key | @uri)] | @tsv' <<< "$projects")
}

# Print the firing lines on stdin grouped by rule, under the heading given.
report() {
  local heading="$1" lines
  lines="$(cat)"
  if [ -z "$lines" ]; then
    echo "$heading: none"
    return
  fi
  echo "$heading:"
  LC_ALL=C sort -t$'\t' -k1,1 -k3,3 <<< "$lines" | awk -F'\t' '
    $1 != rule { rule = $1; print "  " $1 " " $2 }
    { print "    " $3 ": " $4 }
  '
}

VERDICTS="$(verdicts "$SPEC")"
if [ -z "$VERDICTS" ]; then
  echo "sonar-untriaged: no verdict rows in section 2.5 of $SPEC" >&2
  exit 2
fi
FIRING="$(firing)"

# The firing lines whose rule matches the awk predicate given, over verdict[<rule>].
firing_where() {
  awk -F'\t' "NR == FNR { verdict[\$1] = \$2; next } $1" <(echo "$VERDICTS") <(echo "$FIRING") |
    sed '/^$/d'
}

UNTRIAGED="$(firing_where '!($1 in verdict)')"
COVERED="$(firing_where 'verdict[$1] == "covered"')"

report 'Untriaged rules' <<< "$UNTRIAGED"
report 'Covered rules with open issues' <<< "$COVERED"
[ -z "$UNTRIAGED" ]
