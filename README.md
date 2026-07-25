# Aprbrown.Analyzers

Custom [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/) analyzers and code fixes for .NET, packaged for consumption as a NuGet analyzer package.

## Status

All three analyzers — `APB0001` (no primary constructors on classes or structs), `APB0002` (no
default value on a `CancellationToken` parameter) and `APB0003` (parameter names should match the
implemented interface member) — are proven end to end from a packed `.nupkg` by
`scripts/verify-package.sh`, and the `APB0003` code fix ships alongside them.

The shipped config is complete: [`docs/spec.md`](docs/spec.md) §2.3's derivation has run through
all five steps, leaving the blanket plus **150 enumerated rules** — 3 `APB`, 103
`Meziantou.Analyzer`, 29 SDK `CA`, 4 `StyleCop.Analyzers` ordering, and 11 IDE naming and style.
This repository is held to that ruleset itself (spec §7) and CI gates every pull request.

The release pipeline is in place: a `v*` tag builds, tests, runs the same smoke gate, waits for an
approval click, and publishes to nuget.org with a keyless one-hour credential. See
[Releasing](#releasing).

Still to come, tracked in the issue backlog: the nuget.org account and trusted publishing policy,
the `v1.0.0-preview.1` rehearsal, and onboarding the six fleet repositories.

## Documentation

- **[`docs/spec.md`](docs/spec.md)** — the implementation spec: what v1 ships, how the shipped
  configuration is derived, the three analyzers specified by behaviour, packaging, and the
  `MIX` → `APB` migration. Start here to build the package.
- **[`docs/consuming.md`](docs/consuming.md)** — how a project consumes the package. Packed as the
  package README, so it is also the nuget.org page.
- **[`CHANGELOG.md`](CHANGELOG.md)** — what each version does to a consumer's build, grouped by
  build impact. Each GitHub Release body is cut from it.
- **[`CONTEXT.md`](CONTEXT.md)** — vocabulary.
- **[`docs/adr/`](docs/adr/)** — the decisions behind all of the above.

## Planned layout

| Path | Purpose |
|---|---|
| `src/Aprbrown.Analyzers/` | The analyzers themselves (`DiagnosticAnalyzer` implementations) |
| `src/Aprbrown.Analyzers.CodeFixes/` | Matching `CodeFixProvider`s. Needs `Microsoft.CodeAnalysis.CSharp.Workspaces` — a fix that renames a symbol returns a changed solution, not a changed document |
| `src/Aprbrown.Analyzers.Package/` | NuGet packaging project that ships the analyzer + code fix assemblies |
| `tests/Aprbrown.Analyzers.Tests/` | Unit tests using `Microsoft.CodeAnalysis.Testing` verifiers |

Analyzers target `netstandard2.0` so they load in every supported host (MSBuild, VS, Rider, `dotnet build`). Test and packaging projects can target current .NET.

## Building

```bash
dotnet build Aprbrown.Analyzers.slnx
dotnet test  Aprbrown.Analyzers.slnx
```

Unit tests test the analyzer; they cannot test the *package*. The pack-consume smoke test does —
it packs to a temp feed, restores `tests/Fixture.Consumer` against it, builds, and asserts an
exact diagnostic set. Run it on any dev machine:

```bash
./scripts/verify-package.sh
```

`tests/Fixture.Consumer` is deliberately outside the solution: it only builds once the package has
been packed, so the smoke script drives it rather than `dotnet build`.

## Releasing

**The git tag is the only source of a version number.** No `<Version>` property exists anywhere in
this repository and nothing in it mints a publishable version on its own; a version exists only when
one is typed into `git tag`. nuget.org versions are immutable and unrevocable, so a version number is
spent the moment it is pushed.

Every gate therefore runs at pull request time, and
[`release.yml`](.github/workflows/release.yml) re-runs the same set expecting to discover nothing
new. It adds only the two checks that cannot exist before a tag, both in `scripts/verify-release.sh`
and both runnable locally:

```bash
./scripts/verify-release.sh 1.0.0        # the two release-time gates
./scripts/changelog-section.sh 1.0.0     # the release body, exactly as the workflow cuts it
```

1. `AnalyzerReleases.Unshipped.md` holds no rule rows — the build already catches rules that were
   never *recorded*; this catches rules recorded but never promoted to *shipped*.
2. `CHANGELOG.md` has a non-empty section for the version, because the GitHub Release body is cut
   from it. A prerelease tag reads the section of the version it prefixes: `v1.0.0-preview.1` is a
   rehearsal for `1.0.0` and carries that version's build impact.

To cut a release: promote `AnalyzerReleases.Unshipped.md`'s rows into `AnalyzerReleases.Shipped.md`
under a `## Release X.Y` heading, author the `CHANGELOG.md` section, merge, then tag. Publishing
pauses on the `release` environment for an approval click before anything reaches nuget.org, and
uses a single-use OIDC-issued key valid for one hour — there is no long-lived nuget.org credential
in this repository.

The one repository secret involved is **`NUGET_USER`**, holding the nuget.org *profile name* — not
the email address. It is what the trusted publishing policy is matched against; there is no
`NUGET_API_KEY`, and nothing to rotate.

## Adding an analyzer

1. Add the `DiagnosticAnalyzer` under `src/Aprbrown.Analyzers/`, with a unique diagnostic ID and a `DiagnosticDescriptor` in a shared descriptors file.
2. Add a matching `CodeFixProvider` under `src/Aprbrown.Analyzers.CodeFixes/` if the diagnostic is mechanically fixable.
3. Add tests under `tests/` covering both the triggering case and at least one near-miss that must *not* report.
4. Specify the rule in [`docs/spec.md`](docs/spec.md) §3 — ID, category, default severity, rationale, what it must *not* flag, and the required test cases — and add it to `AnalyzerReleases.Unshipped.md`.
5. Add it to the shipped configuration by ID. A rule absent from that allowlist is off, however the analyzer is written.
6. Record it in [`CHANGELOG.md`](CHANGELOG.md) under the version it will ship in, classified by build impact. A new rule can fail a build that was green on the previous version, and the changelog is the only place that says so — release tracking covers the rule table, not the consequence.

## Repository tooling

`.agents/skills/` and `.claude/skills/` hold shared agent skill definitions (the `.claude` entries are symlinks into `.agents`), pinned by `skills-lock.json`. They are development tooling and are not part of the shipped package.

## Licence

[MIT](LICENSE) — `Copyright (c) 2026 Andrew P R Brown`.

The package declares this as an SPDX expression
(`<PackageLicenseExpression>MIT</PackageLicenseExpression>`) rather than shipping a licence file,
so `LICENSE` above is not packed into the `.nupkg`.
