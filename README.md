# Aprbrown.Analyzers

Custom [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/) analyzers and code fixes for .NET, packaged for consumption as a NuGet analyzer package.

## Status

Early — the tracer bullet is in. The repository skeleton is stood up and all three analyzers —
`APB0001` (no primary constructors on classes or structs), `APB0002` (no default value on a
`CancellationToken` parameter) and `APB0003` (parameter names should match the implemented
interface member) — are proven end to end from a packed `.nupkg` by `scripts/verify-package.sh`.
The `APB0003` code fix ships alongside them. The shipped config so far carries the blanket plus
those three; the full ruleset, dogfooding and CI are additive work tracked in the issue backlog.
The sections below describe the intended final shape of the project.

## Documentation

- **[`docs/spec.md`](docs/spec.md)** — the implementation spec: what v1 ships, how the shipped
  configuration is derived, the three analyzers specified by behaviour, packaging, and the
  `MIX` → `APB` migration. Start here to build the package.
- **[`docs/consuming.md`](docs/consuming.md)** — how a project consumes the package. Packed as the
  package README, so it is also the nuget.org page.
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

## Adding an analyzer

1. Add the `DiagnosticAnalyzer` under `src/Aprbrown.Analyzers/`, with a unique diagnostic ID and a `DiagnosticDescriptor` in a shared descriptors file.
2. Add a matching `CodeFixProvider` under `src/Aprbrown.Analyzers.CodeFixes/` if the diagnostic is mechanically fixable.
3. Add tests under `tests/` covering both the triggering case and at least one near-miss that must *not* report.
4. Specify the rule in [`docs/spec.md`](docs/spec.md) §3 — ID, category, default severity, rationale, what it must *not* flag, and the required test cases — and add it to `AnalyzerReleases.Unshipped.md`.
5. Add it to the shipped configuration by ID. A rule absent from that allowlist is off, however the analyzer is written.

## Repository tooling

`.agents/skills/` and `.claude/skills/` hold shared agent skill definitions (the `.claude` entries are symlinks into `.agents`), pinned by `skills-lock.json`. They are development tooling and are not part of the shipped package.

## Licence

[MIT](LICENSE) — `Copyright (c) 2026 Andrew P R Brown`.

The package declares this as an SPDX expression
(`<PackageLicenseExpression>MIT</PackageLicenseExpression>`) rather than shipping a licence file,
so `LICENSE` above is not packed into the `.nupkg`.
