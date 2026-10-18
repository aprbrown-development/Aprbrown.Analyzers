# ADR-0007: Pin Roslyn to 5.0.0, and raising the toolchain floor is a major bump

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

**Roslyn is pinned to `5.0.0`**, the Roslyn of the first .NET 10 SDK, in the analyzer, code-fix and
test projects alike. The assemblies stay `netstandard2.0`.

The floor this implies, verified against the Roslyn version each host ships rather than NuGet
publish dates:

| Host | Minimum | Source |
|---|---|---|
| .NET SDK | **10.0.100** (the `1xx` feature band) | `dotnet/sdk` at `v10.0.100` takes `Microsoft.Net.Compilers.Toolset` `5.0.0-2.25515.111`; every 9.0 band takes 4.x |
| Visual Studio | **2026 version 18.0** | `dotnet/roslyn` at tag `Visual-Studio-2026-Version-18.0` is version 5.0, and its package versioning notes give Roslyn 5.0 as Visual Studio 2026 18.0 and .NET 10 |
| Rider | Not determined | No primary source maps Rider releases to a Roslyn version. A Rider build that compiles through a .NET SDK meets the floor when that SDK does |

That floor is every .NET 10 SDK and every Visual Studio 2026. The `1xx` band is supported for the
life of the .NET 10 runtime and is the band distribution packages such as Arch's ship, so a higher
floor would lock out a supported toolchain for no gain.

`5.9.0`, the latest stable release and the Roslyn of SDK 10.0.400 and Visual Studio 18.9, was
considered and rejected: no rule needs an API newer than 5.0, so it would meet neither of the
reasons below for moving the pin.

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
- Consumers on a .NET 9 or older SDK, or Visual Studio 2022, must move to a .NET 10 SDK or Visual
  Studio 2026 before taking `2.0.0`. Onboarded repositories already on SDK 10.0.112 need nothing.
- CI installs SDK `10.0.x`, which resolves to the newest band and so hosts a Roslyn at or above the
  floor. It does not exercise the floor itself; if that ever matters, the remedy is a `global.json`
  `sdk` section, as the workflows already set out.
- The next floor raise is expected when .NET 10's toolchains leave support, or earlier only if a
  rule needs a newer API.
