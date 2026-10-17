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

## 1.1.0

The prescribed `Meziantou.Analyzer` moves from `3.0.123` to `3.0.305`, three rules that version adds
are switched on, and a performance tier of 53 rules joins the ruleset, which is now 206 rules.
**Bump your own `Meziantou.Analyzer` reference to `3.0.305`** alongside this package: the package
takes no dependency on it, so nothing moves your pin for you — see
[the consumer docs](docs/consuming.md#1-reference-three-packages).

### Fails a green build — by design

- **`MA0220`, `MA0240` and `MA0241` are on, at warning.** They are the three rules `3.0.305` adds
  that are enabled by default at a build-gating severity, the criterion the rest of the Meziantou
  tier was chosen by, and the tier is now 106 rules. None of them fires until your repository
  configures something: `MA0220` reports a regular-expression option for `MA0003`, `MA0048` or
  `MA0104` that does not parse, and so would be silently ignored; `MA0240` reports the syntax a
  `BannedSyntaxes.txt` bans; `MA0241` reports a line of that file that cannot be applied. On an
  older `Meziantou.Analyzer` that lacks them they are inert. Of the other 26 rules `3.0.305` adds,
  `MA0227` and `MA0242` join the performance tier below and the remaining 24 stay off.
- **A performance tier: 53 rules at warning, most of them `Info` until now.** 23 are
  `Meziantou.Analyzer` rules and 30 are the SDK's `CA` rules, chosen from each vendor's Performance
  category because the rewrite they ask for keeps behaviour and costs less: a value factory for
  `ConcurrentDictionary.GetOrAdd` (`MA0242`), `Contains('#')` over `Contains("#")` (`CA1847`),
  `TryGetValue` over `ContainsKey` plus the indexer (`CA1854`), sealing internal types (`CA1852`),
  and so on. Expect hits in LINQ, string and dictionary code. 50 of the 53 ship a code fix that
  `dotnet format analyzers` applies, once the severities are in your `.editorconfig` for the run —
  see [the consumer docs](docs/consuming.md#3-build-and-fix-what-it-reports). Rules whose rewrite
  changes behaviour, such as `CA1866`'s ordinal rewrite of a culture-sensitive `StartsWith`, are
  left off. Where a `Meziantou.Analyzer` rule and a `CA` rule cover the same pattern only one is on,
  so nothing is reported twice. `MA0227` and `MA0242` arrive with `3.0.305` and are inert on an
  older pin.

### Fails a green build — silently

- **Bumping `Meziantou.Analyzer` to `3.0.305` makes rules already on catch more.** This happens when
  you move your own pin, not when you upgrade this package, but it is the change the new prescribed
  version asks of you. The widest are `MA0060`, which grew from `Stream.Read` alone to any ignored
  return value of a pure method — `string.Trim`, `Replace` and friends, the immutable collections,
  a `TryParse`-shaped method, anything marked `[Pure]` — and `MA0002`, which now reports collection
  expressions, `ToImmutableDictionary` and `ImmutableDictionary.CreateBuilder` without a comparer.
  Smaller ones: `MA0051` counts the expression bodies of properties, indexers and operators;
  `MA0070` covers every declaration that can be obsolete; `MA0072` and `MA0086` cover throw
  expressions; `MA0082` covers relational and generic-math `NaN` comparisons; `MA0124`–`MA0126`
  read `[LoggerMessage(Message = …)]`; and `MA0054`, `MA0068`, `MA0130`, `MA0147` and `MA0153` each
  reach a case they missed. No rule this package enables was removed, renamed, or had its default
  severity changed.

### Cannot fail a green build

- **Many false positives removed upstream** in rules this package enables, among them `MA0002`,
  `MA0005`, `MA0011`, `MA0046`, `MA0056`, `MA0065`, `MA0066`, `MA0091`, `MA0100`, `MA0139`,
  `MA0150`, `MA0151`, `MA0158` and `MA0163`. These also arrive with your own `3.0.305` pin.

## 1.0.1

One false positive removed from `APB0002`; the rest is documentation. No configuration entry and
no MSBuild property changed, and no rule reports anything it did not report on `1.0.0`.

### Fails a green build — by design

_Nothing. No rule was added and no severity was raised._

### Fails a green build — silently

_Nothing. The one analyzer change only stops `APB0002` reporting, so nothing catches a case it
missed on `1.0.0`._

### Cannot fail a green build

- **`APB0002` no longer reports an override or implementation that keeps a default its base or
  interface member already declares.** Overriding `Stream.ReadAsync`, or implementing
  `IHealthCheck.CheckHealthAsync`, left no edit that satisfied both `APB0002` and `MA0061`: keep
  `= default` and `APB0002` fired, drop it and `MA0061` fired. A call site binds to the declared
  type, so the base's default applies whatever the override says, and the base is where the
  diagnostic belongs. An override or implementation that *introduces* a default its member lacks
  is still reported. A carve-out taken for this can be dropped after upgrading — see
  [the consumer docs](docs/consuming.md#the-three-custom-rules).

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
- **The consumer docs (the package README) no longer claim `IDE0040` costs nothing to adopt.** They
  said `IDE0040` measured zero violations across the six repositories the ruleset was derived from;
  against live builds it scored 0, 20 and 259 on the three repositories onboarded so far. Every hit is an interface member
  declared without an explicit `public`, which `dotnet_style_require_accessibility_modifiers =
  always` deliberately catches, so its cost scales with how many hand-written interfaces you have.
  The fix is mechanical: `dotnet format style --diagnostics IDE0040`. The zero-violation claims for
  `_camelCase` private fields and `PascalCase` private statics are kept, since they held on
  every repository measured — see [the consumer docs](docs/consuming.md#3-build-and-fix-what-it-reports). The rule and its
  option are unchanged.
- **The consumer docs (the package README) now warn that the StyleCop ordering tier has no code
  fix.** `StyleCop.Analyzers` exports no fix for `SA1201`–`SA1204`, so
  `dotnet format analyzers --diagnostics SA1201 SA1202 SA1203 SA1204` answers `Unable to fix
  SA120x. No associated code fix found.` for each, and every violation is reordered by hand: one
  repository onboarded so far had 149 in production code. The docs now say so before onboarding,
  with a command to count the violations first — see
  [the consumer docs](docs/consuming.md#3-build-and-fix-what-it-reports). The tier is unchanged.

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
