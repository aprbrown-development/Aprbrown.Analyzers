# Aprbrown.Analyzers — implementation spec

What the package contains, how it is built, and how it reaches a consuming repository. Written so an
implementation session can build v1 without reopening a design question.

**Read first:** [`CONTEXT.md`](../CONTEXT.md) for vocabulary, then the four ADRs — this document
states *what to build*, the ADRs state *why*, and it is not repeated here.

| ADR | Settles |
|---|---|
| [0001](adr/0001-distribution-via-public-nuget-org.md) | Public nuget.org distribution; the first push is unrevocable |
| [0002](adr/0002-sealed-allowlist-ruleset-architecture.md) | Ship rules + config, not third-party assemblies; the sealed allowlist |
| [0003](adr/0003-tag-triggered-trusted-publishing-with-pr-time-gates.md) | Tag-triggered trusted publishing; PR-time gates; dogfooding |
| [0004](adr/0004-diagnostic-ids-are-public-api.md) | Versioning and the changelog |

Consumer-facing instructions live in [`consuming.md`](consuming.md), which is packed as the package
README. Do not duplicate them here.

---

## 1. What v1 ships

- **Three analyzers** — `APB0001`, `APB0002`, `APB0003` (section 3).
- **One code fix** — for `APB0003` only. `APB0001` ships without a fixer, settled on evidence in
  [#5](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/5): 31 primary constructors
  fleet-wide, 26 of them in test code that gets a carve-out anyway. Adding it later is a minor bump.
- **The shipped config** — `Aprbrown.Analyzers.globalconfig`, ~131 enumerated rules (section 2).
- **Two MSBuild properties** — `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors`, as overridable
  defaults (section 4).
- **Release tracking and a changelog** — `AnalyzerReleases.Shipped.md`, `.Unshipped.md`,
  `CHANGELOG.md`.

The package takes **no dependency** on `Meziantou.Analyzer` or `StyleCop.Analyzers` (ADR-0002).

---

## 2. The shipped configuration

### 2.1 Form

A global AnalyzerConfig file named **`Aprbrown.Analyzers.globalconfig`**.

Three constraints, each load-bearing:

- **It must not be named `.globalconfig`.** That exact name is auto-discovered by the SDK in the
  project directory *and every ancestor*, and defaults to `global_level = 100` — a head-on collision
  with a consumer's own `.globalconfig`, whose colliding keys are then **unset** with a
  `MultipleGlobalAnalyzerKeys` warning. Verified in
  [#2](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/2).
- **It must open with `is_global = true`.** A file is global because of that key or because it is
  named `.globalconfig`. Since the name is deliberately not that, the key is the only thing making it
  global. Without it the file is parsed as a section-less `.editorconfig` and does nothing.
- **It must not set `global_level`.** Left unset it defaults to `0` for a non-`.globalconfig`
  filename, so a consumer's own `.globalconfig` (defaulting to `100`) outranks it. Setting `100`
  would tie with a consumer's file, and equal levels means a warning and *both* entries dropped.
  This package is a baseline; it should lose those ties.

`global_level` cannot lift the file above a consumer's local `.editorconfig` in any case — #2 tested
`global_level = 9999` and the `.editorconfig` still won. That is the intended hierarchy, not a
limitation to work around.

### 2.2 Contents

The file is a **sealed allowlist** (ADR-0002): one blanket line, then every enabled rule enumerated by
diagnostic ID.

```ini
is_global = true

# Blanket: everything off, including rules that do not exist yet.
dotnet_analyzer_diagnostic.severity = none

# ... ~131 explicit rule lines follow ...
```

Enumerate **by ID only**. Never by `dotnet_analyzer_diagnostic.category-*.severity` — a category
re-enable admits default-off rules and rules upstream adds later, breaking the seal (ADR-0002
decision 4, with measurements).

### 2.3 Derivation — run once, then frozen

The rule list is *derived*, not transcribed, and the derivation is a **one-time act**. Re-running it
against newer vendor versions would silently admit whatever rules those versions added, which is
exactly what the seal exists to prevent. After generation the file is edited by hand, one deliberate
line at a time, under ADR-0004's versioning policy.

Derive against these pinned versions:

| Component | Version |
|---|---|
| `Meziantou.Analyzer` | `3.0.123` |
| `StyleCop.Analyzers` | `1.2.0-beta.556` |
| .NET SDK (source of the `CA` analyzers) | `10.0.110` |
| `Microsoft.CodeAnalysis.CSharp` | `4.14.0` |
| `Microsoft.CodeAnalysis.Analyzers` | `3.11.0` |

Record each rule at **the severity the vendor assigns it**; read that value during generation rather
than assuming `warning`. Then apply the steps below.

**Step 1 — Meziantou: all 103 default-on rules, minus one, plus one.**

Take every `Meziantou.Analyzer` rule with `isEnabledByDefault = true` — 103 rules, of which 100
default to `Warning` and `MA0037`, `MA0039`, `MA0049` default to `Error`. Then:

- **Omit `MA0004`** (`ConfigureAwait`). The sole universality-test exclusion: its rationale is false
  for a library like `EveEsiClient`. A repository that is all-web may re-enable it locally.
- **Add `MA0032`** at `warning`. It is default-*off*, so a default-on sweep misses it; the house wants
  the call-site half of the cancellation rule, completing the trio `CA2016` + `MA0040` + `MA0032`
  alongside `APB0002`'s declaration half.

Rules that are framework-specific but **inert** elsewhere ship on — `MA0115`–`MA0119`, `MA0123`,
`MA0124`–`MA0126`, `MA0139`, `MA0153` (Blazor, `Microsoft.Extensions.Logging`, Serilog). They are
silent where the framework is absent and cover the day a repository adopts it.

`MA0025` (`NotImplementedException`) and `MA0026` (TODO) ship on. Fleet exposure was measured: 6
TODOs in total, and 135 `NotImplementedException` throws **all** in `EveIndustryTools.Tests`.
`MA0025` therefore comes with a documented `tests/**` carve-out — throwing *is* the correct statement
that something is not part of a fake's contract.

**Step 2 — SDK `CA` rules: the 27 that are default-on.**

Take every `CA` rule the .NET `10.0.110` SDK enables by default at `AnalysisMode=Default` — 27 rules —
and enumerate each by ID.

A contradiction check against the rest of the ruleset came back clean: all 27 are correctness rules
(platform compatibility, marshalling, `stackalloc` in loops, rethrow, spans) with no naming or
ordering opinion. The only overlaps are *duplications*, never conflicts — `CA2200`≈`MA0027`,
`CA2016`≈`MA0040`, same fix in each case. The two rules that would genuinely contradict the house
style, `CA2007` (`ConfigureAwait`, against our `MA0004` exclusion) and `CA1707` (no underscores,
against `_camelCase`), are **default-off and stay off** under the blanket — which is precisely why
re-enabling by category is forbidden.

**Do not set `AnalysisMode` or `AnalysisLevel` anywhere.** They cannot reach past the blanket at any
value (ADR-0002 decision 4). Setting them would document an intent the build does not honour.

**Step 3 — StyleCop: four rules.**

`SA1201`, `SA1202`, `SA1203`, `SA1204` — member ordering by kind, then access, then const, then
static. The one mainstream analyzer shipping ordering rules; this is not a StyleCop adoption.

`Mixologist`'s eight `category-StyleCop.CSharp.*.severity = none` lines are **redundant** under the
blanket and must not be carried over. Do not port them.

**Step 4 — IDE, naming and style rules.**

Every one of these **must be enumerated by ID**. Verified in #4: under the blanket, a naming rule
whose `dotnet_diagnostic` severity is not set explicitly is *silently unenforced* — it neither fires
nor warns that it is inactive.

- `IDE1006`, `IDE0003`, `IDE0009`.
- The naming rules, symbols and styles for private-field `_camelCase` and private-static
  `PascalCase`. Port the `dotnet_naming_rule.*` / `dotnet_naming_symbols.*` / `dotnet_naming_style.*`
  triples verbatim; #2 verified they work byte-identically in a global config.
- `dotnet_style_require_accessibility_modifiers = always`.
- `dotnet_style_qualification_for_field` / `_property` / `_method` / `_event` = `false`.
- Seven `csharp_style_expression_bodied_*` entries at **`suggestion`** — non-gating nudges so six
  repositories agree on shape without failing builds.

> **Note while porting the naming block:** `Mixologist`'s `.editorconfig` carries a comment claiming
> naming rules are matched in declaration order and that a general rule would otherwise swallow a
> specific one. That is **false** — rules are ordered by specificity, and #2 verified it twice. The
> behaviour is correct today for a different reason than the comment gives. Do not carry the comment
> across, and do not preserve declaration order as if it mattered.

**Step 5 — the three `APB` rules**, at the default severities in section 3.

### 2.4 What deliberately stays local

Not in the shipped config, documented in [`consuming.md`](consuming.md) as a template:

| Local entry | Reason |
|---|---|
| `MA0004` | Rationale false for libraries — the sole universality exclusion |
| `MA0025 = none` under `[tests/**.cs]` | Path-scoped; cannot travel |
| `APB0001 = none` under `[tests/**.cs]` | Path-scoped; test primary constructors hold values, not dependencies |
| `MA0048 = none` for `Pages/`, `Data/Migrations/` | Path-scoped; Razor and EF naming |
| `MA0051 = none` for EF `Migrations/` | Path-scoped; generated code |

Path scoping cannot travel: a global AnalyzerConfig accepts only exact absolute file paths as section
headers, and rejects globs outright with `InvalidGlobalSectionName`, dropping the section. An absolute
prefix does not rescue a glob (#2).

The `tests/**` `APB0001` carve-out is **not optional** in the onboarding snippet. 84% of the fleet's
primary constructors are the test-double idiom
(`private sealed class StubShopper(ShoppingBoard board) : IShopper`). Without it `Mixologist` — chosen
as the first onboarding precisely because it should light up at zero — lights up with 20 violations on
day one.

---

## 3. The three APB analyzers

Specified by behaviour: what each rule flags, what it must **not** flag, and the test cases that prove
it. Implement to the behaviour, not to a described implementation.

`MIX0001`–`MIX0003` in `Mixologist` are the same three rules and remain in that repository's git
history after section 8 deletes them — useful as a cross-check, not as a source to copy.

### 3.1 `APB0001` — Do not use primary constructors on classes or structs

| | |
|---|---|
| Category | `Design` |
| Default severity | `Warning` |
| Code fix | **None in v1** |
| Message | `'{0}' declares a primary constructor; use an explicit constructor with readonly fields` |

**Rationale.** Injected dependencies belong in an explicit constructor assigning readonly fields, so a
class's dependency surface is one legible block rather than a parameter list the compiler scatters.

**Flags** a `class` or `struct` declaration that has a primary constructor parameter list. Report at
the **parameter list**, with the type's identifier as `{0}`.

**Must not flag:**

- **`record` and `record struct` declarations — under any circumstances.** Positional parameters are
  the entire point of a record. This is the single most important behaviour in the rule: the fleet
  contains **537 positional records**, all of which must stay silent.
- A class or struct with no parameter list.
- An explicit constructor, however many parameters it has.

> **The trap.** A record declaration *also* carries a parameter list, and shares a syntax base type
> with class and struct declarations. Any implementation that reaches for that shared base and then
> filters will flag every record in the codebase if the filter is wrong or absent. The record cases
> below are not edge cases — they are the primary test.

**Required tests**

| Input | Expected |
|---|---|
| `class C(int x) { }` | 1 diagnostic, on `(int x)` |
| `struct S(int x) { }` | 1 diagnostic |
| `record R(int X);` | **0** |
| `record struct RS(int X);` | **0** |
| `class C { public C(int x) { } }` | 0 |
| `class C<T>(T value) { }` | 1 diagnostic |
| `class C { }` | 0 |

### 3.2 `APB0002` — Do not give a CancellationToken parameter a default value

| | |
|---|---|
| Category | `Design` |
| Default severity | `Warning` |
| Code fix | **None in v1** |
| Message | `Parameter '{0}' defaults its CancellationToken; make every caller pass one` |

**Rationale.** The caller always decides cancellation. A defaulted token lets a caller silently opt
out, and the opting-out is invisible at the call site. `CA2016`, `MA0032` and `MA0040` police the
call site; this is the **declaration** half.

**Flags** any parameter whose type is exactly `System.Threading.CancellationToken` and which has an
explicit default value. Report at the **parameter**, with its name as `{0}`. One diagnostic per
offending parameter.

Applies to ordinary methods, constructors and interface members alike, and to a record's positional
parameter list. Records are covered because §3.1 endorses that shape — a positional list is the only
route by which a defaulted token can reach a record, so leaving it out would put the shape beyond
every rule in the set.

**Must not flag:**

- A `CancellationToken` parameter with no default value.
- A defaulted parameter of any other type.
- Anything at all, in a compilation where `System.Threading.CancellationToken` cannot be resolved —
  the analyzer must produce no diagnostics rather than fail.
- A parameter whose type is merely *named* `CancellationToken` in some other namespace.

**Out of scope in v1, deliberately:** local functions and delegates. This is a recorded boundary, not
an oversight — do not "fix" it without a version bump under ADR-0004.

Also not covered, for a different reason: the primary constructor of a `class` or `struct`. §3.1
rejects that shape outright, so a second diagnostic on the same construct would only be noise. If
§3.1 is ever relaxed, this becomes a gap to close.

**Required tests**

| Input | Expected |
|---|---|
| `Task M(CancellationToken ct = default)` | 1 diagnostic, on the parameter |
| `Task M(CancellationToken cancellationToken)` | 0 |
| `Task M(CancellationToken ct = default(CancellationToken))` | 1 diagnostic |
| `C(CancellationToken ct = default)` (constructor) | 1 diagnostic |
| `interface I { Task M(CancellationToken ct = default); }` | 1 diagnostic |
| `void M(int x = 5)` | 0 |
| `Task M(CancellationToken a = default, CancellationToken b = default)` | 2 diagnostics |
| Compilation without `CancellationToken` available | 0, no crash |
| `Task M(CancellationToken ct = new CancellationToken())` | 1 diagnostic |
| `record R(CancellationToken Ct = default)` | 1 diagnostic |
| `record struct RS(CancellationToken Ct = default)` | 1 diagnostic |
| `record R(CancellationToken Ct)` | 0 |
| A `CancellationToken` declared in a namespace other than `System.Threading` | 0 |
| `void Local(CancellationToken ct = default)` (local function) | 0 — out of scope |
| `delegate void D(CancellationToken ct = default)` | 0 — out of scope |

### 3.3 `APB0003` — Parameter names should match the implemented interface member

| | |
|---|---|
| Category | `Naming` |
| Default severity | `Warning` |
| Code fix | **Yes** (section 3.4) |
| Message | `Parameter '{0}' should be named '{1}' to match {2}` |

`{0}` is the actual name, `{1}` the interface's name, `{2}` the interface member as
`InterfaceName.MethodName`.

**Rationale.** `CA1725` requires this for base-*class* overrides but is blind to interface
implementations. This is the missing half — so
`IEntityTypeConfiguration<T>.Configure(EntityTypeBuilder<T> builder)` is implemented as
`Configure(EntityTypeBuilder<T> builder)`, not with the parameter renamed.

**Flags**, for each `class` or `struct`: each method **declared on that type** that implements an
interface method, where a parameter's name differs (ordinal comparison) from the interface
parameter's name at the same position. Report at the parameter's source location.

**Must not flag:**

- Interface declarations themselves — only implementers are policed.
- Property or event accessors. Their parameter names are synthesised, never written by the author, so
  there is nothing to rename. Restrict to ordinary methods.
- A member satisfied by **inheritance from a base type**. The implementing method belongs to the base
  and is that type's to answer for; reporting it on the derived type points at code the developer
  cannot edit there.
- A parameter with **no source location** — an implementation inherited from metadata. There is
  nothing to fix.

**Required tests**

| Input | Expected |
|---|---|
| `interface I { void M(int alpha); }` + `class C : I { public void M(int beta) { } }` | 1 diagnostic, expected `alpha` |
| Same, parameter named `alpha` | 0 |
| Explicit implementation `void I.M(int beta)` | 1 diagnostic |
| `interface I { int P { get; set; } }` implemented | 0 |
| `class Base : I { public void M(int beta) {} }` + `class Derived : Base, I { }` | 1 on `Base` only, **0** on `Derived` |
| Generic `interface I<T> { void M(T alpha); }` implemented with `beta` | 1 diagnostic |
| Class implementing two interfaces, one mismatched | 1 diagnostic |
| Multiple mismatched parameters on one method | one diagnostic per parameter |

### 3.4 The `APB0003` code fix

Title: `Rename parameter to '{expected}'`.

**It must rename the symbol, not the declaration token.** A parameter's name appears throughout the
method body; rewriting only the declaration produces code that does not compile. Use the Roslyn
rename API (`Renamer.RenameSymbolAsync`) so every reference is updated, and return a solution change.

Two consequences for the project layout:

- `Aprbrown.Analyzers.CodeFixes` references `Microsoft.CodeAnalysis.CSharp.Workspaces`, pinned to
  `4.14.0` alongside the rest of Roslyn.
- **Do not register a batch `FixAllProvider`.** Rename-based fixes do not compose — two renames
  applied to one document from a stale solution snapshot can conflict or clobber each other. Single-fix
  only in v1.

Tests use `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing`, asserting the fixed source compiles and
that a body reference to the old name was updated.

Note "trivial fix" from map Note 8 describes the *user-visible* change, not the implementation.

---

## 4. Package layout and delivery

### 4.1 Projects

| Path | TFM | Purpose |
|---|---|---|
| `src/Aprbrown.Analyzers/` | `netstandard2.0` | The three `DiagnosticAnalyzer`s |
| `src/Aprbrown.Analyzers.CodeFixes/` | `netstandard2.0` | The `APB0003` `CodeFixProvider` |
| `src/Aprbrown.Analyzers.Package/` | — | Packaging project; produces the `.nupkg` |
| `tests/Aprbrown.Analyzers.Tests/` | current .NET | Analyzer + code fix tests |
| `tests/Fixture.Consumer/` | `net10.0` | Consumer fixture for the pack-consume smoke test |
| `scripts/verify-package.sh` | — | Pack-consume gate (ADR-0003 decision 5) |

`netstandard2.0` for the analyzer and fixer is a Roslyn host constraint, not a preference: an analyzer
built against an older Roslyn loads in a newer host, never the reverse. Roslyn is pinned to `4.14.0`,
below the 5.0.0 the .NET 10 SDK hosts, so the assemblies load in `dotnet build`, container builds and
the IDE alike.

The test project needs an explicit `Microsoft.CodeAnalysis.CSharp.Workspaces` `4.14.0` reference. The
testing harness only floors `Workspaces` at `1.0.1`, and NuGet resolves a floor to the floor — without
the pin, analyzer tests run against Roslyn 1.0.

### 4.2 Package contents — an explicit allowlist

ADR-0001 treats package contents as a reviewed allowlist and the first push as unrevocable. Pack
exactly:

| Packed path | Source |
|---|---|
| `analyzers/dotnet/cs/Aprbrown.Analyzers.dll` | analyzer assembly |
| `analyzers/dotnet/cs/Aprbrown.Analyzers.CodeFixes.dll` | code fix assembly |
| `build/Aprbrown.Analyzers.props` | section 4.3 |
| `build/Aprbrown.Analyzers.globalconfig` | section 2 |
| `README.md` | [`consuming.md`](consuming.md), packed as `PackageReadmeFile` |

Every entry uses an explicit `PackagePath`. Do **not** rely on `<None Include="build\**" Pack="true" />` —
it silently omits dotfiles, and #2 produced a package that built clean and enforced nothing that way.

Not packed: `LICENSE` (declared as `<PackageLicenseExpression>MIT</PackageLicenseExpression>` with
`<Copyright>Copyright (c) 2026 Andrew P R Brown</Copyright>`), no `.pdb`, no `.snupkg`, no repository
`README.md`. `PackageProjectUrl`, `RepositoryUrl` and `HelpLinkUri` point at the public repository.

### 4.3 `build/Aprbrown.Analyzers.props`

One file carries both the MSBuild defaults and the config injection:

```xml
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild Condition="'$(EnforceCodeStyleInBuild)'==''">true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors Condition="'$(TreatWarningsAsErrors)'==''">true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)Aprbrown.Analyzers.globalconfig" />
  </ItemGroup>
</Project>
```

`GlobalAnalyzerConfigFiles` is Microsoft's documented extension point, and the SDK folds it into
`EditorConfigFiles` before passing it to the compiler — there is exactly one channel, not two. The
`.targets`/`EditorConfigFiles` alternative exists to select a config from late-evaluated properties,
which this package does not need now that `AnalysisLevel` is proven irrelevant to it.

**The properties must come from package props specifically.** The SDK assigns
`TreatWarningsAsErrors=false` in `Microsoft.NET.Sdk.props` — after `Directory.Build.props` and package
props, but *before* the project file body. So an `<Import>` at the top of a `.csproj` sets it and is
then silently overwritten, producing warnings where errors were intended. Package props run early
enough; a `.csproj` import does not.

**Verified this session, end to end from a real `.nupkg`** — both properties arrive, and a consumer
overrides them:

| Consumer configuration | Result for a `CA` violation |
|---|---|
| Package defaults only | **error** |
| `Directory.Build.props` sets `TreatWarningsAsErrors=false` | **warning** — consumer wins |
| `Directory.Build.props` sets `true` | error |

Package props are imported before `Directory.Build.props`, so the `Condition` idiom yields genuine
overridable defaults rather than a mandate.

`Nullable` and `ImplicitUsings` do **not** travel — they are project-shape decisions, not part of how
the ruleset functions.

---

## 5. Build, release and versioning

ADR-0003 and ADR-0004 settle these; the deliverables are listed here so nothing is missed.

- `.github/workflows/ci.yml` — PR and `main` pushes. Build, test, and run `verify-package.sh`.
- `.github/workflows/release.yml` — `v*` tags only; the sole workflow holding `id-token: write`.
  Packs with `-p:Version=${GITHUB_REF_NAME#v}` — the tag is the only source of a version number. No
  `NUGET_API_KEY` exists; `NuGet/login@v1` exchanges an OIDC token for a one-hour single-use key.
  Must not delegate its build via `workflow_call`; use a local composite action if sharing is needed.
- **Approval gate.** `release.yml`'s publish job declares `environment: release`, with the repository
  owner as a required reviewer and **`Prevent self-review` left off** — verified as GitHub's default,
  so a sole maintainer can approve their own deployment. This closes ADR-0003's open point: the push
  is unrevocable and a mistyped tag spends a version number permanently, so one click is cheap
  insurance. The nuget.org trusted publishing policy is scoped to the same environment.
- `scripts/verify-package.sh` — packs to a temp feed, restores `tests/Fixture.Consumer` against it,
  builds, and asserts an exact diagnostic set. Assertions in section 6.
- `CHANGELOG.md` — build-impact classified per ADR-0004; `release.yml` cuts a GitHub Release from the
  matching section.
- `AnalyzerReleases.Shipped.md` / `.Unshipped.md` from day one. `release.yml` asserts `Unshipped.md`
  contains no rule rows — deliberately version-agnostic, since a detection-fix patch adds no section.

**First release is a two-step ritual:** review repository contents → flip the repository public →
tag `v1.0.0-preview.1` and expect to throw it away → validate by onboarding `Mixologist` against real
nuget.org → tag `v1.0.0`.

---

## 6. The pack-consume smoke test

Analyzer unit tests test the analyzer; they cannot test the package. This gate tests the package.

| Assertion | Proves |
|---|---|
| An `APB` violation fires | The analyzer assembly is packed and loads |
| A `CA` violation fires | The 27 enumerated `CA` IDs survive the blanket |
| A `Meziantou` violation fires, with `Meziantou.Analyzer` referenced by the fixture | Config-without-assembly binds when the assembly arrives |
| An IDE naming violation fires | Enumeration-by-ID works — these are silently unenforced otherwise |
| `MA0004` does **not** fire | The sole deliberate exclusion stays excluded |
| A `CA1707`-triggering name does **not** fire | The seal holds; default-off rules stayed off |
| Both MSBuild properties arrive | #2's hazard H1 is a *silent* failure |

The `CA` assertion supersedes ADR-0003's wording, which described it as proving "the `AnalysisMode`
tier survives the blanket". There is no `AnalysisMode` tier — see section 10.

The `CA1707` row is new to this spec: it is the direct regression test for the category-re-enable
mistake, which is the cheapest available way to break the seal and would otherwise pass every other
assertion here.

A shell script rather than an xunit test: the precondition is a packed artifact and a temporary feed,
and the gate must be runnable on a dev machine, not only in CI.

---

## 7. Holding this repository to its own ruleset

Per ADR-0003 decision 6, this repository's projects reference **the same physical config file the
packaging project packs**, via `GlobalAnalyzerConfigFiles` — not a copy — and take the same pinned
third-party `PackageReference`s the spec prescribes. It is the reference implementation of the
onboarding snippet.

Two known holes, both inherent:

- **A project cannot be its own analyzer.** `src/Aprbrown.Analyzers` cannot have `APB0001` enforced
  against it. The `APB` rules gate `Aprbrown.Analyzers.CodeFixes`, the packaging project and the tests
  via `ProjectReference` + `OutputItemType="Analyzer"`; the analyzer project is held to the config and
  third-party rules only.
- **The blanket suppresses the RS-series** that keeps analyzer authors honest — including RS2008,
  which is what makes release tracking self-enforcing. This repository's **local `.editorconfig`**
  re-enables `MicrosoftCodeAnalysisCorrectness`, `MicrosoftCodeAnalysisDesign`,
  `MicrosoftCodeAnalysisReleaseTracking` and `MicrosoftCodeAnalysisPerformance` at category level.

That second point is the one place category-level re-enabling is correct, and it is worth being
explicit about why it does not contradict ADR-0002: it is in a **local `.editorconfig`**, not the
shipped config. The seal governs what the package promises consumers. A repository loosening rules for
itself is exactly the deviation mechanism working — and this repository is its first real exercise.

When a house rule trips on this repository's own code the fix is a **local deviation, not a change to
the shipped config**. The analyzer projects target `netstandard2.0` and will hit rules the `net10.0`
fleet never does.

---

## 8. Migrating `Mixologist` from `MIX` to `APB`

`Mixologist` is the first onboarding (section 9). Its migration is the only one this spec details,
because it is the one that also *deletes* a local analyzer implementation.

1. **Onboard normally** — follow [`consuming.md`](consuming.md): add the three pinned
   `PackageReference`s to `Directory.Build.props`, add the carve-out `.editorconfig` template.
2. **Delete `src/Mixologist.Analyzers/`** and its `ProjectReference` +
   `OutputItemType="Analyzer"` wiring from every consuming project.
3. **Delete `tests/Mixologist.Analyzers.Tests/`.** The equivalent coverage now lives in this
   repository (section 3) and was rewritten from behaviour, not ported.
4. **Shrink `Mixologist`'s `.editorconfig` to deviations only.** Everything the shipped config now
   carries comes out — the naming triples, the IDE and qualification entries, the `SA120x` block, the
   eight StyleCop category switch-offs (redundant under the blanket), and the `MA0048`/`MA0051`
   comments that are now carve-outs. What stays:
   - `MA0004` if `Mixologist` wants it (it is all-web, so re-enabling locally is reasonable),
   - the `tests/**` carve-outs for `MA0025` and `APB0001`,
   - the `MA0048`/`MA0051` path carve-outs for `Pages/` and EF `Migrations/`.
   - Fix the false naming-order comment rather than carrying it across (section 2.3).
5. **Remove the `Microsoft.CodeAnalysis.*` pins** that existed only to build the local analyzer —
   `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Analyzers`, and the
   `Microsoft.CodeAnalysis.CSharp.Workspaces` / `...Analyzer.Testing` test pins.
6. **Expect a zero-violation build.** `Mixologist` builds clean today (verified 0/0) and reads **zero
   primary constructors in production**; all 20 of its `APB0001` hits are in `tests/`, covered by the
   carve-out. A non-zero count means the delivery mechanism is wrong, not the code — which is the
   whole reason this repository goes first.

### Reconciling a repository that already has competing config

Only `EveIndustryTools` has config of its own, but the pattern is general:

- **Its code is already compliant.** #5 measured 1,188 class declarations, zero primary constructors,
  zero naming violations, classic constructors with `_camelCase` throughout. The risk is entirely its
  *configuration*, not its source.
- **Reconcile by subtraction.** Delete every local entry the shipped config now sets identically.
  What remains is the repository's genuine deviation set, and it should be short enough to justify
  line by line.
- **A different pinned `Meziantou.Analyzer` version is fine** and needs no resolution — the package
  carries no dependency on it (ADR-0002). The repository keeps its pin. Rules in the shipped config
  that its version does not implement are inert; rules its version adds arrive disabled under the
  blanket.
- **Keep local anything path-scoped**, which cannot travel regardless.
- **Adoption cost on naming and accessibility is zero fleet-wide** — private-field `_camelCase`,
  private-static `PascalCase` and `IDE0040` all measured 0 across all six repositories. Violations
  will concentrate in the Meziantou and `CA` tiers.

---

## 9. Onboarding order

The fleet is onboarded in this order, and the order is load-bearing rather than arbitrary. Each
repository is a **big-bang** adoption: install, fix every violation, done — no severity ratchet, no
baseline suppression file.

| # | Repository | Why here |
|---|---|---|
| 1 | `Mixologist` | Already compliant, so its violation count should be **zero** |
| 2 | `WordleHelper` | First honest read of adoption cost on a repository that never had the ruleset |
| 3–5 | `EveEsiClient`, `FPL_Helper/web`, `WeddingSite` | Bulk adoption once the cost is known |
| 6 | `EveIndustryTools` | The only repository with competing configuration |

**`Mixologist` first, because it isolates the variable.** It is the repository the ruleset was
extracted *from*, it builds clean today (verified 0/0), and it reads zero primary constructors in
production. So its onboarding tests the **delivery mechanism** on its own rather than confounding it
with a wall of violations: any diagnostic that fires is evidence the package is wrong, not that the
code is. A repository with real violations could not distinguish those two failures. It is also the
final validation of the `v1.0.0-preview.1` rehearsal (ADR-0003 decision 11) — it consumes the preview
from real nuget.org before `v1.0.0` is tagged.

Its 20 `APB0001` hits are all in `tests/`, which is why the `tests/**` carve-out is mandatory in the
onboarding snippet rather than optional. Without it the canary chosen to light up at zero lights up
with 20 on day one, and the signal is lost.

**`EveIndustryTools` last, but for a narrower reason than originally assumed.** Charting sequenced it
last as the hard repository. #5 reframed it: its **code is already fully compliant** — 1,188 class
declarations, zero primary constructors, zero naming violations, classic constructors with
`_camelCase` throughout. Its risk is **entirely its competing configuration** (its own severities, its
differently-pinned `Meziantou.Analyzer`), which is a reconciliation exercise (section 8), not a
code-fixing one. It goes last so that reconciliation happens against a ruleset already proven on five
repositories.

**`WordleHelper` second** because it is the first repository with no prior relationship to the
ruleset. Whatever it costs to adopt is the number that predicts repositories 3–5, and it is worth
learning that before committing to a bulk pass. Expect violations to concentrate in the `Meziantou`
and `CA` tiers: naming and accessibility measured **zero across all six repositories**, so that axis
costs nothing anywhere.

Onboarding the repositories is **out of scope for this map** — the sequencing is spec, the work is
downstream.

---

## 10. Corrections to earlier decisions

Recorded so a reader of the map's history is not misled.

1. **There is no `AnalysisMode` tier.** #4 concluded the 27 `CA` rules would be restored "via an
   `AnalysisMode` tier (option 1)" and left proving it to this ticket. Measured: `AnalysisMode` and
   `AnalysisLevel` cannot reach past the blanket at any value, including `All` and `latest-all`. The
   `CA` rules are enumerated by ID like everything else. ADR-0003's smoke-test table inherits the
   original wording and is amended in place.
2. **Enumeration by ID is forced, not chosen.** Re-enabling by category admits default-off rules —
   demonstrated with `CA1707`, a rule this ruleset specifically wants off — and would let future
   upstream rules arrive enabled, breaking the seal.
3. **Map Note 5's "overridable defaults" is now verified**, not assumed. Consumer
   `Directory.Build.props` beats package props (section 4.3).
4. **`APB0003`'s fixer is not textually trivial.** It must rename the symbol so body references
   update, which pulls `Microsoft.CodeAnalysis.CSharp.Workspaces` into the code fix project and rules
   out a batch `FixAllProvider`.
5. **ADR-0003's environment open point is closed** — the approval gate is adopted (section 5).
6. **The Meziantou sweep is `isEnabledByDefault` *and* a build-gating severity**, not
   `isEnabledByDefault` alone. Section 2.3 step 1 says "every `Meziantou.Analyzer` rule with
   `isEnabledByDefault = true` — 103 rules". Measured against the pinned `3.0.123`: 209 rules
   ship, **173** are enabled by default, and only **103** of those default to `Warning` or
   `Error`. The other 70 default to `Info` (66) or `Hidden` (4) and do not gate a build. The
   stated count and its 100-`Warning`/3-`Error` breakdown are correct; only the criterion
   sentence is loose, and read literally it yields 173 rules rather than 103. The narrower
   reading is the one the derivation used and the one the counts describe.
   `consuming.md`'s "All 103 default-on rules" inherits the same looseness.
7. **The call-site "trio" is a duo.** Sections 2.3 and 3.2 pair `APB0002`'s declaration half with
   `CA2016` + `MA0040` + `MA0032` at the call site. `MA0040` is enabled by default at `Info`, so
   correction 6 excludes it from the 103 and it is **not** enumerated — it stays off under the
   blanket. Only `MA0032` is in the shipped config today; `CA2016` arrives with step 2. Admitting
   `MA0040` would be a third deliberate departure from the sweep and a ruleset decision in its
   own right, so it is recorded here rather than taken silently.
