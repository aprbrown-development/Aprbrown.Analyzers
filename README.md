# Aprbrown.Analyzers

One .NET coding standard, delivered as a NuGet package.

It ships three custom [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/) analyzers
(`APB0001`–`APB0003`, one with a code fix) and — the larger half — a configuration that decides which
**150 rules** from `Meziantou.Analyzer`, `StyleCop.Analyzers` and the .NET SDK's own `CA` and `IDE`
analyzers are switched on. There is a single universal ruleset: no profiles, no presets, nothing to
configure. Anything you disagree with, you override in your own `.editorconfig`, which always wins.

C# only, and it needs a toolchain hosting Roslyn 4.14 or newer (.NET SDK 9.0.300+, Visual Studio 2022
17.14+).

## Installing

**→ [`docs/consuming.md`](docs/consuming.md) is the install guide**, and is also the package's
nuget.org page. In short: reference three packages from your `Directory.Build.props`, then add the
`.editorconfig` entries that a package cannot carry for you.

```xml
<PackageReference Include="Aprbrown.Analyzers"  Version="1.0.0"          PrivateAssets="all" />
<PackageReference Include="Meziantou.Analyzer"  Version="3.0.123"        PrivateAssets="all" />
<PackageReference Include="StyleCop.Analyzers"  Version="1.2.0-beta.556" PrivateAssets="all" />
```

The package takes no dependency on the other two: it configures their rules without bundling their
assemblies, so your repository pins the versions. Adopting it switches `TreatWarningsAsErrors` on by
default, so it will fail builds that currently pass — read the install guide before starting.

## The three custom rules

- **`APB0001`** — no primary constructors on classes or structs. Injected dependencies belong in an
  explicit constructor assigning readonly fields. Records are exempt.
- **`APB0002`** — no default value on a `CancellationToken` parameter. A defaulted token lets a caller
  drop cancellation invisibly at the call site.
- **`APB0003`** — an interface implementation's parameter names must match the interface's. `CA1725`
  covers base-class overrides but is blind to interface implementations; this is the missing half.
  Ships with a code fix.

Full detail, including the other 147 rules and everything deliberately left off, is in
[`docs/consuming.md`](docs/consuming.md#the-rules).

## Documentation

| Document | Who it is for |
|---|---|
| [`docs/consuming.md`](docs/consuming.md) | **Consumers.** Install, configure, override, troubleshoot. Packed as the package README, so it is also the nuget.org page. |
| [`CHANGELOG.md`](CHANGELOG.md) | **Upgraders.** What each version does to a consumer's build, grouped by build impact. Each GitHub Release body is cut from it. |
| [`docs/spec.md`](docs/spec.md) | **Contributors.** The implementation spec: what v1 ships, how the configuration is derived, the three analyzers specified by behaviour, packaging. |
| [`GLOSSARY.md`](GLOSSARY.md) | Vocabulary used across the other documents. |
| [`docs/adr/`](docs/adr/) | The decisions behind all of the above, and why. |

## Repository status

The three analyzers and the `APB0003` code fix are proven end to end from a packed `.nupkg` by
`scripts/verify-package.sh`, and the shipped configuration is complete at 150 enumerated rules. This
repository is held to that ruleset itself, and CI gates every pull request against it.

The release pipeline is in place: a `v*` tag builds, tests, runs the same pack-consume gate, waits for
an approval click, and publishes to nuget.org with a keyless one-hour credential.

**`1.0.0` is published on [nuget.org](https://www.nuget.org/packages/Aprbrown.Analyzers).**
Onboarding the rest of the fleet is tracked in the
[issue backlog](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues).

## Development

### Layout

| Path | Purpose |
|---|---|
| `src/Aprbrown.Analyzers/` | The analyzers themselves (`DiagnosticAnalyzer` implementations), `netstandard2.0` |
| `src/Aprbrown.Analyzers.CodeFixes/` | Matching `CodeFixProvider`s, `netstandard2.0`. Needs `Microsoft.CodeAnalysis.CSharp.Workspaces` — a fix that renames a symbol returns a changed solution, not a changed document |
| `src/Aprbrown.Analyzers.Package/` | Packaging project. Ships the two assemblies, the MSBuild props and the shipped configuration; builds no code of its own |
| `tests/Aprbrown.Analyzers.Tests/` | Unit tests using `Microsoft.CodeAnalysis.Testing` verifiers |
| `tests/Fixture.Consumer/` | Consumer fixture for the pack-consume smoke test. Deliberately outside the solution |

The analyzers target `netstandard2.0` and pin Roslyn to `4.14.0` so they load in every supported host
(MSBuild, Visual Studio, Rider, `dotnet build`) — an analyzer built against an older Roslyn loads in a
newer host, never the reverse. Test and packaging projects target current .NET.

### Building and testing

```bash
dotnet build Aprbrown.Analyzers.slnx
dotnet test  Aprbrown.Analyzers.slnx
```

Unit tests test the analyzers; they cannot test the *package*. The pack-consume smoke test does — it
takes a packed package, restores `tests/Fixture.Consumer` against it from a temp feed, builds, and
asserts an exact diagnostic set. It never packs one itself, so the package a release pushes is the
one it gated ([ADR-0003](docs/adr/0003-tag-triggered-trusted-publishing-with-pr-time-gates.md)
amendment 3). Run it on any dev machine by packing a smoke version first:

```bash
dotnet pack src/Aprbrown.Analyzers.Package/Aprbrown.Analyzers.Package.csproj -c Release \
  -o artifacts -p:Version=0.0.1-smoke
./scripts/verify-package.sh artifacts/Aprbrown.Analyzers.0.0.1-smoke.nupkg
```

`./scripts/tests/verify-package-tests.sh` checks, in seconds and without the SDK, that the gate
refuses deliberately broken packages, `./scripts/tests/ruleset-diff-tests.sh` that its comparison
with the expected ruleset names every line that differs, and `./scripts/tests/diagnostic-diff-tests.sh`
that its comparison of a fixture build against the fixture's expectation markers does the same.

`tests/Fixture.Consumer` is outside the solution because it only builds once the package has been
packed, so the smoke script drives it rather than `dotnet build`. Each line in it that should raise a
diagnostic says so in a trailing `// expect: <ID> <severity>` comment, at the severity the rule ships
at; any line without one must stay silent. A new fixture case is the violating line and its marker.

### Adding an analyzer

1. Add the `DiagnosticAnalyzer` under `src/Aprbrown.Analyzers/`, with a unique diagnostic ID and a
   `DiagnosticDescriptor` in `Descriptors.cs`.
2. Add a matching `CodeFixProvider` under `src/Aprbrown.Analyzers.CodeFixes/` if the diagnostic is
   mechanically fixable.
3. Add tests under `tests/` covering both the triggering case and at least one near-miss that must
   *not* report.
4. Specify the rule in [`docs/spec.md`](docs/spec.md) §3 — ID, category, default severity, rationale,
   what it must *not* flag, and the required test cases — and add it to `AnalyzerReleases.Unshipped.md`.
5. Add it to the shipped configuration by ID. A rule absent from that allowlist is off, however the
   analyzer is written. Add the same line to [`tests/expected-ruleset.txt`](tests/expected-ruleset.txt),
   the expected ruleset the shipped config is compared against line for line; any change to the
   house ruleset is made in both files.
6. Document it in [`docs/consuming.md`](docs/consuming.md) — that document is where a consumer learns
   what a rule does, and every diagnostic's IDE help link points at it.
7. Record it in [`CHANGELOG.md`](CHANGELOG.md) under the version it will ship in, classified by build
   impact. A new rule can fail a build that was green on the previous version, and the changelog is the
   only place that says so — release tracking covers the rule table, not the consequence.

### Releasing

**The git tag is the only source of a version number.** No `<Version>` property exists anywhere in this
repository and nothing in it mints a publishable version on its own; a version exists only when one is
typed into `git tag`. nuget.org versions are immutable and unrevocable, so a version number is spent the
moment it is pushed.

Every gate therefore runs at pull request time, and
[`release.yml`](.github/workflows/release.yml) re-runs the same set expecting to discover nothing new.
It adds only the two checks that cannot exist before a tag, both in `scripts/verify-release.sh` and both
runnable locally:

```bash
./scripts/verify-release.sh 1.0.0        # the two release-time gates
./scripts/changelog-section.sh 1.0.0     # the release body, exactly as the workflow cuts it
```

1. `AnalyzerReleases.Unshipped.md` holds no rule rows — the build already catches rules that were never
   *recorded*; this catches rules recorded but never promoted to *shipped*.
2. `CHANGELOG.md` has a non-empty section for the version, because the GitHub Release body is cut from
   it. A prerelease tag reads the section of the version it prefixes: `v1.0.0-preview.1` is a rehearsal
   for `1.0.0` and carries that version's build impact.

To cut a release: promote `AnalyzerReleases.Unshipped.md`'s rows into `AnalyzerReleases.Shipped.md` under
a `## Release X.Y` heading, author the `CHANGELOG.md` section, merge, then tag. Publishing pauses on the
`release` environment for an approval click before anything reaches nuget.org, and uses a single-use
OIDC-issued key valid for one hour — there is no long-lived nuget.org credential in this repository.

The one repository secret involved is **`NUGET_USER`**, holding the nuget.org *profile name* — not the
email address. It is what the trusted publishing policy is matched against; there is no `NUGET_API_KEY`,
and nothing to rotate.

### Repository tooling

Agent skills come from the globally installed `mattpocock-skills` plugin rather than from this
repository. `docs/agents/` configures them for this repository: the issue tracker, the triage labels
and the domain docs. None of it is part of the shipped package.

## Licence

[MIT](LICENSE) — `Copyright (c) 2026 Andrew P R Brown`.

The package declares this as an SPDX expression
(`<PackageLicenseExpression>MIT</PackageLicenseExpression>`) rather than shipping a licence file, so
`LICENSE` above is not packed into the `.nupkg`.
