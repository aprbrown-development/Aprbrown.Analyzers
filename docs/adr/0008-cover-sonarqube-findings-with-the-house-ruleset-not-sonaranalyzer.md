# ADR-0008: Cover SonarQube findings with the house ruleset, not SonarAnalyzer.CSharp

- **Status:** Accepted
- **Date:** 2026-10-18
- **Builds on:** [ADR-0002](0002-sealed-allowlist-ruleset-architecture.md),
  [ADR-0006](0006-sonarqube-analysis-from-a-contained-repo-scoped-homelab-runner.md)

## Context

Most of the fleet now publishes SonarQube analyses, and SonarQube reports issues on code that every
build passed. The goal is to shift left: a SonarQube issue on a green build is a gap in the house
ruleset, so the build should fail first.

The obvious move is to add `SonarAnalyzer.CSharp` to the house ruleset the way ADR-0002 adds
Meziantou: each consumer pins it, and the shipped config enumerates `Sxxxx` IDs. Three facts, measured
against the homelab's Community Build and the scanner's source, stand against it:

- **Under the scanner, the quality profile decides, not the shipped config.** SonarScanner for .NET
  removes any `SonarAnalyzer*` analyzer the project references and runs the server's own C# plugin.
  It passes a generated ruleset naming every Sonar rule, and Roslyn applies a ruleset ahead of any
  global config, so neither the blanket nor `dotnet_diagnostic.Sxxxx.severity = none` changes what
  SonarQube reports. Adopting the package would make a second copy of the `Sxxxx` list, a custom
  quality profile, mandatory and hand-synchronised.
- **The licence.** Since 10.3 the package is under the SONAR Source-Available License v1.0, which
  excludes from its permitted purposes using AI technology outside the program "to ingest,
  interpret, analyze … or interact with the data provided by the Program". Agents read this fleet's
  build output. The last LGPL release, 9.32, is too old to pin.
- **Build cost.** No benchmark is published; community reports range from twice to eighteen times
  the build time.

Measuring the fleet's open issues also showed the gap is small. Of about 317 C# issues, 189 were
`IDE0021`/`IDE0022`, house rules that consumers on 1.0.0 still received at `suggestion`. The rest
fell across 17 native Sonar rules, and seven of those already have an equivalent in the analyzers
the fleet pins.

## Decision

1. **The package does not configure or depend on `SonarAnalyzer.CSharp`.** SonarQube's own analysis
   stays the independent signal ADR-0006 describes.
2. **Every Sonar rule that fires in the fleet gets a Sonar verdict**, decided by the universality
   test: *covered* by a house rule (an existing equivalent switched on, or a new `APB` rule),
   *signal* (agreed with, left to SonarQube), or *rejected*. The verdicts are kept in
   [`spec.md` section 2.5](../spec.md#25-sonarqube-verdicts).
3. **A homelab "House ruleset" quality profile** extends Sonar way comprehensive and deactivates the
   rejected rules only. Covered rules stay active: a finding under one is a detection gap in its
   covering rule, not a duplicate, because the build already fails wherever that rule fires.
4. **A newly firing Sonar rule is triaged** to a verdict when it appears, found by comparing the
   rules SonarQube reports against the verdict table.

The first triage covers the 17 rules. Five equivalents are switched on: `CA1849`, `CA1052`,
`IDE0004`, `IDE0051` and `IDE0052`. `CA5382` and `CA5383` looked like a sixth and seventh, for
`S2092`, but never fire on ASP.NET Core 3.0 or later, so `S2092` is a signal rule. One rule is added, `APB0006`, for nested
conditional expressions. Equivalents that default to Info, such as `MA0042`, were passed over for
ones that default to Warning, keeping the sweeps' rule that Info and Hidden vendor rules are not
house rules.

## Considered options

- **Adopt `SonarAnalyzer.CSharp`.** Direct parity with SonarQube, at the cost of the licence clause,
  a second ruleset kept in step by hand, and build time.
- **Tune only the quality profile.** A quieter dashboard with no change to what a build enforces.
- **Enable `MA0240` with a shipped `BannedSyntaxes.txt` for nested conditionals.** The package would
  have to inject the file into consumers that may write their own, and the ban would have no help
  link or descriptor contract.

## Consequences

- SonarQube keeps reporting signal rules on green builds; those issues are expected, not gaps.
- The quality profile is homelab state, kept as a backup in the `Containers` repository, because
  consumers outside the homelab have no SonarQube.
- Switching a new equivalent on is a minor release under ADR-0004 and can fail a consumer's green
  build; `CA1849` alone accounts for about 54 fixes across the fleet.
