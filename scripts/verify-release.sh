#!/usr/bin/env bash
#
# The two checks that cannot exist before a tag.
#
# Everything else release.yml runs — build, test, verify-package.sh — already ran at PR time, and is
# re-run there to discover nothing new. These two are the exceptions, because neither has anything
# to check until a version number exists:
#
#   1. AnalyzerReleases.Unshipped.md holds no rule rows. The build already catches rules that were
#      never *recorded* — the RS2000-series is re-enabled in this repository's .editorconfig and
#      TreatWarningsAsErrors makes it fatal. This catches the other half: rules recorded but never
#      promoted to shipped. Deliberately version-agnostic rather than "the tag matches a
#      ## Release X.Y header", which would wrongly fail every patch release, since a detection fix
#      adds no section.
#   2. CHANGELOG.md has a non-empty section for this version, because release.yml cuts the release
#      body from it.
#
#   scripts/verify-release.sh 1.0.0
#
# UNSHIPPED_FILE is overridable for the tests in scripts/tests/release-gate-tests.sh, as is
# CHANGELOG_FILE — read not here but by changelog-section.sh, which inherits it. release.yml sets
# neither and gets the checkout's own files.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNSHIPPED_FILE="${UNSHIPPED_FILE:-$REPO_ROOT/src/Aprbrown.Analyzers/AnalyzerReleases.Unshipped.md}"

fail() { echo "RELEASE FAIL: $*" >&2; exit 1; }
info() { echo "  release: $*"; }

VERSION="${1:-}"
[ -n "$VERSION" ] || fail "usage: verify-release.sh <version>   (e.g. 1.0.0, without the v prefix)"

# --- Assertion: Unshipped.md holds no rule rows -----------------------------------------------
# A missing file is a failure rather than a vacuous pass. Release tracking is machine-enforced by
# the build, so the file cannot legitimately be absent; if it is, this check would otherwise report
# success for having read nothing — the failure shape that gates exist to refuse.
[ -f "$UNSHIPPED_FILE" ] || fail "no release tracking file at $UNSHIPPED_FILE"

# A rule row is a diagnostic ID — letters then digits — in the first cell. That shape is what
# separates a row from everything else the file contains: the ";" comment header, the "### New
# Rules" and "### Changed Rules" headings, the "Rule ID | Category | …" header row (two words before
# the pipe, so it cannot match) and the "--------|" separator. Matching the ID rather than the
# section heading is what makes this catch a *changed* rule too, which under
# TreatWarningsAsErrors breaks a consumer's build just as thoroughly as a new one.
ROWS="$(grep -nE '^[[:space:]]*[A-Za-z]+[0-9]+[[:space:]]*\|' "$UNSHIPPED_FILE" || true)"
if [ -n "$ROWS" ]; then
  echo "$ROWS" >&2
  fail "AnalyzerReleases.Unshipped.md still holds rule rows; promote them into AnalyzerReleases.Shipped.md under a '## Release X.Y' heading before tagging"
fi
info "AnalyzerReleases.Unshipped.md holds no rule rows"

# --- Assertion: the release body exists --------------------------------------------------------
# Delegated rather than reimplemented, for the same reason ci.yml invokes verify-package.sh instead
# of copying its assertions into YAML: a second parser is a second set of answers, free to disagree
# with the one that actually produces the release body.
if ! "$REPO_ROOT/scripts/changelog-section.sh" "$VERSION" > /dev/null; then
  fail "the release body cannot be cut from CHANGELOG.md for $VERSION"
fi
info "CHANGELOG.md has a non-empty section for $VERSION"

echo "RELEASE CHECKS PASS"
