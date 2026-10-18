#!/usr/bin/env bash
#
# Print one version's CHANGELOG.md section to stdout.
#
# release.yml cuts the GitHub Release body from this, so there is exactly one authored source and
# the release page cannot disagree with the repository. Release notes are deliberately *not*
# generated from commit messages: commits describe what the implementer did, and this changelog has
# to answer what happens to the consumer's build.
#
# The heading is not printed. GitHub renders the release title from the tag, and a body that opened
# with its own "## 1.0.0" would say the version twice — once from the tag and once from a heading
# free to disagree with it.
#
#   scripts/changelog-section.sh 1.0.0
#   CHANGELOG_FILE=/path/to/CHANGELOG.md scripts/changelog-section.sh 1.0.0
#
# Exits non-zero if the section is absent or empty. An absent section must fail rather than return
# nothing: `gh release create --notes-file` accepts an empty file happily, so silence here would
# publish a blank release page against a version number that has already been spent.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHANGELOG_FILE="${CHANGELOG_FILE:-$REPO_ROOT/CHANGELOG.md}"

fail() { echo "CHANGELOG: $*" >&2; exit 1; }

VERSION="${1:-}"
[ -n "$VERSION" ] || fail "usage: changelog-section.sh <version>   (e.g. 1.0.0, without the v prefix)"
[ -f "$CHANGELOG_FILE" ] || fail "no changelog at $CHANGELOG_FILE"

# A prerelease resolves to the section of the version it prefixes: 1.0.0-preview.1 reads ## 1.0.0.
# A prerelease is a rehearsal for the release that follows it, and a rehearsal has nothing of its
# own to say about build impact — the impact it carries is that
# version's, and duplicating the section under a second heading would create two authored sources
# free to drift, which is the exact thing cutting the body from the changelog exists to prevent.
SECTION_VERSION="${VERSION%%-*}"

# Matched by field equality rather than by regex, because every version number is mostly dots and a
# dot in a regex matches anything — but more importantly because a prefix match would find
# "## 1.0.10" when asked for "1.0.1" and publish one version's notes under another's tag. $1 == "##"
# also leaves "### …" subheadings inside a section alone: they are part of the body, not the end of
# it. A trailing date or title on the heading is ignored; only the version field is read.
#
# The `"" ==` is not decoration. An awk -v assignment carries strnum semantics, so when both sides
# look like numbers awk compares them *numerically* — and "1.10" == "1.1" is true to a number. That
# is the same wrong-notes-under-the-wrong-tag failure the field match exists to prevent, arriving by
# the other door. Concatenating an empty string forces both sides to strings. Three-part versions
# are not numbers and so were never at risk, which is exactly why this would have sat here unnoticed
# until the day a two-part heading was written.
section_exists() {
  awk -v v="$SECTION_VERSION" '
    $1 == "##" && ($2 "") == (v "") { found = 1; exit }
    END { exit !found }
  ' "$CHANGELOG_FILE"
}

section_body() {
  awk -v v="$SECTION_VERSION" '
    $1 == "##" {
      if (found) exit
      if (($2 "") == (v "")) { found = 1; next }
    }
    found { print }
  ' "$CHANGELOG_FILE"
}

section_exists || fail "no '## $SECTION_VERSION' section in $CHANGELOG_FILE; author it before tagging"

# Command substitution strips the trailing blank lines; sed strips the leading ones. Both exist
# because the section is bounded by headings that are surrounded by blank lines in any readable
# Markdown, and neither belongs in a release body.
BODY="$(section_body | sed -e '/./,$!d')"
[ -n "$BODY" ] || fail "the '## $SECTION_VERSION' section is empty; a blank release body is not a release note"

printf '%s\n' "$BODY"
