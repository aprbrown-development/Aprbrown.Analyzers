# ADR-0002: Ship rules and configuration, not third-party assemblies, as a sealed allowlist

- **Status:** Accepted
- **Date:** 2026-07-23
- **Ticket:** [Audit the Mixologist ruleset against the universality test](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/4),
  written up in [Write the consumption spec](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/8)
- **Map:** [Map: a portable house ruleset for .NET](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/1)
- **Supersedes nothing. Depended on by:** [ADR-0003](0003-tag-triggered-trusted-publishing-with-pr-time-gates.md)

## Context

The ruleset being made portable exists today in `Mixologist` as three separable layers: three custom
Roslyn analyzers; a curated set of third-party analyzers (`Meziantou.Analyzer`,
`StyleCop.Analyzers`); and an `.editorconfig` of severities and naming conventions.

Measuring the layers rather than reading them changed the problem. `Mixologist`'s 132-line
`.editorconfig` is a **delta, not a ruleset**: roughly 130 rules gate every build, and about 15 were
ever written down. The other ~115 are enforced by inheritance from whatever each vendor happens to
default to. Making that portable means first making it *explicit*.

Two constraints framed the design. `EveIndustryTools` pins a different `Meziantou.Analyzer` version,
so bundling that dependency would force a version fight at adoption time. And a package's global
config is only a *baseline* — a consumer's local `.editorconfig` outranks it — so whatever ships must
be a default that a repository can argue with.

## Decision

### 1. The package ships the custom analyzers and the configuration, not the third-party assemblies

`Aprbrown.Analyzers` contains the three `APB` analyzers, their code fixes, and the shipped config. It
takes **no dependency** on `Meziantou.Analyzer` or `StyleCop.Analyzers`. Each consuming repository
takes its own pinned `PackageReference` for those.

This works because a global config setting a severity for an analyzer that is not installed is
**inert** — verified, including for a fabricated diagnostic ID, under `TreatWarningsAsErrors`. So the
configuration layer travels as one file regardless of which analyzer assemblies are present at the
other end.

It decouples the release train from upstream's, and sidesteps the `EveIndustryTools` version
conflict entirely. The cost is that onboarding is three package references rather than one, and that
the shipped config can reference rules a consumer has not installed.

### 2. One universal ruleset, with no configuration surface

No profiles, no severity levels, no MSBuild knobs selecting a variant. A rule is admitted by the
**universality test**: exclude it only if its rationale is *false* in some kind of .NET project.

This is a deliberate sharpening of the obvious test ("is the rationale independent of project
kind?"), which gives the wrong answer for inert framework rules. `MA0115` is a Blazor rule; in a
non-Blazor repository it is *silent*, not *wrong*. Silence is harmless and covers the day a
repository adopts that framework. Falsity is not harmless. `MA0004` (`ConfigureAwait`) is the sole
exclusion: its rationale — "ASP.NET installs no synchronization context" — is plainly false for a
library like `EveEsiClient`.

### 3. The shipped config is a sealed allowlist

The file opens with `dotnet_analyzer_diagnostic.severity = none` — the blanket — and then enumerates
every rule that is on. A rule not named is off.

The blanket covers diagnostic IDs that do not exist yet, so a rule introduced by a future
`Meziantou 3.0.130` arrives **disabled**. It can only enter the house ruleset by someone adding a
line. This is what makes the versioning policy in [ADR-0004](0004-diagnostic-ids-are-public-api.md)
real rather than aspirational: without sealing, "a new rule is a minor version bump" would be a
promise the package could not keep, because upstream could add rules to it at any time.

### 4. Every enabled rule is enumerated by its diagnostic ID

Not by category, and not by MSBuild property. This is forced by measurement, not chosen for tidiness.

**`AnalysisMode` cannot reach past the blanket.** Ticket #4 assumed the SDK's 27 default-on `CA`
rules could be restored by an `AnalysisMode` tier and left proving it to the consumption spec.
Measured on SDK `10.0.110`, it does not work at any setting:

| configuration | `CA2200` (default-on) |
|---|---|
| no config at all | fires |
| blanket only | silent |
| blanket + `AnalysisMode` = `Default` / `Recommended` / **`All`** / `Minimum` | **silent** |
| blanket + `AnalysisLevel=latest-all` | **silent** |
| blanket + `dotnet_diagnostic.CA2200.severity = warning` | fires |

An explicit severity in a config beats an enable-by-default MSBuild switch, so once the blanket is
down the only way back up is in the config itself.

**Re-enabling by category would break the seal.** It is the obvious cheaper repair and it must be
rejected: `dotnet_analyzer_diagnostic.category-Naming.severity = warning` switched on `CA1707`, which
is default-**off** and which this ruleset specifically wants off because it fights the house
`_camelCase` convention. A category re-enable admits every rule in that category — including rules
upstream adds later, which is precisely what decision 3 exists to prevent.

So enumeration by ID is not a style preference. **The enumeration *is* the seal.** It applies
identically to Meziantou's 103 rules, the SDK's 27, StyleCop's four and the IDE rules, because the
blanket is analyzer-agnostic. The shipped config is therefore a long file, and that length is the
feature.

### 5. Path-scoped carve-outs stay in the consuming repository

A global AnalyzerConfig accepts only exact absolute file paths as section headers; globs are rejected
outright and the section is dropped with a warning. A package cannot know a consumer's absolute
paths, so carve-outs such as `[tests/**.cs]` **cannot travel**. They ship as a documented template in
the onboarding snippet, not as a mechanism.

## Consequences

- The shipped config is roughly 131 enumerated lines and must be **generated once**, against pinned
  analyzer versions, then maintained by hand. Re-deriving it against a newer vendor version would
  silently admit new rules and break the seal — the derivation is a one-time act, not a build step.
- The shipped config must be a **tested artifact**. The blanket, the enumeration and global-config
  precedence interact; a config that builds clean and enforces nothing is a demonstrated failure
  mode. ADR-0003's pack-consume smoke test carries this.
- Consumers get three pinned package references, not one, and the pinned versions become part of the
  spec because the package no longer carries them.
- The package ships exactly **two** MSBuild properties (`EnforceCodeStyleInBuild`,
  `TreatWarningsAsErrors`). `AnalysisMode` was a candidate third and is dropped as inert; shipping it
  would document an intent the build does not honour.
