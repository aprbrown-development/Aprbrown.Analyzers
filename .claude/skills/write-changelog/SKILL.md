---
name: write-changelog
description: Audit every change since the last release tag, classify each by build impact, settle the next version number, and leave CHANGELOG.md and release tracking ready for /deploy.
disable-model-invocation: true
---

Prepare the next release: the `CHANGELOG.md` section, its version number, and promoted release tracking, landed as a pull request for the owner to review. Releasing it is `/deploy`, after the merge.

The rules this skill applies live in the repository, and are read rather than restated here:

- **Bump rules**: the Decision table and Consequences of `docs/adr/0004-diagnostic-ids-are-public-api.md`, and the table at the head of `CHANGELOG.md`.
- **Shipped surface**: the `<None Pack="true">` allowlist and the package metadata shown on nuget.org (`<Description>`, `<PackageTags>`, the licence and the URLs) in `src/Aprbrown.Analyzers.Package/Aprbrown.Analyzers.Package.csproj`. A change reaches a consumer only through one of those.
- **Voice**: the existing `CHANGELOG.md` sections. Entries open with a bold sentence stating what changed for the consumer, then why and what they do about it, linking `docs/consuming.md` anchors.

Every entry is **authored for the consumer**. A commit is a lead, never an entry: its message says what the implementer did, and the question each entry answers is what happens to a consumer's build.

## Steps

1. **Read the rules** above: ADR-0004, the head of `CHANGELOG.md` and the shipped surface. Done when you can name the bump for each row of the table at the head of `CHANGELOG.md`.

2. **Take the inventory.** `git fetch origin --tags`, then the last release is `git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude '*-*' origin/main` (prereleases are rehearsals of the release they prefix, never a baseline). List every change since it with `git log --reverse --no-merges --format='%h %s' <tag>..origin/main`, grouped by the pull request that merged it. Done when you hold the tag and the full commit list.

3. **Classify every commit** by reading its diff (`git show <sha>`), never its message alone. Each takes exactly one class:

   - **by design**: can fail a green build loudly (a rule enabled, a severity raised, a default property tightened)
   - **silently**: an existing rule now reports something it did not (a **detection fix**)
   - **voids config**: a rule removed or an `APB` ID renamed
   - **raises the floor**: the **toolchain floor** rises, failing every older SDK or IDE before any rule runs (a `Microsoft.CodeAnalysis.CSharp*` version raised in `Directory.Packages.props`)
   - **cannot fail**: a fixer added, a false positive removed, a consumer-visible doc change, a severity lowered
   - **no entry**: touches nothing in the shipped surface (tests, scripts, workflows, ADRs, repo-only docs)

   The test diff is the evidence that separates **silently** from **cannot fail**: a new test expecting a diagnostic where the old code reported none is a detection fix, whatever the commit calls itself; one expecting no diagnostic where there was one is a false positive removed. A change that does both gets both entries. `docs/consuming.md` is packed as the package README, so its corrections are **cannot fail**, not **no entry**; so are corrections to the package metadata. The floor is judged net of the last release: a later commit lowering part of a raise shares its entry, and the entry names the floor this version ships.

   Done when every commit in the inventory has one class and one line of evidence from its diff. Show the owner the table before writing.

4. **Choose the version**: the highest bump any class demands, applied to the last release's version: **voids config** and **raises the floor** are major, **by design** is minor, anything else is patch, and a release of **no entry** commits only is not a release (stop and say so). Done when you hold `X.Y.Z` and the one change that decided it.

5. **Write the section.** When the top `## ` heading of `CHANGELOG.md` names a version with no tag, it is the open section: audit it rather than starting over, renaming its heading to `## X.Y.Z` if step 4 chose differently. Otherwise add `## X.Y.Z` above the previous release. Use the three headings the previous sections use (`Fails a green build — by design`, `— silently`, `Cannot fail a green build`), each stating _Nothing_ with its reason when empty. **Raises the floor** goes under `Fails a green build — by design`, as in `2.0.0`. **Voids config** has no heading yet: when it occurs, propose one to the owner before writing it. Then rewrite the paragraph under the version heading so it summarises the final entries; it was likely written before the last of them landed. Reread the package `<Description>` the same way, since a rule added or removed can falsify it without any commit touching it; a correction is a **cannot fail** entry in this section.

   Done when every classified commit maps to exactly one entry, every entry maps to at least one commit, and both the summary paragraph and the package `<Description>` are true of what this version ships.

6. **Promote release tracking.** Move every rule row in `src/Aprbrown.Analyzers/AnalyzerReleases.Unshipped.md` into `AnalyzerReleases.Shipped.md` under a new `## Release X.Y.Z`, keeping their `### New Rules` / `### Changed Rules` / `### Removed Rules` subheadings, and leave `Unshipped.md` holding only its two comment lines. Nothing to move is the normal case for a patch. Done when `Unshipped.md` holds no rule rows.

7. **Check it.** `./scripts/verify-release.sh X.Y.Z` and `dotnet build Aprbrown.Analyzers.slnx -c Release` (the RS2000 rules fail the build on release tracking that disagrees with the analyzers). Done when both pass.

8. **Hand it over** as a pull request, its body written with the `pr` skill: the classification table from step 3 and the deciding change from step 4 are the evidence. Report the version, its deciding change, and the next step: merge, then `/deploy`.
