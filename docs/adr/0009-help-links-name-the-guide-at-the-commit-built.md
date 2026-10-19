# ADR-0009: Help links name the guide at the commit built, and the build supplies the repository

- **Status:** Accepted
- **Date:** 2026-10-19
- **Amends:** [ADR-0003](0003-tag-triggered-trusted-publishing-with-pr-time-gates.md) (where
  `PackageProjectUrl`, `RepositoryUrl` and `HelpLinkUri` come from)

## Context

Every `APB` descriptor's `HelpLinkUri` was a literal `…/blob/main/docs/consuming.md#the-custom-rules`,
and `PackageProjectUrl` and `RepositoryUrl` repeated the repository address. SonarQube reports the
literal under `S1075` (URIs should not be hardcoded), and it had already gone stale once: renaming
"The three custom rules" in 2.1.0 left every earlier package's link landing at the top of the
guide, because a link into `main` follows the guide's present, not the version the consumer has.

A link baked into a published assembly cannot change after the fact. What can be chosen is what
each build bakes in.

## Decision

- **The repository address comes from the git `origin` remote**, as the SDK's Source Link reads it
  (`PrivateRepositoryUrl`), with any `.git` suffix removed. `PackageProjectUrl` is set from it at
  pack time, and `PublishRepositoryUrl` sets `RepositoryUrl` and the nuspec's commit.
- **A help link names the guide at the commit being built**:
  `<repository>/blob/<SourceRevisionId>/<ConsumingGuidePath>#the-custom-rules`. The build writes
  the part before the fragment into a generated `ConsumingGuide.Url` constant.
- **The guide's path is one property**, `ConsumingGuidePath` in `Directory.Build.props`, read by the
  generated constant, the package's README entry and the test project's copy of the guide.
- **A build with no `origin` remote or commit fails**, rather than shipping a link with no
  repository in it.
- The descriptor contract test checks the link's shape (a file on GitHub at a 40-character commit),
  that its path is `ConsumingGuidePath`, and that its fragment is a heading of that file. It no
  longer checks the repository's name, since following a move is the point.

## Consequences

- A shipped link keeps working when the guide is moved, renamed or re-headed, since that commit's
  copy never changes. A renamed or transferred repository is covered by GitHub's redirect, unless a
  new repository later takes the old name.
- A consumer reads the guide that described their version, not the latest one.
- After a move, the next build picks up the new location without anyone editing a file; moving the
  guide is a one-line change to `ConsumingGuidePath`.
- A local build names a commit that may not have been pushed yet. Only builds from pushed commits,
  in CI and at a tag, are ever shipped.
- Building from a source archive with no git metadata no longer works.
