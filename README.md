# Aprbrown.Analyzers

Custom [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/) analyzers and code fixes for .NET, packaged for consumption as a NuGet analyzer package.

## Status

Early — scaffolding only. The repository currently contains agent-skill configuration and no analyzer code yet. The sections below describe the intended shape of the project, not what is implemented.

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
4. Document the rule — ID, category, default severity, and rationale — in `docs/rules/`.

## Repository tooling

`.agents/skills/` and `.claude/skills/` hold shared agent skill definitions (the `.claude` entries are symlinks into `.agents`), pinned by `skills-lock.json`. They are development tooling and are not part of the shipped package.

## Licence

[MIT](LICENSE) — `Copyright (c) 2026 Andrew P R Brown`.

The package declares this as an SPDX expression
(`<PackageLicenseExpression>MIT</PackageLicenseExpression>`) rather than shipping a licence file,
so `LICENSE` above is not packed into the `.nupkg`.
