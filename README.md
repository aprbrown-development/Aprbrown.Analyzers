# Aprbrown.Analyzers

Custom [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/) analyzers and code fixes for .NET, packaged for consumption as a NuGet analyzer package.

## Status

Early — scaffolding only. The repository currently contains agent-skill configuration and no analyzer code yet. The sections below describe the intended shape of the project, not what is implemented.

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
| `src/Aprbrown.Analyzers.CodeFixes/` | Matching `CodeFixProvider`s |
| `src/Aprbrown.Analyzers.Package/` | NuGet packaging project that ships the analyzer + code fix assemblies |
| `tests/Aprbrown.Analyzers.Tests/` | Unit tests using `Microsoft.CodeAnalysis.Testing` verifiers |

Analyzers target `netstandard2.0` so they load in every supported host (MSBuild, VS, Rider, `dotnet build`). Test and packaging projects can target current .NET.

## Building

```bash
dotnet restore
dotnet build
dotnet test
```

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
