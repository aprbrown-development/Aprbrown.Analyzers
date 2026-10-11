# Aprbrown.Analyzers

A Roslyn analyzer package that carries one .NET coding standard to every repository in a small
personal fleet. The package ships three custom analyzers and — more importantly — the configuration
that decides which of the 150 rules from several analyzer vendors are switched on.

## Language

**House ruleset**:
The single, universal set of rules this package enforces, spanning custom, third-party and SDK
analyzers. There is exactly one; it is not parameterised per project.
_Avoid_: profile, preset, style guide, standard

**Shipped config**:
The global AnalyzerConfig file packed inside the package (`Aprbrown.Analyzers.globalconfig`) that
sets a severity for every rule in the house ruleset.
_Avoid_: globalconfig, editorconfig, ruleset file, .ruleset

**Expected ruleset**:
The independent, checked-in record of every setting the shipped config must contain, against which
the shipped config is compared exactly, as a consumer receives it. Deliberately a second copy: a change to the house ruleset is
made in both, and any line where they disagree is named.
_Avoid_: oracle, golden file, snapshot, baseline

**Sealed allowlist**:
The shape of the shipped config: a blanket `none` followed by an explicit enumeration of every rule
that is on. Sealed because a rule not named in it is off, including rules that do not exist yet.
_Avoid_: whitelist, opt-in list

**The blanket**:
The single `dotnet_analyzer_diagnostic.severity = none` line that opens the shipped config and
switches off every analyzer diagnostic before any rule is enumerated back on.
_Avoid_: catch-all, default off, global none

**Universality test**:
The admission test a rule must pass to enter the house ruleset: it is excluded only if its rationale
is *false* in some kind of .NET project. A rule that is merely silent elsewhere is admitted.
_Avoid_: project-independence test, generality test

**Deviation**:
A rule severity a consuming repository overrides in its own local `.editorconfig`, which outranks
the shipped config. The supported escape hatch from the house ruleset.
_Avoid_: override, exception, suppression, opt-out

**Carve-out**:
A deviation scoped to a path glob, such as relaxing a rule under `tests/`. Distinct from a plain
deviation because path scoping *cannot* travel in a global config and is therefore always local.
_Avoid_: exclusion, path exception, ignore rule

**The fleet**:
The six consuming repositories this ruleset is built for — `Mixologist`, `WordleHelper`,
`EveEsiClient`, `FPL_Helper/web`, `WeddingSite`, `EveIndustryTools`.
_Avoid_: the repos, consumers, downstream projects

**Onboarding**:
The one-off work of adopting the package in a consuming repository: adding the package references,
adding the carve-out template, and fixing every violation in one pass.
_Avoid_: migration, rollout, adoption ramp

**Detection fix**:
A change that makes an existing rule catch a case it previously missed. The rule table is unchanged,
so it is invisible to release tracking, yet it can turn a consumer's green build red.
_Avoid_: bug fix, improvement

**Pack-consume smoke test**:
The gate that proves an already-built package delivers the house ruleset intact to a consumer. It
inspects the package it is handed and never makes its own.
_Avoid_: package verification, smoke script, pack test

**Consumer fixture**:
The stand-in consuming project the pack-consume smoke test restores against the package. Every
diagnostic it raises is declared in its own source, by expectation markers.
_Avoid_: test project, sample consumer, fixture app

**Expectation marker**:
A comment on a consumer fixture line naming a diagnostic, at its shipped severity, that the line must
raise. A line or file without one must raise nothing, so silence is asserted as exactly as firing.
_Avoid_: annotation, expect comment, assertion

**Descriptor contract test**:
The test that holds every rule the package defines to what a consumer is told about it: its help
link lands on a heading of the consumer documentation, and its default severity is the one the
shipped config sets. A rule added later is covered without the test being edited.
_Avoid_: descriptor test, metadata test, help-link check

**Diagnostic contract**:
What a rule's diagnostic promises its code fix: where it is reported and the conclusion it carries,
so the fix acts on the analyzer's finding rather than re-deriving it.
_Avoid_: property bag, payload, diagnostic properties
