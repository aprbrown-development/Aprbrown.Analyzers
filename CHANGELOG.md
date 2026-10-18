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
| Toolchain floor raised ([ADR-0007](docs/adr/0007-pin-roslyn-to-5-0-and-treat-a-raised-floor-as-major.md)) | major | **Yes**, on every older SDK or IDE — before any rule runs |
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

## 2.1.0

Six rules join the ruleset, each the build-time counterpart of a SonarQube rule already reporting
in the fleet: `CA1849`, `CA1052`, `IDE0004`, `IDE0051`, `IDE0052` and a new custom rule, `APB0006`,
which reports a nested conditional expression. The ruleset is now 215 rules, 210 of them warnings
and five errors.

### Fails a green build — by design

- **`CA1849` reports a blocking call in an async method that has an awaitable counterpart**, such
  as `stream.Flush()` where `await stream.FlushAsync(cancellationToken)` would do. It matches
  SonarQube's `S6966`, which had 54 open across the fleet, so expect it to be the largest of the
  five. See [the rules](docs/consuming.md#the-rules).
- **`CA1052` asks a type holding only static members to be `static`**, at every accessibility,
  since `dotnet_code_quality.CA1052.api_surface = all` reaches the internal holders the default
  would skip. It matches `S1118` (5 open), but where `S1118` accepts a private constructor,
  `CA1052` wants the class `static`.
- **`IDE0004`, `IDE0051` and `IDE0052` are raised from `Hidden` to warning**: a redundant cast, an
  unused private member, and a private member written but never read. They match `S1905` (2 open)
  and `S1144` (5 open). `dotnet format style` fixes the first two once their severities are in your
  `.editorconfig` for the run; `IDE0052` has no code fix.
- **`APB0006` is a new rule, on at warning: conditional expressions are not nested.** A
  conditional inside another's condition or branches is reported, once per inner conditional, so
  `a ? 1 : b ? 2 : c ? 3 : 4` reports twice. A conditional inside a lambda, anonymous method or
  local function is in a scope of its own and is not nested; `?.`, `??`, `??=` and switch
  expressions are not conditionals. It matches SonarQube's `S3358` (18 open across the fleet).
  There is no code fix: rewrite each as an `if` statement or a switch expression. See
  [the custom rules](docs/consuming.md#the-custom-rules).

### Fails a green build — silently

_Nothing. No existing rule reports anything it did not report on `2.0.0`._

### Cannot fail a green build

- **The install guide documents the five rules** under [the rules](docs/consuming.md#the-rules),
  and its tier table and totals count them.
- **The custom rules' heading no longer counts them.** "The three custom rules" in the install guide
  listed five; it is now [the custom rules](docs/consuming.md#the-custom-rules), and every rule's
  help link points there. A help link from an earlier version now opens the guide at its top.

## 2.0.0

The analyzers are now built against Roslyn 5.0 instead of 4.14, so the minimum toolchain rises to
**.NET SDK 10.0.100** or **Visual Studio 2026 18.0**. No rule, severity or code fix changed.

### Fails a green build — by design

- **The minimum toolchain is now .NET SDK 10.0.100 or Visual Studio 2026 18.0**, anything hosting
  Roslyn 5.0 or newer: every .NET 10 SDK, but no .NET 9 SDK and no Visual Studio 2022. On an older
  SDK or IDE the host cannot load the analyzers and reports `CS9057`, which `TreatWarningsAsErrors`
  turns into a failed build. Update the toolchain (on CI too, and in any `global.json` `sdk` pin)
  before taking this version. See [the requirements](docs/consuming.md#requirements).

### Fails a green build — silently

_Nothing. No rule reports anything it did not report on `1.1.1`._

### Cannot fail a green build

- **The install guide names the new minimum toolchain.** Its
  [requirements](docs/consuming.md#requirements) give .NET SDK 10.0.100 or Visual Studio 2026 18.0
  in place of `1.1.1`'s .NET SDK 9.0.300 or Visual Studio 2022 17.14, and name `CS9057` as the error
  an older host reports.

## 1.1.1

A correction to the package's description; no configuration entry, MSBuild property or analyzer
changed, and no rule reports anything it did not report on `1.1.0`.

### Fails a green build — by design

_Nothing. No rule was added and no severity was raised._

### Fails a green build — silently

_Nothing. No analyzer changed._

### Cannot fail a green build

- **The package description no longer miscounts the custom analyzers.** `1.1.0`'s description, shown
  on nuget.org and in package-manager UIs, said the package ships four custom analyzers when it
  ships five. It now names no number, so adding a rule cannot make it stale again. See
  [the custom rules](docs/consuming.md#the-three-custom-rules) for the full set.

## 1.1.0

The seven expression-bodied-member preferences now gate the build instead of only suggesting. The
prescribed `Meziantou.Analyzer` moves from `3.0.123` to `3.0.305`, three rules that version adds
are switched on, a performance tier of 53 rules joins the ruleset, and two custom rules arrive:
`APB0004` puts a wrapped expression body's `=>` at the end of the declaration line, and `APB0005`
moves trailing comments onto their own line. `CA1725` now holds every override to its base
declaration's parameter names, and a suppressor, `APBS0001`, leaves interface implementations to
`APB0003`. The ruleset is now 209 rules, 204 of them warnings and five errors.
**Bump your own `Meziantou.Analyzer` reference to `3.0.305`** alongside this package: the package
takes no dependency on it, so nothing moves your pin for you — see
[the consumer docs](docs/consuming.md#1-reference-three-packages).

### Fails a green build — by design

- **The seven expression-bodied-member preferences are raised from `suggestion` to warning.**
  `IDE0021`, `IDE0022`, `IDE0025`, `IDE0026`, `IDE0027`, `IDE0053` and `IDE0061` shipped at
  `suggestion` on `1.0.x`, which never reaches a build log but which SonarQube imports, so a quality
  gate could fail on code every build had passed. At warning the build and Sonar agree. The
  `csharp_style_expression_bodied_*` options are unchanged, but `methods = true` means a method
  whose body is one statement spread over several lines is reported too, so expect hits wherever a
  block body holds a single statement. `dotnet format style` fixes every case once the seven
  severities are in your `.editorconfig` for the run; run it before `APB0004`'s and `APB0005`'s
  fixes. If your own `.editorconfig` sets a `csharp_style_expression_bodied_*` option with an inline
  severity such as `when_on_single_line:suggestion`, delete the line, since it overrides the house
  setting. See [the rules](docs/consuming.md#the-rules).
- **`APB0004` is a new rule, on at warning: a wrapped expression body's `=>` ends the declaration
  line.** `int M() =>` with the expression on the next line passes; `int M()` with `=> expression`
  on the next line is reported, on the arrow. It covers methods, constructors, finalizers,
  operators, properties, indexers, accessors and local functions, and leaves lambdas and
  switch-expression arms alone. Under `TreatWarningsAsErrors` every arrow-first member fails the
  build until it is moved. Its code fix supports fix-all, so one "Fix all occurrences in Solution"
  clears the lot; the exception is an arrow with a preprocessor directive between it and the
  declaration, which is reported without a fix and moved by hand. See
  [the custom rules](docs/consuming.md#the-three-custom-rules).
- **`APB0005` is a new rule, on at warning: a comment goes on its own line above the code it
  describes.** Every `//` comment trailing code is reported, so expect hits wherever a statement,
  field or list item carries a note at the end of its line. Block comments, documentation, comments
  on preprocessor directive lines and tool directives (`NOSONAR`, `ReSharper `, `lang=`,
  `language=`) are exempt. One fix-all pass of its code fix clears it, in the IDE or with
  `dotnet format analyzers --diagnostics APB0005` once the severity is in your `.editorconfig` for
  the run; the exceptions are a comment after the closing line of a multi-line string and one on a
  line opened by a block comment, which are reported without a fix and moved by hand. Run it after
  `APB0004`'s fix, which moves `=>` up to the end of the line above and with it any comment on that
  line. See [the custom rules](docs/consuming.md#the-three-custom-rules).
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
- **`CA1725` is on, at warning: an override keeps its base declaration's parameter names.** It
  checks every override, internal types included, because the configuration sets
  `dotnet_code_quality.CA1725.api_surface = all`. Expect hits on framework overrides written with
  your own names: `Equals(object? other)` wants `obj`, and `Stream.Read` wants `buffer`, `offset`
  and `count`. Its code fix renames the parameter and every use of it;
  `dotnet format analyzers --diagnostics CA1725` applies it once the severity is in your
  `.editorconfig` for the run. Interface implementations stay `APB0003`'s, see below. See
  [the rules](docs/consuming.md#the-rules).

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
- **A diagnostic suppressor, `APBS0001`, hides `CA1725` on interface implementations.** `CA1725`
  reports an implementation that renames the interface's parameter, and so does `APB0003`; the
  suppressor leaves that to `APB0003` alone, which is the stricter of the two. It only ever removes
  a `CA1725`, and an override that renames its base's parameter keeps its `CA1725` even when it
  also implements an interface. Its ID is public API: name it in `NoWarn` to switch it off, as
  [the consumer docs](docs/consuming.md#switching-the-suppressor-off) explain. `APB0003`'s
  description, shown when you expand the diagnostic, no longer says `CA1725` is blind to interface
  implementations.

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
