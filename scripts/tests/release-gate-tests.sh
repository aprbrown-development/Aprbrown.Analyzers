#!/usr/bin/env bash
#
# Tests for the two release-time gates (ADR-0003 decisions 8 and 9).
#
# verify-package.sh is an assertion over a real artifact and its failure mode is loud; its few
# tests, in verify-package-tests.sh, pin only that it refuses a broken package it is handed. These
# two are different in kind. They are text parsers over Markdown, and a text parser that quietly
# matches nothing looks exactly like a gate that passed — which is the failure shape #2 produced a
# package for (built clean, enforced nothing) and the one this repository has spent the most effort
# refusing to repeat.
#
# What is worth pinning is precisely what a hand-check would not notice:
#   - a rule row in Unshipped.md is detected, and the table furniture around it is not mistaken for
#     one, in either direction;
#   - a version section is matched exactly, so `1.0.1` never reads `1.0.10`'s section and ships the
#     wrong release notes against an unrevocable push;
#   - a prerelease tag resolves to the section of the version it prefixes, which is the one piece of
#     deliberate cleverness in either script and therefore the one most worth a test.
#
# Run directly, or via ci.yml, which runs it at PR time for the reason every other gate runs there:
# a release-time check that first proves itself at release time has already cost a version number.

# No `set -e`, unlike every other script in scripts/ — deliberately, and this is the one place in
# the repository where its absence is correct. Over half the cases here run a script that is
# *expected* to exit non-zero, and `out="$(...)"` under `set -e` would kill the harness on the first
# one rather than record it as a pass. The exit code is read explicitly instead, on the line after
# every invocation, which is the whole job of assert_exits.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPTS="$REPO_ROOT/scripts"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASSED=0
FAILED=0

# `if`, not `[ … ] && echo`. The short-circuit form makes the function's exit status depend on
# whether a detail was passed, which is a trap primed for the day someone restores `set -e` above.
ok()      { PASSED=$((PASSED + 1)); echo "  ok: $1"; }
not_ok()  {
  FAILED=$((FAILED + 1))
  echo "NOT OK: $1" >&2
  if [ $# -gt 1 ]; then echo "        $2" >&2; fi
}

# Every case is "run one of the scripts over a fixture and check how it exited", so the two helpers
# below are the whole harness. Output is captured rather than shown because a passing negative case
# is *meant* to print a failure message, and a wall of expected errors trains the eye to skip the
# unexpected one.
assert_exits() {
  local want="$1" name="$2"; shift 2
  local out rc
  out="$("$@" 2>&1)"; rc=$?
  if [ "$rc" -eq "$want" ]; then
    ok "$name"
  else
    not_ok "$name" "expected exit $want, got $rc; output: $out"
  fi
}

assert_stdout() {
  local want="$1" name="$2"; shift 2
  local out rc
  out="$("$@" 2>/dev/null)"; rc=$?
  if [ "$rc" -ne 0 ]; then
    not_ok "$name" "script exited $rc"
  elif [ "$out" = "$want" ]; then
    ok "$name"
  else
    not_ok "$name" "expected [$want], got [$out]"
  fi
}

# --- fixtures --------------------------------------------------------------------------------
# Written as files rather than piped in, because both scripts take paths: the workflow points them
# at the checkout, and a test that fed them stdin would be testing something the workflow never does.
write() { local path="$1"; shift; printf '%s\n' "$@" > "$path"; }

UNSHIPPED_CLEAN="$WORK/unshipped-clean.md"
write "$UNSHIPPED_CLEAN" \
  '; Unshipped analyzer release' \
  '; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md'

# The state a release actually leaves behind if the author empties the table rather than deleting
# it: headings and furniture, no rows. It must pass — "no rule rows" is the assertion, not "no
# Markdown".
UNSHIPPED_EMPTY_TABLE="$WORK/unshipped-empty-table.md"
write "$UNSHIPPED_EMPTY_TABLE" \
  '; Unshipped analyzer release' \
  '' \
  '### New Rules' \
  '' \
  'Rule ID | Category | Severity | Notes' \
  '--------|----------|----------|--------------------------------------'

UNSHIPPED_NEW_RULE="$WORK/unshipped-new-rule.md"
write "$UNSHIPPED_NEW_RULE" \
  '; Unshipped analyzer release' \
  '' \
  '### New Rules' \
  '' \
  'Rule ID | Category | Severity | Notes' \
  '--------|----------|----------|--------------------------------------' \
  'APB0004 | Design   | Warning  | SomeAnalyzer, [Documentation](https://example.invalid)'

# A severity change records no *new* rule, and it is the case a check that only looked under a
# "### New Rules" heading would walk straight past — while being, under TreatWarningsAsErrors,
# every bit as capable of turning a consumer's green build red.
UNSHIPPED_CHANGED_RULE="$WORK/unshipped-changed-rule.md"
write "$UNSHIPPED_CHANGED_RULE" \
  '; Unshipped analyzer release' \
  '' \
  '### Changed Rules' \
  '' \
  'Rule ID | New Category | New Severity | Old Category | Old Severity | Notes' \
  '--------|--------------|--------------|--------------|--------------|-------' \
  'APB0002 | Design       | Error        | Design       | Warning      | Raised'

CHANGELOG="$WORK/CHANGELOG.md"
write "$CHANGELOG" \
  '# Changelog' \
  '' \
  'Preamble prose, which is not a version section and must never be returned as one.' \
  '' \
  '## 1.0.10' \
  '' \
  'Body of the ten.' \
  '' \
  '## 1.0.1 — 2026-08-01' \
  '' \
  'Body of the one.' \
  '' \
  '## 1.0.0' \
  '' \
  'Body of the first.' \
  '' \
  '### A subheading' \
  '' \
  'Second paragraph of the first.'

# Two-part headings, which are the only ones that look like numbers to awk. Kept in a changelog of
# their own so the realistic three-part fixture above stays readable.
CHANGELOG_TWO_PART="$WORK/CHANGELOG-two-part.md"
write "$CHANGELOG_TWO_PART" \
  '# Changelog' \
  '' \
  '## 1.1' \
  '' \
  'Body of one-one.' \
  '' \
  '## 1.10' \
  '' \
  'Body of one-ten.'

CHANGELOG_EMPTY_SECTION="$WORK/CHANGELOG-empty.md"
write "$CHANGELOG_EMPTY_SECTION" \
  '# Changelog' \
  '' \
  '## 2.0.0' \
  '' \
  '## 1.0.0' \
  '' \
  'Body of the first.'

# --- changelog-section.sh ---------------------------------------------------------------------
SECTION="$SCRIPTS/changelog-section.sh"

assert_stdout 'Body of the first.

### A subheading

Second paragraph of the first.' \
  'a section runs to the next ## heading, and ### subheadings do not end it' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION" 1.0.0

# The trap this test exists for. A prefix match for "1.0.1" finds "## 1.0.10" first, and the release
# body for one version would be the notes of another — silently, against an unrevocable push.
assert_stdout 'Body of the one.' \
  'a version heading matches exactly, not by prefix' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION" 1.0.1

assert_stdout 'Body of the ten.' \
  'a trailing date on the heading does not stop the match' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION" 1.0.10

# The one deliberate piece of cleverness: a prerelease is a rehearsal for the version it prefixes
# (ADR-0003 decision 11), and it has nothing truthful of its own to say about build impact — the
# build impact it carries is that version's.
assert_stdout 'Body of the first.

### A subheading

Second paragraph of the first.' \
  'a prerelease tag resolves to the section of the version it prefixes' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION" 1.0.0-preview.1

# The same trap by the other door, and the one that actually bit. An awk -v assignment carries
# strnum semantics, so two headings that both look like numbers are compared *as* numbers — and
# 1.10 equals 1.1 to a number. Three-part versions are not numbers, so every test above passed
# while this was broken. These two are what make the string coercion in changelog-section.sh
# something other than decoration.
assert_stdout 'Body of one-ten.' \
  'a two-part heading is matched as a string, not as a number' \
  env CHANGELOG_FILE="$CHANGELOG_TWO_PART" "$SECTION" 1.10

assert_stdout 'Body of one-one.' \
  'the version it would have been confused with still resolves to itself' \
  env CHANGELOG_FILE="$CHANGELOG_TWO_PART" "$SECTION" 1.1

assert_exits 1 'an absent version section fails rather than returning nothing' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION" 9.9.9

assert_exits 1 'a present but empty section fails; an empty release body is not a release note' \
  env CHANGELOG_FILE="$CHANGELOG_EMPTY_SECTION" "$SECTION" 2.0.0

assert_exits 1 'a missing changelog file fails' \
  env CHANGELOG_FILE="$WORK/does-not-exist.md" "$SECTION" 1.0.0

assert_exits 1 'no version argument fails' \
  env CHANGELOG_FILE="$CHANGELOG" "$SECTION"

# --- verify-release.sh ------------------------------------------------------------------------
VERIFY="$SCRIPTS/verify-release.sh"

assert_exits 0 'a clean Unshipped.md and a present section pass' \
  env UNSHIPPED_FILE="$UNSHIPPED_CLEAN" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 1.0.0

assert_exits 0 'an emptied table is not a rule row' \
  env UNSHIPPED_FILE="$UNSHIPPED_EMPTY_TABLE" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 1.0.0

assert_exits 1 'a new rule left unshipped fails the release' \
  env UNSHIPPED_FILE="$UNSHIPPED_NEW_RULE" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 1.0.0

assert_exits 1 'a changed rule left unshipped fails the release' \
  env UNSHIPPED_FILE="$UNSHIPPED_CHANGED_RULE" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 1.0.0

assert_exits 1 'a missing changelog section fails the release' \
  env UNSHIPPED_FILE="$UNSHIPPED_CLEAN" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 9.9.9

assert_exits 1 'a missing Unshipped.md fails rather than passing vacuously' \
  env UNSHIPPED_FILE="$WORK/does-not-exist.md" CHANGELOG_FILE="$CHANGELOG" "$VERIFY" 1.0.0

assert_exits 1 'no version argument fails' \
  env UNSHIPPED_FILE="$UNSHIPPED_CLEAN" CHANGELOG_FILE="$CHANGELOG" "$VERIFY"

# --- the real files -----------------------------------------------------------------------------
# The cases above run over fixtures, which proves the parsers and nothing about this repository.
# This one runs the changelog side over the committed CHANGELOG.md, so the section the first release
# will be cut from is known to exist and to be non-empty *now* rather than at tag time.
assert_exits 0 'the committed CHANGELOG.md has a non-empty 1.0.0 section' \
  "$SECTION" 1.0.0

# Unshipped.md deliberately gets no equivalent: between releases it legitimately holds rules no
# release has promoted yet, so it is only required to be empty at tag time, by verify-release.sh.

echo
echo "release gate tests: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
