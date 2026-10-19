# Aprbrown.Analyzers — implementation spec

What the package contains, how it is built, and how it reaches a consuming repository. Written so an
implementation session can build v1 without reopening a design question.

**Read first:** [`GLOSSARY.md`](../GLOSSARY.md) for vocabulary, then the ADRs — this document
states *what to build*, the ADRs state *why*, and it is not repeated here.

| ADR | Settles |
|---|---|
| [0001](adr/0001-distribution-via-public-nuget-org.md) | Public nuget.org distribution; the first push is unrevocable |
| [0002](adr/0002-sealed-allowlist-ruleset-architecture.md) | Ship rules + config, not third-party assemblies; the sealed allowlist |
| [0003](adr/0003-tag-triggered-trusted-publishing-with-pr-time-gates.md) | Tag-triggered trusted publishing; PR-time gates; dogfooding |
| [0004](adr/0004-diagnostic-ids-are-public-api.md) | Versioning and the changelog |
| [0005](adr/0005-ship-a-suppressor-so-one-rule-reports-each-mismatch.md) | The `APBS0001` suppressor, so `CA1725` and `APB0003` never report the same parameter |

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

1.1.0 adds a fourth analyzer, `APB0004`, with its own code fix (section 3.5), and a
`DiagnosticSuppressor`, `APBS0001`, that leaves interface implementations to `APB0003` (section 3.3,
ADR-0005).

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
- `CA1725` with `dotnet_code_quality.CA1725.api_surface = all`, so an override keeps its base
  declaration's parameter names at every accessibility. Not an SDK default, so it belongs to this
  tier rather than the `CA` tier of step 2. Added in 1.1.0 alongside the `APBS0001` suppressor
  (section 3.3).
- The naming rules, symbols and styles for private-field `_camelCase` and private-static
  `PascalCase`. Port the `dotnet_naming_rule.*` / `dotnet_naming_symbols.*` / `dotnet_naming_style.*`
  triples verbatim; #2 verified they work byte-identically in a global config.
- `dotnet_style_require_accessibility_modifiers = always`.
- `dotnet_style_qualification_for_field` / `_property` / `_method` / `_event` = `false`.
- Seven `csharp_style_expression_bodied_*` entries, their IDs at **`warning`**. They first shipped
  at `suggestion` as non-gating nudges; #67 raised them, because a suggestion never reaches a build
  log but SonarQube imports it, so a quality gate failed on code every build had passed.

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
| `MA0048 = none`, repository-wide or path-scoped | A deviation, not always a carve-out: Razor and EF naming are path-shaped, a file-per-type convention is not — see section 10 correction 14 |
| `MA0051 = none` for EF `Migrations/` | Path-scoped; generated code |
| `SA0001 = none` for any project without an XML documentation file | Reported with no location, so the blanket cannot reach it — see section 10 correction 11 |

Path scoping cannot travel: a global AnalyzerConfig accepts only exact absolute file paths as section
headers, and rejects globs outright with `InvalidGlobalSectionName`, dropping the section. An absolute
prefix does not rescue a glob (#2).

The `tests/**` `APB0001` carve-out is **not optional** in the onboarding snippet. 84% of the fleet's
primary constructors are the test-double idiom
(`private sealed class StubShopper(ShoppingBoard board) : IShopper`). Without it `Mixologist` — chosen
as the first onboarding precisely because it should light up at zero — lights up with 20 violations on
day one.

### 2.5 SonarQube verdicts

Every SonarQube C# rule that has fired in the fleet carries a Sonar verdict
([ADR-0008](adr/0008-cover-sonarqube-findings-with-the-house-ruleset-not-sonaranalyzer.md)). A
*covered* rule names the house rule that enforces it in the build; a *rejected* rule is deactivated
in the homelab "House ruleset" quality profile; a *signal* rule stays active there and nowhere else.
A rule that fires without a row here is untriaged, and gets one.

| Sonar rule | Verdict | Covering rule or reason |
|---|---|---|
| `S6966` Awaitable method should be used | covered | `CA1849` |
| `S3358` Ternary operators should not be nested | covered | `APB0006` |
| `S6618` "string.Create" should be used instead of "FormattableString" | covered | `MA0111` |
| `S1118` Utility classes should not have public constructors | covered | `CA1052`, which asks for a `static` class rather than a private constructor |
| `S1905` Redundant casts should not be used | covered | `IDE0004` |
| `S1144` Unused private types or members should be removed | covered | `IDE0051`, `IDE0052` |
| `S107` Methods should not have too many parameters | rejected | Taste; `MA0051` already bounds method length |
| `S3267` Loops should be simplified with LINQ expressions | rejected | Taste, and at odds with the performance tier |
| `S2699` Tests should include assertions | signal | No equivalent; recognising every assertion library is SonarQube's strength |
| `S2092` Cookies should have the "secure" flag | signal | `CA5382` and `CA5383` never fire on ASP.NET Core 3.0 or later: they only analyse calls through `Microsoft.AspNetCore.Http.Internal.ResponseCookies`, a type that version removed |
| `S3330` Cookies should have the "HttpOnly" flag | signal | `CA5396` covers `System.Web` only |
| `S8969` Null-forgiving operators should not be redundant | signal | `IDE0080` covers `!is` only; `MA0191` bans every `!` |
| `S3218` Inner class members should not shadow outer class static or type members | signal | No equivalent; `MA0084` covers locals only |
| `S1192` String literals should not be duplicated | signal | No equivalent |
| `S1075` URIs should not be hardcoded | signal | No equivalent; `CA1054`–`CA1056` concern string-typed URIs, not literals |
| `S108` Nested blocks of code should not be left empty | signal | Partial only: `MA0090` (Info), and `MA0037`/`SA1106` for empty statements |
| `S4136` Method overloads should be grouped together | signal | No equivalent; `SA1201`–`SA1204` order by kind, not overload |

`CA1849`, `CA1052`, `IDE0004`, `IDE0051`, `IDE0052` and `APB0006` join the house ruleset in 2.1.0;
until then their rows are the plan, not the shipped config.

---

## 3. The APB analyzers

Specified by behaviour: what each rule flags, what it must **not** flag, and the test cases that prove
it. Implement to the behaviour, not to a described implementation.

`MIX0001`–`MIX0003` in `Mixologist` are the same rules as `APB0001`–`APB0003` and remain in that repository's git
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
- A defaulted parameter on a method that overrides a base member, or implements an interface member
  implicitly or explicitly, when the **corresponding parameter** (same ordinal) of that member has
  an explicit default value. A call site binds to the declared type, so that member's default
  applies whatever the override says, and `MA0061` demands the override keep it — flagging it would
  leave no edit that satisfies both rules. The default is reported once, on the member that chose
  it. Compare an override only with the member it directly overrides, not the root of the chain;
  an implementation of several interface members at once is exempt if any of them defaults. An
  override or implementation that *introduces* a default its member lacks is still flagged, and
  so is an interface member redeclared on a derived interface, which is a declaration rather than
  an implementation.

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
| Override of `Stream.ReadAsync(Memory<byte>, CancellationToken = default)` keeping `= default` | 0 — inherited default |
| Implicit implementation of `I.M(CancellationToken ct = default)` keeping `= default` | 0 on the implementation; 1 on `I.M` |
| Explicit implementation `I.M(CancellationToken ct = default)` keeping `= default` | 0 on the implementation; 1 on `I.M` |
| Override keeping the default of a base `RunAsync(CancellationToken ct = default)` in the same source | 0 on the override; 1 on the base |
| Implementation of a generic `I<T>.M(T item, CancellationToken ct = default)` keeping `= default` | 0 on the implementation; 1 on `I<T>.M` |
| `interface J : I { Task I.M(CancellationToken ct = default) => …; }` (default implementation) | 0 on `J`'s; 1 on `I.M` |
| Half-typed override or implementation whose arity disagrees with its member | no crash |
| Override of an abstract `RunAsync(CancellationToken ct)` adding `= default` | 1 diagnostic |
| Override adding `= default` to a middle override that dropped the root's default | 1 on the leaf; 1 on the root |
| `interface J : I { new Task M(CancellationToken ct = default); }` | 1 on `J.M`; 1 on `I.M` |
| One method implementing `IPlain.M(CancellationToken ct)` and `IDefaulted.M(CancellationToken ct = default)`, keeping `= default` | 0 on the implementation; 1 on `IDefaulted.M` |

### 3.3 `APB0003` — Parameter names should match the implemented interface member

| | |
|---|---|
| Category | `Naming` |
| Default severity | `Warning` |
| Code fix | **Yes** (section 3.4) |
| Message | `Parameter '{0}' should be named '{1}' to match {2}` |

`{0}` is the actual name, `{1}` the interface's name, `{2}` the interface member as
`InterfaceName.MethodName`.

**Rationale.** `CA1725` requires this for base-*class* overrides. It reports some interface
implementations too, but not consistently: with its default `api_surface` it misses explicit
implementations, and it accepts a method implementing several interfaces if the name matches any
one of them. The package ships a suppressor, `APBS0001`, that hides `CA1725` on interface
implementations (ADR-0005), so this rule is the single, stricter report — so
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

### 3.5 `APB0004` — Put an expression body's arrow at the end of the declaration line

| | |
|---|---|
| Category | `Style` |
| Default severity | `Warning` |
| Code fix | **Yes**, with fix-all |
| Message | `Move '=>' to the end of the previous line` |

Added in 1.1.0, as a minor bump under ADR-0004.

**Rationale.** A wrapped expression-bodied member ends its declaration line with `=>` and puts the
expression on the next line:

```csharp
public void SomeFunction(string aParameter) =>
    the.thing.that.this.does.with.aParameter;
```

so the line that declares the member also says it has an expression body. Nothing else enforces the
layout: `IDE2006` enforces the opposite one, and `dotnet format whitespace` has no option for it.

**Flags** an `ArrowExpressionClauseSyntax` whose `=>` token is the first token on its line, meaning a
line break separates it from the previous token, with or without blank lines, comments or directives
in between. Report at the **`=>` token**. That covers methods, constructors, finalizers, operators,
conversion operators, properties, indexers, accessors and local functions.

**Must not flag:**

- An arrow at the end of the declaration line.
- A single-line member, `int M() => 1;`.
- A lambda's `=>`, however it is wrapped. Lambdas are out of scope; a lambda rule would get its own ID.
- A switch-expression arm's `=>`, however it is wrapped.

Neither of the last two is an `ArrowExpressionClauseSyntax`, so an analyzer registered on that node
kind never sees them.

**Code fix.** Title `Move '=>' to the end of the previous line`. One trivia edit in one document:
` =>` goes directly after the previous token, and every piece of trivia that sat between that token
and the arrow now follows the arrow. The expression keeps its indentation and comments keep their
place relative to the code, so a trailing comment stays inline:

```csharp
int M() // why
    => x;
```

becomes

```csharp
int M() => // why
    x;
```

Moving such a comment onto its own line is a separate rule (#71). Fix-all uses the batch fixer,
which is safe here, unlike §3.4, because no two fixes touch the same tokens.

**No fix across a directive.** When any preprocessor directive (`#if`, `#else`, `#region`, …) lies
between the previous token and the arrow, the diagnostic is still reported but no fix is offered,
and fix-all skips it. Moving the arrow across a directive changes which conditional region it
belongs to:

```csharp
int M()
#if DEBUG
    => 1;
#else
    => 2;
#endif
```

Only the active branch is parsed, so this reports one diagnostic, on that branch's arrow; moving it
up would leave the other branch's arrow stranded under the `#if`.

**Help link.** The help link of every `APB` rule, this one included, lands on the `consuming.md`
heading "The custom rules". Until 2.1.0 that heading read "The three custom rules", and every help
link shipped before then points at its anchor, `#the-three-custom-rules`, which no longer exists, so
those links open `consuming.md` at its top. An HTML anchor could keep them landing, but
`consuming.md` is also the nuget.org page, and nuget.org does not render raw HTML.
From 2.1.0 a help link names the guide at the commit its package was built from, so a later
heading change no longer strands it.

**Required tests**

| Input | Expected |
|---|---|
| `int M()` then `=> 1;` on the next line | 1 diagnostic, on `=>` |
| The same arrow-first layout on a constructor, finalizer, operator, conversion operator, property, indexer, `get` accessor and local function | 1 diagnostic each |
| `int M() =>` then `1;` | 0 |
| `int M() => 1;` | 0 |
| An arrow-first lambda | 0 |
| An arrow-first switch-expression arm | 0 |
| A wrapped parameter list, arrow first on the line after `)` | 1 diagnostic; fixed to `) =>` |
| `int M() // why` then `=> x;` | fixed to exactly `int M() => // why` then `x;` |
| A comment line between the declaration and the arrow | the comment ends up after `=>`, still above the expression |
| The `#if DEBUG` / `#else` example above | 1 diagnostic, no fix offered |
| The same example with `DEBUG` defined | 1 diagnostic, on the other arrow, no fix offered |
| The arrow alone on its line, the expression on the line below | fixed to `int M() =>` then the expression, with no blank line left behind |
| A `#region` between the declaration and the arrow | 1 diagnostic, no fix offered |
| An arrow-first member whose expression is a raw string literal | 1 diagnostic; fixed, the literal unchanged |
| Several arrow-first members in one document | fix-all fixes every one in a single pass |

### 3.6 `APB0005` — Put a comment on its own line above the code it describes

| | |
|---|---|
| Category | `Style` |
| Default severity | `Warning` |
| Code fix | **Yes**, with fix-all (section 3.7) |
| Message | `Move this comment onto its own line above the code` |

Added in 1.1.0, as a minor bump under ADR-0004.

**Rationale.** A comment explains the code below it, which is where a reader looks for it. A trailing
comment is read after the code it was meant to explain, and long lines push it out of sight. The
house comment policy in `docs/consuming.md` is the wider statement; this rule enforces only the
position of a comment, never whether it is justified.

**Flags** a single-line comment (`//`) with a token of code before it on the same line, whatever that
token is: `;`, `,`, `)`, `{`, `=>` or any other. Report at the comment.

**Must not flag:**

- A `//` comment on its own line.
- Block comments (`/* … */`) wherever they sit, including inline uses such as `Call(/* retries */ 3)`
  and `/*lang=json*/"…"`.
- Documentation (`///`, `/** */`).
- A comment on a preprocessor directive line, such as `#endif // DEBUG` or
  `#pragma warning disable CA1234 // reason`.
- A comment whose text, after the `//` and any whitespace, starts with `NOSONAR`, `ReSharper `,
  `lang=` or `language=`. These are tool directives whose position matters. There is no other
  content-based exemption.

**Required tests**

| Input | Expected |
|---|---|
| A trailing `//` after a statement `;`, a field `;`, a list item `,`, a `)`, a `{` or an `=>` | 1 diagnostic each, on the comment |
| A `//` comment on its own line | 0 |
| A trailing `/* */`, or `Call(/* retries */ 3)` | 0 |
| `///` documentation | 0 |
| `#endif // X`, `#pragma warning disable … // reason` | 0 |
| A trailing comment starting `NOSONAR`, `ReSharper `, `lang=` or `language=` | 0 |
| A trailing comment after the closing line of a multi-line raw string literal | 1 diagnostic |

### 3.7 The `APB0005` code fix

Title: `Move this comment onto its own line above the code`. Fix-all is supported, through the batch
fixer: each fix edits the trivia of the tokens on its own line only, so fixes never overlap.

Find the first token on the line the comment trails. Remove the comment, and the whitespace before
it, from the end of the line, keeping the line break. Insert the comment on a new line directly
before that first token, at that token's indentation, adding no blank line. Trivia already leading
that token, such as documentation, own-line comments or blank lines, stays above the hoisted comment.

This:

```csharp
var users = await _dbContext
    .Users()
    .Where(user => user.Roles.Contains("Admin")) // Admin users
    .Where(user => user.IsActive) // Active users
    .ToListAsync(cancellationToken);
```

fixes, in one fix-all pass, to:

```csharp
var users = await _dbContext
    .Users()
    // Admin users
    .Where(user => user.Roles.Contains("Admin"))
    // Active users
    .Where(user => user.IsActive)
    .ToListAsync(cancellationToken);
```

**No fix** is offered when the trailed line does not begin with a token of its own, such as the
closing line of a multi-line raw string literal, or a line opened by a block comment
(`/* a */ x; // b`), where the hoisted comment would land after the block comment and still trail
it. The diagnostic is still reported, and fix-all skips it.

**Required tests**: `x = 2; // Green` fixes to `// Green` above `x = 2;` at the same indentation; a
list item's comment is hoisted inside its collection initializer; a documented declaration's
comment lands between the documentation and the declaration; `int M() => // why` followed by `x;`
fixes to `// why`, `int M() =>`, `x;`; the example above, and a document with several trailing
comments, are fixed in one fix-all pass; and the raw-string case offers no fix.

### 3.8 `APB0006` — Do not nest conditional expressions

| | |
|---|---|
| Category | `Style` |
| Default severity | `Warning` |
| Code fix | **None** |
| Message | `Unnest this conditional expression into an if statement or a switch expression` |

Added in 2.1.0, as a minor bump under ADR-0004. It covers SonarQube's `S3358`; ADR-0008 records why
a rule of this package's own was chosen over `MA0240` with a shipped `BannedSyntaxes.txt`.

**Rationale.** A conditional expression inside another's condition or branches makes the reader
hold every condition at once to know which value is chosen. An `if` statement or a switch
expression names each case on its own line.

**Flags** a conditional expression (`a ? b : c`) that sits anywhere inside the condition, true
branch or false branch of another conditional expression, looking through parentheses, casts,
invocations, operators, switch expressions and any other expression between them. Report at the
inner conditional, once per nested conditional, so a three-deep chain reports twice.

**Must not flag:**

- A conditional inside a lambda, anonymous method or local function body, even when that function
  sits inside a conditional: the body is a scope of its own. Nesting *within* that body is judged
  afresh, so a conditional nested in another inside the same lambda is still reported.
- `?.`, `??`, `??=` and switch expressions. None is a conditional expression, so none reports, and
  none counts as the outer conditional of a nesting.
- Sibling conditionals in one expression, such as `(a ? 1 : 2) + (b ? 3 : 4)`.

**No code fix.** Unnesting means choosing between an `if` statement and a switch expression, and
the choice depends on what the surrounding code reads best as.

**Required tests**

| Input | Expected |
|---|---|
| `a ? 1 : b ? 2 : 3`, and the same nesting in the true branch and in the condition | 1 diagnostic, on the inner conditional |
| The inner conditional wrapped in a cast, an argument, an arithmetic operator or an array element | 1 diagnostic |
| `a ? 1 : b ? 2 : c ? 3 : 4` | 2 diagnostics, one per inner conditional |
| A lone conditional, and sibling conditionals | 0 |
| A conditional in an expression lambda, a statement lambda, an anonymous method or a local function inside a conditional | 0 |
| A nested conditional inside a lambda that itself sits in a conditional | 1 diagnostic, on the conditional nested within the lambda |
| `??`, `?.`, `??=` or a switch expression inside a conditional, and a conditional inside each of `??`, `?.` and a switch expression | 0 |
| A conditional in a switch-expression arm inside a conditional | 1 diagnostic |

---

## 4. Package layout and delivery

### 4.1 Projects

| Path | TFM | Purpose |
|---|---|---|
| `src/Aprbrown.Analyzers/` | `netstandard2.0` | The `DiagnosticAnalyzer`s and the `APBS0001` suppressor |
| `src/Aprbrown.Analyzers.CodeFixes/` | `netstandard2.0` | The `APB0003`, `APB0004` and `APB0005` `CodeFixProvider`s |
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
`README.md`. `PackageProjectUrl` and `RepositoryUrl` point at the public repository; `HelpLinkUri`
points one level deeper, at [`consuming.md`](consuming.md)'s custom-rules section, because it is what an
IDE opens from a squiggle and a consumer clicking `APB0001` wants the rule explained rather than the
repository root. None of the three is written in the source: the build reads the repository from the
git `origin` remote, and the help link names the guide at the commit being built
([ADR-0009](adr/0009-help-links-name-the-guide-at-the-commit-built.md)).

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

**The properties must come from package props specifically.** The props phase runs
`Directory.Build.props`, then package props, then the SDK's own defaults, then the project
file body — in that order. `Microsoft.NET.Sdk.props` line 114 assigns
`TreatWarningsAsErrors=false` under the same `Condition="'$(TreatWarningsAsErrors)'==''"` guard
this package uses: after both of the first two, but *before* the project file body. So an
`<Import>` at the top of a `.csproj` is too late in the other direction — the SDK has already
satisfied the guard, so the package's own `Condition` never fires and the property stays
`false`, producing warnings where errors were intended. Package props run early enough; a
`.csproj` import does not.

**Verified this session, end to end from a real `.nupkg`** — both properties arrive, and a consumer
overrides them:

| Consumer configuration | Result for a `CA` violation |
|---|---|
| Package defaults only | **error** |
| `Directory.Build.props` sets `TreatWarningsAsErrors=false` | **warning** — consumer wins |
| `Directory.Build.props` sets `true` | error |

`Directory.Build.props` is imported *before* package props, so a consumer wins by **pre-empting**
the default rather than overwriting it: it sets the property first, and the package's
`Condition="'$(Prop)'==''"` then finds a value already there and stands down. The overridability is
therefore the `Condition`'s doing and nothing else's. Drop it and the package would mandate the
property, because package props run last of the two — see correction 15, which records the earlier
inverted claim.

`Nullable` and `ImplicitUsings` do **not** travel — they are project-shape decisions, not part of how
the ruleset functions.

---

## 5. Build, release and versioning

ADR-0003 and ADR-0004 settle these; the deliverables are listed here so nothing is missed.

- `.github/workflows/ci.yml` — PR and `main` pushes. Build, test, pack a smoke version, and run
  `verify-package.sh` against it.
- `.github/workflows/sonar.yml` — `main` pushes and dispatch only, on the repository's own homelab
  runner (ADR-0006). Publishes a SonarQube analysis with coverage; a signal, never a gate.
- `.github/workflows/release.yml` — `v*` tags only; the sole workflow holding `id-token: write`.
  Packs with `-p:Version=${GITHUB_REF_NAME#v}` — the tag is the only source of a version number. No
  `NUGET_API_KEY` exists; `NuGet/login@v1` exchanges an OIDC token for a one-hour single-use key.
  Must not delegate its build via `workflow_call`; use a local composite action if sharing is needed.
- **Approval gate.** `release.yml`'s publish job declares `environment: release`, with the repository
  owner as a required reviewer and **`Prevent self-review` left off** — verified as GitHub's default,
  so a sole maintainer can approve their own deployment. This closes ADR-0003's open point: the push
  is unrevocable and a mistyped tag spends a version number permanently, so one click is cheap
  insurance. The nuget.org trusted publishing policy is scoped to the same environment.
- `scripts/verify-package.sh <path-to.nupkg>` — takes an already-packed package, places it alone in
  a temp feed, restores `tests/Fixture.Consumer` against it, builds, and asserts an exact diagnostic
  set. It never packs: the caller does, so `release.yml` gates the file it pushes (ADR-0003
  amendment 3). Assertions in section 6.
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
| A `CA` violation fires | The ~~27~~ **29** enumerated `CA` IDs survive the blanket — see section 10 correction 8 |
| A `Meziantou` violation fires, with `Meziantou.Analyzer` referenced by the fixture | Config-without-assembly binds when the assembly arrives |
| A member-ordering violation fires, with `StyleCop.Analyzers` referenced by the fixture | The same, for the four `SA` IDs |
| An IDE naming violation fires | Enumeration-by-ID works — these are silently unenforced otherwise |
| A *second* IDE naming violation fires, on a private static named `_created` | Naming rules resolve by **specificity**, not declaration order. The general private-fields rule is satisfied by that name and only the specific private-statics rule objects, so this row is the one that makes the source repository's declaration-order comment demonstrably false rather than merely unported |
| A missing accessibility modifier fires, twice | `IDE0040` is enumerated and reachable; the second is on an *interface member*, which is exactly what `always` buys over `for_non_interface_members` |
| `IDE0022` fires | The raised severity of the seven expression-bodied IDs survives the trip — an error in the `TreatWarningsAsErrors=true` build, a warning without it |
| `IDE0003` does **not** fire | Not a seal assertion but a pinned limitation — it is IDE-only, see section 10 correction 12. The one assertion in the file that should eventually break |
| The config totals 150 enumerated rules | No rule arrived outside a tier; the per-tier counts cannot catch that on their own — section 10 correction 13 |
| `MA0004` does **not** fire | The sole deliberate exclusion stays excluded |
| `CA2007` does **not** fire | The seal holds; a genuinely default-off rule stayed off |
| A `CA1707`-triggering name does **not** fire | No category re-enable admitted it — but see section 10 correction 9: `CA1707` is default-on at `Hidden`, so this row proves less on its own than "default-off rules stayed off" claimed, and `CA2007` above carries that weight |
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

Two places where that needs more than the snippet:

- **A project cannot be its own analyzer.** The `APB` rules gate `Aprbrown.Analyzers.CodeFixes`, the
  packaging project and the tests from current source, via `ProjectReference` +
  `OutputItemType="Analyzer"`. `src/Aprbrown.Analyzers` instead takes a `PackageReference` to the
  **last published** `Aprbrown.Analyzers`, so it is held to the `APB` rules one release behind the
  source. Its `PackageId` is `Aprbrown.Analyzers.Source`, so NuGet sees no self-cycle (`NU1108`); the
  reference is `PrivateAssets="all"`, so the projects that reference it, already running the current
  rules, do not report every diagnostic twice; and it is `ExcludeAssets="build;buildTransitive"`, so
  the package's props do not add a second copy of the shipped config, which would unset every shared
  key (`MultipleGlobalAnalyzerKeys`). Only the analyzer project takes this reference. Dependabot
  (`.github/dependabot.yml`, allowed `Aprbrown.Analyzers` and nothing else) opens a pull request
  raising the pin after each release.
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

One case of that needs a sequence of its own: a **released `APB` rule with a false positive that fires
on the analyzer project's own code**. The analyzer project cannot build until the fix ships, and the
fix cannot ship until the analyzer project builds. The escape is a **temporary** deviation in the
local `.editorconfig`, scoped to the analyzer project's path, so the current-source rule still runs
on `CodeFixes` and the tests:

```ini
[src/Aprbrown.Analyzers/**.cs]
dotnet_diagnostic.APB0005.severity = none
```

It is removed in the Dependabot pull request that raises the pin to the release carrying the fix.
Rolling the pin back is not the escape: the repository otherwise stays on the shipped config exactly.

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
   - the `MA0051` path carve-out for EF `Migrations/`, and `MA0048 = none` **repository-wide** —
     not the `Pages/` + `Migrations/` glob this step originally prescribed, which measured 135
     violations (section 10 correction 14).
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
- **Adoption cost on naming is zero fleet-wide; on accessibility it is not** — private-field
  `_camelCase` and private-static `PascalCase` measured 0 across all six repositories. `IDE0040`
  did not (correction 16): expect it wherever a repository's hand-written interfaces leave
  `public` off their members. Other violations will concentrate in the Meziantou and `CA` tiers.

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
and `CA` tiers: naming measured **zero across all six repositories**, so that axis costs nothing
anywhere. Accessibility does not share that zero — `IDE0040` costs one fix per interface member
written without `public` (correction 16).

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
   `consuming.md` said "All 103 default-on rules" and inherited the same looseness; its rule table now
   states the narrower criterion — every rule enabled by default *at a build-gating severity* — so the
   two documents no longer differ on what "default-on" means.
7. **The call-site "trio" is a duo.** Sections 2.3 and 3.2 pair `APB0002`'s declaration half with
   `CA2016` + `MA0040` + `MA0032` at the call site. `MA0040` is enabled by default at `Info`, so
   correction 6 excludes it from the 103 and it is **not** enumerated — it stays off under the
   blanket. Only `MA0032` is in the shipped config today; `CA2016` arrives with step 2. Admitting
   `MA0040` would be a third deliberate departure from the sweep and a ruleset decision in its
   own right, so it is recorded here rather than taken silently.
8. **The `CA` tier is 29 rules, not 27.** Section 2.3 step 2 says "every `CA` rule the .NET
   `10.0.110` SDK enables by default at `AnalysisMode=Default` — 27 rules". Measured the same way
   correction 6 measured Meziantou — load the two `NetAnalyzers` assemblies the SDK ships,
   instantiate every exported analyzer, read `IsEnabledByDefault` and `DefaultSeverity` off each
   descriptor: 317 rules ship, **171** are enabled by default, and **29** of those default to a
   build-gating severity — **27** at `Warning` and **two** at `Error`, `CA1856` and `CA2252`. The
   stated 27 is exactly the `Warning` subset.
   The two `Error` rules are carried rather than dropped to reach the stated count, because step 2
   is the same sentence that says to record each rule at the severity the vendor assigns and to
   read that value rather than assume it — dropping them would flatten precisely the judgement it
   asks to be preserved, and would leave two error-severity rules a plain `dotnet build` enforces
   today silently off across the fleet. It is also what correction 6 did with Meziantou's three
   `Error` rules, which are inside the 103 for the same reason.
   `AnalysisMode=Default` itself subtracts nothing here: the SDK's `analysislevel_10_default`
   config turns off exactly one rule, `CA1516`, which defaults to `Info` and so falls outside the
   gating filter regardless. `consuming.md`'s "The 27 rules the SDK enables by default" is
   corrected to 29 in place, since that row describes what the package actually ships.
9. **`CA1707` is default-*on* at `Hidden`, not default-off.** Section 2.3 step 2, ADR-0002
   decision 4 and correction 2 above all call it default-off, and it is not: measured against SDK
   `10.0.110` it is `IsEnabledByDefault = true` with `DefaultSeverity = Hidden`. Every conclusion
   drawn from it stands — a `Naming` category re-enable still sets it to `warning` and it still
   fights the house `_camelCase` convention, which is what those passages were demonstrating — but
   the mechanism keeping it off is the blanket plus this file never naming it, not upstream's
   enabled flag. `CA2007` is the genuinely default-off half of that pair
   (`IsEnabledByDefault = false`, `Warning`), and it is the one carrying the weight in the smoke
   test's silence assertions.
10. **`CA2016` does not arrive with step 2 either.** Correction 7 says only `MA0032` is enumerated
    today and `CA2016` arrives with the `CA` tier. It does not: `CA2016` is enabled by default at
    `Info`, so correction 8's gating filter excludes it exactly as correction 6's excluded
    `MA0040`. The call-site companion to `APB0002`'s declaration half is `MA0032` alone. Admitting
    `CA2016` would be a deliberate departure from the sweep and a ruleset decision in its own
    right, so — like `MA0040` — it is recorded here rather than taken silently.
11. **The sealed allowlist has one measured leak, and it is `SA0001`.** The blanket is a bulk
    `dotnet_analyzer_diagnostic.severity` entry, and a bulk entry has no syntax tree to resolve
    options against for a diagnostic reported with **no location** — so it never reaches one.
    `SA0001` ("XML comment analysis disabled") is reported per-project with no location and
    therefore fires at the vendor's `Warning` in any project that does not generate a
    documentation file, despite never being enumerated. This repository already hit it in its own
    test project before the StyleCop tier shipped and answered it with a local deviation; every
    consumer now meets it the moment they add the prescribed `StyleCop.Analyzers` reference, so
    `consuming.md` documents it and §2.4 registers it.
    It could be repaired in one line — an explicit `dotnet_diagnostic.SA0001.severity = none` *does*
    reach a no-location diagnostic where the bulk entry does not, which is exactly how this
    repository's own `.editorconfig` silences it. That line is not taken here, and the honest reason
    is scope rather than principle: #17 specifies this tier as exactly `SA1201`–`SA1204` and "no
    other `SA` rule appears", and the smoke test asserts that set exactly. Repairing the leak inside
    the shipped config is a ruleset decision in its own right — a rule the package names on every
    consumer's behalf — and under ADR-0004 it belongs in its own ticket, weighed against the cost it
    currently pushes onto every consumer: one line of onboarding boilerplate, forever.
12. **`IDE0003` cannot fire in a build, so one house convention is IDE-only.** Section 2.3 step 4
    enumerates `IDE0003` and `IDE0009` alongside the four `dotnet_style_qualification_for_*`
    options, and every other rule in that step gates a build. This pair does not. `IDE0003`
    ("remove `this` or `Me` qualification") is one of the Simplify-family rules Roslyn runs only in
    the IDE; a command-line build never reports it, `EnforceCodeStyleInBuild` notwithstanding.
    Measured on SDK `10.0.110` against `this._size` with `dotnet_style_qualification_for_field =
    false`, in the same compilation where `IDE1006` and `IDE0040` both fired: `IDE0003` silent.
    `IDE0009`, the add-qualification half driven by the same options, *does* fire in a build — but
    only when the options say `true`, which the house ruleset never does, so it is enumerated and
    unreachable in practice too.
    Both stay in the shipped config. In an editor the pair works, which is where a developer meets
    a `this.` they did not mean to write, and the alternative is dropping a house convention over a
    tooling gap. What changes is the claim: "no `this.` qualification" is enforced by the IDE, not
    by the build, and `docs/consuming.md` says so. The smoke test pins the silence with `IDE1006`
    and `IDE0040` as its control, and its failure message says what to do on the day an SDK fixes
    it — this is the one assertion in that file which *should* eventually break.
13. **The shipped config is 150 rules, not "~131".** Section 1 and section 2.2 both give "~131",
    written before any tier was measured. The tiers as derived are 3 `APB` + 103 Meziantou + 29
    `CA` + 4 StyleCop + 11 IDE = **150**. Every part of that gap is already accounted for by an
    earlier correction or by step 4 itself: correction 6 fixed the Meziantou criterion, correction
    8 carried the `CA` tier's two `Error` rules, and step 4's eleven IDs were never given a count to
    miss. The estimate is left in place rather than edited, because it is what the design believed
    before measurement and the arithmetic above is the more useful record. `docs/consuming.md` and
    `GLOSSARY.md` are corrected in place, since both describe what the package actually ships.
    The smoke test asserts the total alongside each tier's own count — a per-tier count cannot
    catch a rule that arrived belonging to no tier at all.
14. **`Mixologist`'s `MA0048` entry is a repository-wide deviation, not a path carve-out.** Section 8
    step 4 prescribed `MA0048 = none` for `Pages/` and EF `Data/Migrations/` only, and
    `consuming.md` presented the same glob as the shape a consumer should expect. Implemented
    literally while onboarding against `1.0.0-preview.1` (#22), `Mixologist` built with **135
    `MA0048` errors** and zero of any other rule — so the delivery mechanism was clean and the
    prescribed deviation set was wrong. Restoring the repository-wide switch-off `Mixologist` held
    before onboarding returns it to 0/0.
    `MA0048` is off there for three reasons and only two are path-shaped: Razor page models named
    for their `.cshtml`, timestamp-prefixed EF migrations, and — the third — a house convention that
    a record existing only to be returned lives in the file of the type returning it
    (`SourceEditor.cs` declares `SourcePick`, `BookFields` and `SourceMergeResult`; `RecipeStore.cs`
    declares `RecipeView`, `RecipeSeed` and `RecipeListRow`), with an interface sitting alongside its
    single implementation. That third reason is about code shape rather than folders, runs through
    `Recipes/`, `Sources/` and `Ingredients/` alike, and so has no glob that expresses it. Section 8
    was written before the `Sources/` and typed-source work landed in `Mixologist`, which is
    probably why only the path-shaped reasons were visible then.
    Nothing about the package changes — `MA0048` stays enumerated at `warning`, and this is a
    guidance defect, not a delivery one. `consuming.md` now presents the glob as the path-shaped
    example and names the repository-wide case beside it, for the same reason the `tests/**`
    carve-out is marked not optional: section 9 makes any diagnostic in the canary evidence the
    package is wrong, and a consumer copying a defective snippet cannot tell a guidance defect from
    a real violation. Its standing advice to prefer a narrow glob is kept — an exception with no
    path to narrow to is the documented exception to it, not a licence to switch rules off widely.
15. **Package props are imported *after* `Directory.Build.props`, not before.** Section 4.3,
    `build/Aprbrown.Analyzers.props`'s own comment and `consuming.md`'s override advice all said the
    reverse, and attributed the two MSBuild properties' overridability to that ordering. Measured on
    SDK `10.0.110`, in `/usr/share/dotnet/sdk/10.0.110/Current/Microsoft.Common.props`: line 34
    imports `$(DirectoryBuildPropsPath)`, line 74 imports
    `$(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props` — the NuGet-generated props that
    pull in `build/<PackageId>.props`. Confirmed by evaluation, with a `Directory.Build.props` and
    an `obj/*.nuget.g.props` each appending its name to a marker property, which came out
    `Directory.Build.props`, then package props, then the project body. The delivery research
    already had this right (`docs/research/2026-07-21-ruleset-delivery-and-precedence.md`, test D);
    only the spec and the two docs inverted it. All three are corrected in place, `consuming.md`
    included, since a consumer reads its override advice to decide where to put the property.
    Nothing about the package changes and §4.3's verification table stands unedited — the consumer
    does win, but by **pre-empting** the default rather than overwriting it. So does the
    `.csproj`-`<Import>` conclusion, which rests on the SDK's own default in
    `/usr/share/dotnet/sdk/10.0.110/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.props` line 114
    landing after both and before the project body.
    What the inversion put at risk is the `Condition`. Under the ordering as documented, the
    `Condition` would be redundant belt-and-braces — a later `Directory.Build.props` would win
    regardless — and an author reasoning from that could delete it as noise. Under the real
    ordering it is load-bearing and nothing else does its job: measured with the `Condition` removed
    and a consumer `Directory.Build.props` setting `TreatWarningsAsErrors=false`, the property
    evaluates to `true`. The package would silently mandate. #39's own rationale draws the opposite
    consequence — that a `Condition`-less package still could not mandate — and that is the same
    inversion one step further on; the measurement above is the record.
    One neighbouring sentence is repaired for the same reason. §4.3 said a `.csproj` `<Import>`
    "sets it and is then silently overwritten", and nothing overwrites it: line 114 carries the
    same `Condition` guard, so the SDK's `false` lands first and the package's guard then never
    fires — hazard H1 in the delivery research, stated there correctly. The conclusion is
    untouched; only the mechanism beside it was wrong, and making the ordering explicit is what
    exposed it.
16. **`IDE0040` does not measure zero; the probe that said so discarded its true positives.**
    Sections 8 and 9 said naming *and accessibility* cost nothing fleet-wide, with `IDE0040` among
    the three rules measured at 0 across all six repositories. Measured against live builds:

    | Repository | `IDE0040` | private-field `_camelCase` | private-static `PascalCase` |
    |---|---:|---:|---:|
    | `Mixologist` (#22) | 0 | 0 | 0 |
    | `FPL_Helper/web` (#24) | 20 | 0 | 0 |
    | `EveIndustryTools` (#25) | 259 | 0 | 0 |

    Every hit has one shape: an interface member declared without an explicit `public`. The zero
    came from #5's grep probe, which counted type and member declarations lacking a modifier and
    threw interface members away as false positives — "interface members (which correctly take no
    modifier)". Under `dotnet_style_require_accessibility_modifiers = always`, which the shipped
    config sets deliberately and the fixture asserts, those are the true positives. The probe and
    the ruleset disagreed about what counts, and the probe's answer is the one that reached the
    docs. A repository with few hand-written interfaces scores near zero and reveals nothing, which
    is why the number grew with each repository rather than settling.
    The two naming claims stand: both rules held at zero on every repository measured, including
    `EveIndustryTools`'s 1,307 `.cs` files. Nothing about the package changes — `always` stays, and the
    fix is mechanical and purely additive (`dotnet format style --diagnostics IDE0040`). Sections 8
    and 9 are corrected in place, because both are guidance a reader acts on rather than a record
    of what the design believed, and `consuming.md`'s build-and-fix step now names `IDE0040` as the
    exception and points to its existing note on the option.
