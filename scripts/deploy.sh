#!/usr/bin/env bash
#
# Release the version CHANGELOG.md's top section names: run the release-time gates, push its tag,
# follow release.yml through the approval gate, then confirm the package is on nuget.org and the
# GitHub Release exists. This is what `/deploy` runs.
#
# It covers only what follows the merge. Promoting AnalyzerReleases.Unshipped.md's rows and authoring
# the CHANGELOG.md section are judgement, made in a pull request, and they come first (README,
# "Releasing").
#
#   scripts/deploy.sh
#
# Rerunning is safe. A tag already on origin at main's commit is resumed rather than refused, so a
# run that timed out waiting for the approval click picks up where it stopped.

set -euo pipefail

PACKAGE_ID=aprbrown.analyzers
# Under /deploy's ten-minute Bash timeout, so a slow approval ends here with a message rather than
# as a killed process. A release run takes about two minutes once approved.
WAIT_SECONDS=540

fail() { echo "deploy: $*" >&2; exit 1; }

if (( $# )); then
  echo "deploy: unsupported option $1" >&2
  exit 2
fi

# The main checkout is the parent of the shared git dir, whichever worktree we were started from.
# The tag must name main's commit, and verify-release.sh reads the files of the checkout it runs in.
root=$(dirname "$(git rev-parse --path-format=absolute --git-common-dir)")
cd "$root"

branch=$(git branch --show-current)
[[ $branch == main ]] || fail "the main checkout is on '$branch', not main; refusing"
[[ -z $(git status --porcelain) ]] || fail "the main checkout has uncommitted changes; refusing"
git pull -q --ff-only
git fetch -q --tags origin
commit=$(git rev-parse HEAD)
echo "deploy: main at $(git log --oneline -1)"

# The version is the one the changelog's top section was authored for. That section is where the
# bump was decided (by build impact, ADR-0004), so it is read rather than typed a second time. The
# build still takes its version from the tag alone (ADR-0003 decision 2); this only picks the tag.
version=$(awk '$1 == "##" { print $2; exit }' CHANGELOG.md)
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
  || fail "CHANGELOG.md's top section is '## $version', not '## X.Y.Z'"
tag="v$version"

published() {
  curl -fsS "https://api.nuget.org/v3-flatcontainer/$PACKAGE_ID/index.json" \
    | grep -qF "\"$version\""
}
published && fail "$version is already on nuget.org; author the next CHANGELOG.md section first"

./scripts/verify-release.sh "$version"

# A tag is resumed only at main's own commit. Anywhere else it is a version already spent on other
# code, and the only fix is a new version, never a moved tag.
tagged=$(git rev-parse -q --verify "refs/tags/$tag^{commit}" || true)
if [[ -n $tagged && $tagged != "$commit" ]]; then
  fail "$tag already exists at ${tagged:0:7}, not main's ${commit:0:7}; refusing"
fi

if [[ -z $tagged ]]; then
  # A person at a terminal confirms the push here. Under /deploy, typing the command was the go-ahead,
  # and there is no terminal to answer.
  if [[ -t 0 ]]; then
    read -rp "deploy: push $tag? A nuget.org version cannot be reused. [y/N] " answer
    [[ $answer == [yY] ]] || fail "not pushed"
  fi
  # Annotated, with the tag name as its message, matching v1.0.0 and v1.0.0-preview.1.
  git tag -a "$tag" -m "$tag"
  git push -q origin "$tag"
  echo "deploy: pushed $tag"
else
  echo "deploy: $tag already pushed at ${commit:0:7}; resuming"
fi

deadline=$((SECONDS + WAIT_SECONDS))
repo=$(gh repo view --json nameWithOwner -q .nameWithOwner)

# The run appears a few seconds after the push, under the tag's name as its branch.
run=""
until [[ -n $run ]]; do
  (( SECONDS < deadline )) || fail "no Release run appeared for $tag"
  run=$(gh run list --workflow release.yml --branch "$tag" --limit 1 --json databaseId -q '.[0].databaseId')
  [[ -n $run ]] || sleep 5
done
url="https://github.com/$repo/actions/runs/$run"
echo "deploy: following $url"

announced=""
while :; do
  read -r status conclusion < <(gh run view "$run" --json status,conclusion -q '"\(.status) \(.conclusion)"')
  [[ $status == completed ]] && break
  if [[ $status == waiting && -z $announced ]]; then
    echo "deploy: waiting for approval of the 'release' environment: $url"
    announced=1
  fi
  if (( SECONDS >= deadline )); then
    fail "$tag is pushed but its run is still '$status' after ${WAIT_SECONDS}s; approve it at $url, then rerun to finish verifying"
  fi
  sleep 10
done
[[ $conclusion == success ]] || fail "the Release run ended '$conclusion': $url"
echo "deploy: Release run succeeded"

# A successful push can take a few minutes to appear in the flat container, which is what a
# consumer's restore reads. Success here means the version is installable, not just accepted.
until published; do
  (( SECONDS < deadline )) || fail "$version was pushed but is not yet listed on nuget.org; rerun to finish verifying"
  sleep 15
done
echo "deploy: $version is listed on nuget.org"

gh release view "$tag" --json url -q .url > /dev/null || fail "no GitHub Release for $tag"
echo "deploy: released $tag: https://www.nuget.org/packages/Aprbrown.Analyzers/$version"
