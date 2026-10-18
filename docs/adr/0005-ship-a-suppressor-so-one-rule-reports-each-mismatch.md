# ADR-0005: Ship a diagnostic suppressor so one rule reports each parameter-name mismatch

- **Status:** Accepted
- **Date:** 2026-10-18
- **Ticket:** [Evaluate CA1725 for override parameter names](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/82)
- **Extends:** [ADR-0002](0002-sealed-allowlist-ruleset-architecture.md) decision 1

## Context

`APB0003` checks that an interface implementation keeps the interface's parameter names. Nothing
in the house ruleset checked the same for an override, and the documentation said that was
`CA1725`'s job and that `CA1725` was blind to interface implementations.

Measured on SDK 10.0.112, it is not blind. `CA1725` reports an implicit implementation that renames
a parameter, and with `api_surface = all` an explicit one too. Where a method implements two
interfaces that name the parameter differently, it accepts a match with either, so it is looser
than `APB0003`. Enabling `CA1725` for overrides would therefore report every renamed interface
parameter twice, once per rule. Double reporting is why `SA1315` was rejected
(`.out-of-scope/stylecop-sa1315.md`).

The options were to leave overrides unchecked, to accept the duplicates, to drop `APB0003` in
favour of the looser rule (a major version under ADR-0004), or to hide `CA1725` where `APB0003`
already speaks.

## Decision

### 1. The package ships a `DiagnosticSuppressor` alongside the analyzers and code fixes

ADR-0002 decision 1 lists what the package contains: the `APB` analyzers, their code fixes and the
shipped config. It now also contains `InterfaceParameterNameSuppressor`, in the analyzer assembly.
It suppresses `CA1725`, and only `CA1725`, on a parameter of a method that implements an interface
member, implicitly or explicitly. `APB0003` is then the single report on interfaces, and `CA1725`
the single report on overrides.

A parameter whose method overrides a base member under a different name keeps its `CA1725`, even
when the method also implements an interface: the override mismatch is `CA1725`'s to report, and
`APB0003` says nothing about it.

### 2. The suppression ID is `APBS0001`, and it is public API

A consumer switches the suppressor off by naming its ID in `NoWarn`, so under ADR-0004 the ID is
public API like a diagnostic ID. The `S` keeps it out of the `APB0001`+ series a reader scans for
rules. Suppressors are not covered by release tracking, so the CHANGELOG is the record of it.

### 3. The suppressor is not enumerated in the shipped config

Measured: the blanket does not disable a suppressor. Roslyn turns one off only when its ID is
suppressed in the compilation options, which `NoWarn` reaches and an analyzer config does not. So
neither the blanket nor `dotnet_diagnostic.APBS0001.severity` has any effect on it, and naming it in
the sealed allowlist would document a switch that does nothing.

## Consequences

- `CA1725` joins the house ruleset at `warning` with `api_surface = all`, without double reporting.
- A future overlap between a custom rule and a vendor rule has a precedent: suppress the vendor rule
  where the custom one is stricter, rather than accept duplicates or drop the stricter rule.
- The pack-consume smoke test proves the suppressor works end to end: the fixture's interface
  mismatches raise `APB0003` and nothing else. With the suppressor removed, `CA1725` appears on both.
