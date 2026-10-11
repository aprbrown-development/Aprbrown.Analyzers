# Changelog

Every entry answers one question: **can this version fail a build that was green on the previous
one?**

That is a narrower job than a changelog usually has, and a deliberate one.
[`AnalyzerReleases.Shipped.md`](src/Aprbrown.Analyzers/AnalyzerReleases.Shipped.md) already records
rule *table* changes and is machine-enforced, so it cannot rot — but it covers only the `APB` rules
this package defines, and it is structurally blind to the most dangerous class of release there is.
A **detection fix**, where an existing rule starts catching a case it previously missed, changes no
rule table at all. Under `TreatWarningsAsErrors` — which this package switches on by default — it is
the release most likely to turn a consumer's green build red, and nothing but this file will say so.

Each section is therefore grouped by build impact rather than by component, following
[ADR-0004](docs/adr/0004-diagnostic-ids-are-public-api.md):

| Change | Bump | Can it fail a green build? |
|---|---|---|
| New rule, or severity raised | minor | **Yes**, by design — opted into by upgrading |
| Existing rule catches more cases (detection fix) | patch | **Yes**, silently — same rule, more hits |
| Rule removed or ID renamed | major | No, but it silently voids config a consumer owns |
| Fixer added, false positive removed, docs | patch | No |

Notes are authored, never generated from commit messages: commits describe what the implementer did,
and no commit convention reliably encodes "this now catches more".
[`release.yml`](.github/workflows/release.yml) cuts each GitHub Release body from the section
matching its tag, so there is exactly one authored source and the release page cannot disagree with
this file. A prerelease tag reads the section of the version it prefixes — `v1.0.0-preview.1` is a
rehearsal for `1.0.0` and carries that version's build impact, because it has none of its own.

## 1.0.1

Documentation only. No analyzer's behaviour, no configuration entry and no MSBuild property
changed, so every answer below is the same one.

### Fails a green build — by design

_Nothing. No rule was added and no severity was raised._

### Fails a green build — silently

_Nothing. No analyzer changed, so nothing catches a case it missed on `1.0.0`._

### Cannot fail a green build

- **The README records that ReSharper offers to introduce the primary constructor `APB0001`
  forbids.** The conversion is offered on a type the build has just rejected, which reads like a
  misconfigured ruleset and is not one: the offer comes from ReSharper's engine rather than Roslyn's
  `IDE0290`, which is not named in the sealed allowlist, and no file this package ships reaches it.
  Switching the offer off is an entry the consuming repository makes in its own editor
  configuration — see
  [the consumer docs](docs/consuming.md#resharper-offers-the-primary-constructor-apb0001-forbids).
  The shipped config is unchanged.
- **`APB0002`'s description no longer names `CA2016` and `MA0040` as its call-site companions.**
  Both default to `Info`, so neither is in the shipped config. `MA0032` alone polices the call site.
  The corrected text is what an IDE shows when you expand the diagnostic, and the same correction
  is made in [the consumer docs](docs/consuming.md#the-three-custom-rules).
- **The README no longer claims `IDE0040` costs nothing to adopt.** It said `IDE0040` measured zero
  violations across the six repositories the ruleset was derived from; against live builds it
  scored 0, 20 and 259 on the first three repositories onboarded. Every hit is an interface member
  declared without an explicit `public`, which `dotnet_style_require_accessibility_modifiers =
  always` deliberately catches, so its cost scales with how many hand-written interfaces you have.
  The fix is mechanical: `dotnet format style --diagnostics IDE0040`. The zero-violation claims for
  `_camelCase` private fields and `PascalCase` private statics are kept, since they held everywhere
  — see [the consumer docs](docs/consuming.md#3-build-and-fix-what-it-reports). The rule and its
  option are unchanged.

## 1.0.0

The first release, so the question above has no previous version to ask it against. What follows
takes its place: everything a repository adopting this package is signing up to in one pass
([onboarding](docs/consuming.md) is a single sweep of fixes, not a ramp). Later entries will be a
few lines.

### Fails a green build — by design

- **The house ruleset switches on 150 rules** and switches off everything else. The shipped config
  opens with a blanket `dotnet_analyzer_diagnostic.severity = none` and enumerates every rule that
  is on, by ID: 3 `APB`, 103 `Meziantou.Analyzer`, 29 SDK `CA`, 4 StyleCop ordering (`SA1201`–
  `SA1204`), and 11 IDE naming, qualification and style rules. A rule not named is off, including
  rules that do not exist yet — so a future upstream release cannot change what a consumer's build
  enforces without a version of this package moving.
- **`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` default to `true`.** Both are defaults,
  not mandates: a consuming repository that sets either property itself wins. But a repository that
  sets neither gets a build that fails on every rule above, which for most of the fleet is the
  change with the largest immediate cost.
- **`APB0001` — do not use primary constructors on classes or structs.** Warning, category Design.
  Ships without a code fix; adding one later is a minor bump, not a breaking change.
- **`APB0002` — do not give a `CancellationToken` parameter a default value.** Warning, category
  Design.
- **`APB0003` — parameter names should match the implemented interface member.** Warning, category
  Naming.
- **`MA0032` is on although Meziantou ships it off**, at warning: a `CancellationToken` must be
  passed where one is accepted.

Two rules are deliberately **not** enumerated and stay off, which is worth stating because both are
ones a consumer might expect: `MA0004` (`ConfigureAwait`) is the sole universality-test exclusion,
and `CA1707` would ban the underscores the house `_camelCase` convention requires.

### Fails a green build — silently

_Nothing. There is no previous version whose detection behaviour could have changed._

### Cannot fail a green build

- **A code fix for `APB0003`**, which renames the symbol rather than the token, so every reference
  follows.
- **The seven expression-bodied-member preferences ship at `suggestion`**, deliberately. Six
  repositories agree on shape without a disagreement failing anyone's build.
- **`IDE0003` does not fire in a command-line build.** The `this.`-removal half of the qualification
  convention is IDE-only in Roslyn; `IDE0009`, the add half, does gate. This is a known limitation
  rather than a choice, and it is pinned by an assertion that is expected to break the day it is
  fixed upstream.
- **The package carries its own README**, so the nuget.org page is the install instructions. No
  symbols and no `.pdb` are shipped in this version.
