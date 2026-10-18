# ADR-0007: Pin Roslyn to 5.9.0, and raising the toolchain floor is a major bump

- **Status:** Accepted
- **Date:** 2026-10-18
- **Ticket:** [Decide whether to raise the Roslyn version the analyzers build against](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/76)
- **Extends:** [ADR-0004](0004-diagnostic-ids-are-public-api.md) (its versioning table)

## Context

The analyzer and code-fix assemblies compile against `Microsoft.CodeAnalysis.CSharp` and
`Microsoft.CodeAnalysis.CSharp.Workspaces`. An analyzer built against a given Roslyn loads in that
Roslyn or any newer one, never an older one: a host older than the reference refuses the assembly
with `CS9057`, and under the `TreatWarningsAsErrors` this package switches on that is a failed
build. The version the analyzers build against is therefore the **toolchain floor** every consumer
must meet.

That pin had been `4.14.0` since the repository skeleton, set low so the analyzers loaded in .NET SDK
9.0.300+ and Visual Studio 2022 17.14+. No ADR recorded it, and nothing said when it should move.

.NET 8 and .NET 9 both reach end of support in November 2026, and with them the toolchains that 4.14
was held low for. No rule or code fix needs an API newer than 4.14; the question is only which
toolchains the floor is worth protecting.

## Decision

**Roslyn is pinned to `5.9.0`**, the latest stable release, in the analyzer, code-fix and test
projects alike. The assemblies stay `netstandard2.0`.

The floor this implies, verified against the Roslyn version each host ships rather than NuGet
publish dates:

| Host | Minimum | Source |
|---|---|---|
| .NET SDK | **10.0.400** (the `4xx` feature band) | `dotnet/sdk` at `v10.0.400` takes `Microsoft.Net.Compilers.Toolset` `5.9.0-1.26374.102`; `v10.0.300` takes `5.6.0` |
| Visual Studio | **2026 version 18.9** | Microsoft's [SDK, MSBuild and Visual Studio versioning](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs) page ships 10.0.4xx with Visual Studio 18.9. `dotnet/roslyn` has no 18.9 tag yet to confirm it directly; its tags from 18.0 to 18.8 Preview 1 each carry Roslyn 5.*N* in 18.*N*, consistent with that pairing |
| Rider | Not determined | No primary source maps Rider releases to a Roslyn version. A Rider build that compiles through a .NET SDK meets the floor when that SDK does |

The `5.0.0` floor of SDK 10.0.100 was considered and rejected knowingly. It would keep every .NET 10
band loading, including `1xx`, which stays in support for the life of the runtime. `5.9.0` locks
that band out, along with the `2xx` and `3xx` bands that are already out of support, in exchange
for a floor that the remaining supported band, `4xx`, also carries for the life of the runtime.

**The pin moves for only two reasons:** to retire toolchains that have gone out of support, or to
gain a Roslyn API a rule or fixer needs. Moving it for neither is churn that costs consumers an
upgrade.

**Raising the toolchain floor is a major bump.** This extends ADR-0004's versioning table with one
row:

| change | bump | why |
|---|---|---|
| Toolchain floor raised | **major** | Every consumer on an older SDK or IDE fails to build, whatever their configuration. |

ADR-0004 classifies changes by what they do to a consumer's diagnostics; a raised floor does
something worse, failing the build before any rule runs, so it ranks with the change ADR-0004
already calls major.

## Consequences

- This change ships as `2.0.0`.
- Consumers on SDK 10.0.1xx through 10.0.3xx, or Visual Studio 2026 before 18.9, must update their
  toolchain before taking `2.0.0`. Onboarded repositories building on SDK 10.0.112 (Roslyn 5.0) are
  among them.
- CI installs SDK `10.0.x`, which resolves to the newest band, so the pack-and-consume smoke test
  exercises the new floor without a `global.json` pin. If it ever resolved to an older band, the
  remedy is a `global.json` `sdk` section, as the workflows already set out.
- The next floor raise is expected when .NET 10's toolchains leave support, or earlier only if a
  rule needs a newer API.
