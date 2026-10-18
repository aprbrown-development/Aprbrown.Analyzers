# Aprbrown.Analyzers

One .NET coding standard, delivered as a NuGet package.

It ships two things: five custom Roslyn analyzers (`APB0001`–`APB0005`), and — the larger half — a
configuration that decides which **209 rules** from `Meziantou.Analyzer`, `StyleCop.Analyzers` and
the .NET SDK's own `CA` and `IDE` analyzers are switched on.

There is one universal ruleset. No profiles, no presets, no tiers to pick from, nothing to configure.
That is deliberate: it encodes a single house style rather than trying to suit every repository. Where
you disagree, you override it in your own `.editorconfig`, which always wins.

By default it also switches `TreatWarningsAsErrors` on, so **adopting this package will fail builds
that currently pass**. That is the point of it, and [Installing](#installing) is written on the
assumption that you want that. If you would rather start with warnings, see
[Making it advisory](#making-it-advisory).

## Requirements

| | |
|---|---|
| Language | **C# only.** No Visual Basic or F# analyzers are shipped. |
| Toolchain | .NET SDK **10.0.400** or newer, or Visual Studio 2026 **18.9** or newer — anything hosting Roslyn **5.9** or newer. The analyzers are built against Roslyn 5.9, and a host cannot load an analyzer built against a Roslyn newer than its own: an older one fails the build with `CS9057`. |
| Derived against | .NET SDK 10. The `CA` tier is enumerated from the rules that SDK switches on by default, so on an older SDK a few of those IDs will not exist yet. Unknown IDs are inert, so nothing breaks — the tier is just slightly smaller. |
| Target frameworks | Any. Nothing in this package constrains what your projects target. |

It is a development-only dependency: no assembly from it reaches your build output, and it is not a
dependency of packages you publish.

## Installing

### 1. Reference three packages

Put these in the repository's `Directory.Build.props`, not in an individual project, so they apply to
every project you have now and every one you add later.

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Aprbrown.Analyzers"  Version="1.0.0"          PrivateAssets="all" />
    <PackageReference Include="Meziantou.Analyzer"  Version="3.0.305"        PrivateAssets="all" />
    <PackageReference Include="StyleCop.Analyzers"  Version="1.2.0-beta.556" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Three references, not one. `Aprbrown.Analyzers` deliberately takes **no dependency** on the other two:
it configures their rules without bundling their assemblies, so your repository owns those versions and
can pin whatever it likes. A configuration entry for an analyzer you have not installed is inert, so
nothing breaks if you drop one — those rules simply stop being enforced. The .NET SDK's `CA` and `IDE`
analyzers need no reference at all; they arrive with the SDK.

`PrivateAssets="all"` stops the analyzers flowing on to *your* consumers. It does not cover the project
that declares the reference, which is why the references belong in a repository-wide
`Directory.Build.props` rather than in one project — every project needs its own.

### 2. Add these `.editorconfig` entries

**This step is not optional.** These are exceptions that cannot travel inside a NuGet package, because
they are scoped to file paths and a package cannot know your repository's layout. Without them a normal
repository will not build.

Paste this into the `.editorconfig` at your repository root, adjusting the globs to match where your
code actually lives:

```ini
# Required. StyleCop's "XML comment analysis disabled" fires once per project when no
# documentation file is generated, and the shipped configuration cannot reach it (see below).
[*.cs]
dotnet_diagnostic.SA0001.severity = none

# Required if you have tests. Test doubles hold values rather than injected dependencies,
# and throwing is the correct way to say "not part of this fake's contract".
[tests/**.cs]
dotnet_diagnostic.APB0001.severity = none
dotnet_diagnostic.MA0025.severity = none

# If you have Razor page models or EF Core migrations: both are named by convention
# rather than after their type, and migrations are generated and long.
[{Pages,Data/Migrations}/**.cs]
dotnet_diagnostic.MA0048.severity = none

[Data/Migrations/**.cs]
dotnet_diagnostic.MA0051.severity = none
```

Then read the note on `MA0048` below, because for many repositories the glob above is the wrong shape.

#### Why each of these is needed

**`SA0001`** is the one rule the shipped configuration's everything-off line cannot switch off. It is
reported once per project with no source location, and a blanket severity entry has no file to attach
itself to — so it arrives at StyleCop's own `warning`, which is an error under `TreatWarningsAsErrors`,
even though this package never enables it. Setting
`<GenerateDocumentationFile>true</GenerateDocumentationFile>` is the alternative fix. Test projects are
where you will meet it first.

**`APB0001` in tests.** Across the six repositories this ruleset was derived from, roughly 84% of
primary-constructor usage was the test-double idiom — `private sealed class StubShopper(ShoppingBoard
board) : IShopper`. `MA0025` ("implement the functionality instead of throwing") is the matching
exemption: a fake that throws on the members it does not implement is behaving correctly.

**`MA0048` is often repository-wide rather than path-shaped, so treat the glob above as an example
rather than as the expected answer.** File-per-type is as much a convention about the *shape* of code as
about folders: a record that exists only to be returned, living in the file of the type that returns it,
or an interface sitting beside its single implementation, will fire anywhere in the tree and leaves no
folder to narrow to. On the first repository onboarded, the two-folder glob still left **135 `MA0048`
errors**, and only a repository-wide switch-off returned the build to zero. Decide which shape your
repository is before copying the glob:

```ini
[*.cs]
dotnet_diagnostic.MA0048.severity = none   # types declare their own return records
```

### 3. Build, and fix what it reports

Adoption is **big-bang**: install, fix the violations, done. There is no severity ratchet and no
baseline-suppression file to grandfather existing code in. Violations concentrate in the
`Meziantou.Analyzer` and `CA` tiers. The naming rules are usually free — `_camelCase` private fields
and `PascalCase` private statics were each measured at **zero violations across all six
repositories** the ruleset was derived from, and have stayed at zero on every repository onboarded
since.

`IDE0040` is the exception. Its hits are interface members declared without an explicit `public`, so
what it costs you scales with how many hand-written interfaces you have: the three repositories
onboarded so far scored 0, 20 and 259. The fix is mechanical and purely additive —
`dotnet format style --diagnostics IDE0040` — and the reason it fires at all is
[the note on `dotnet_style_require_accessibility_modifiers`](#the-rules).

**The performance tier is mostly mechanical.** 50 of its 53 rules ship a code fix, such as
`GetOrAdd(key, _ => value)` for `MA0242` or `Contains('#')` for `CA1847`. `dotnet format analyzers`
applies them, but only for rules whose severity it finds in an `.editorconfig`: the package's
configuration does not count. Add a `dotnet_diagnostic.<ID>.severity = warning` line for each rule
you want to sweep, run `dotnet format analyzers --diagnostics <IDs>`, then delete the lines.

**The StyleCop ordering tier has no code fix, so size it before you commit to the pass.**
Every `SA1201`–`SA1204` violation is a member moved by hand, or by a tool your repository supplies
itself; one of the three repositories onboarded so far had 149 in production code alone. To confirm
it, run this on a project with an ordering violation. It changes nothing and prints
`Unable to fix SA120x. No associated code fix found.` for each rule:

```sh
dotnet format analyzers --diagnostics SA1201 SA1202 SA1203 SA1204
```

To count what you would be taking on, build once with the package installed and count the ordering
diagnostics. Turning `TreatWarningsAsErrors` off for this one build stops a failing project from
hiding the ones that depend on it, and `--no-incremental` makes an up-to-date project report again.
The `sed` and `sort -u` count each violation once, although the build summary repeats it and a
multi-targeted project reports it once per target framework:

```sh
dotnet build --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -E 'SA120[1-4]' | sed 's/ \[.*//' | sort -u | wc -l
```

If your repository already has an `.editorconfig`, see
[Adopting into an existing configuration](#adopting-into-an-existing-configuration).

## What the package adds to your build

**Two MSBuild properties**, as defaults you can override:

| Property | Default | Why it ships |
|---|---|---|
| `EnforceCodeStyleInBuild` | `true` | Without it the `IDE` rules silently do not run at build time |
| `TreatWarningsAsErrors` | `true` | The ruleset is a gate, not a suggestion |

Both are part of how the ruleset *functions*, which is why they travel with it. Both are defaults rather
than mandates: setting either one in your own `Directory.Build.props` wins, because the package's
assignments are guarded to apply only when you have not set the property yourself.

`Nullable` and `ImplicitUsings` do **not** travel. Those are decisions about your project's shape, not
about code style.

**A configuration file** naming every enabled rule. It is a *baseline*: your own `.editorconfig`
outranks it, always. That is a documented guarantee of the compiler rather than an accident of file
layout — the package's file is injected as a global analyzer config that deliberately claims no
priority, so any entry of yours for the same rule beats it.

**One diagnostic suppressor**, `APBS0001`, which hides `CA1725` on interface implementations so that
`APB0003` reports them alone. It suppresses nothing else. See
[Switching the suppressor off](#switching-the-suppressor-off).

Nothing else. No runtime assembly, no `.props` you have to import by hand, no `.pdb`.

## The rules

| Tier | Count | What is on |
|---|---|---|
| `APB` | 5 | The five custom rules below |
| `Meziantou.Analyzer` | 106 | Every rule it enables by default *at a build-gating severity*, minus `MA0004`, plus `MA0032` |
| .NET SDK `CA` | 29 | The rules the SDK enables by default as build warnings or errors |
| Performance (`Meziantou.Analyzer` + `CA`) | 53 | 23 `Meziantou.Analyzer` and 30 `CA` rules whose rewrite keeps behaviour and costs less, raised to warning. [Mostly auto-fixable](#3-build-and-fix-what-it-reports) |
| `StyleCop.Analyzers` | 4 | `SA1201`–`SA1204` — member ordering only. This is not a StyleCop adoption. **No code fix**: [size it first](#3-build-and-fix-what-it-reports) |
| `IDE` / naming | 12 | `_camelCase` private fields, `PascalCase` private statics, explicit accessibility modifiers, no `this.` qualification, seven expression-bodied-member preferences, and `CA1725`: an override keeps its base declaration's parameter names, at every accessibility |

209 rules in total. **204 are warnings and five are errors.**

**`CA1725` checks every override, internal ones included**, because the configuration sets
`dotnet_code_quality.CA1725.api_surface = all`. That covers framework overrides: `Equals(object? obj)`,
not `Equals(object? other)`, and `Stream.Read(buffer, offset, count)`. It ships a code fix that renames
the parameter and every use of it, which `dotnet format analyzers --diagnostics CA1725` applies once
`dotnet_diagnostic.CA1725.severity = warning` is in your `.editorconfig` for the run. Interface
implementations are left to `APB0003`, [below](#the-three-custom-rules).

The **seven expression-bodied-member preferences** (`IDE0021`, `IDE0022`, `IDE0025`, `IDE0026`,
`IDE0027`, `IDE0053`, `IDE0061`) gate like every other warning.
`csharp_style_expression_bodied_methods` is `true`, not `when_on_single_line`, so a method whose
body is one statement spread over several lines is reported too. `dotnet format style` fixes every
case, but only acts on a rule whose `dotnet_diagnostic.<ID>.severity` it finds in an
`.editorconfig` — the package's configuration does not count — so add the seven
`dotnet_diagnostic.IDE00xx.severity = warning` lines to your root `.editorconfig` for the run and
delete them after:

```sh
dotnet format style --diagnostics IDE0021 IDE0022 IDE0025 IDE0026 IDE0027 IDE0053 IDE0061
```

Review the diff: where a comment sat above the single statement, the fix leaves it between `=>` and
the expression, and it reads better moved above the member. If your own `.editorconfig` sets one of
the `csharp_style_expression_bodied_*` options with an inline severity, such as
`csharp_style_expression_bodied_methods = when_on_single_line:suggestion`, delete the line: it
overrides the house setting.

The **five errors** are `MA0037`, `MA0039`, `MA0049`, `CA1856` and `CA2252`. These are the vendors' own
default severities, kept rather than softened, and they fail a build even if you turn
`TreatWarningsAsErrors` off.

`dotnet_style_require_accessibility_modifiers` is set to `always` rather than
`for_non_interface_members`, so an interface's members spell `public` out like anything else. That is the
one place in the naming and accessibility tier you are likely to meet friction.

**`this.` qualification is enforced by your editor, not by your build.** `IDE0003` (remove `this.`) is
one of the rules Roslyn only runs inside an IDE, so an unwanted `this.` shows up as a squiggle with a
one-key fix but will not stop a command-line build. `IDE0009`, the add-`this.` half, does gate. Every
other rule in the table above gates.

### The three custom rules

- **`APB0001` — no primary constructors on classes or structs.** Injected dependencies belong in an
  explicit constructor assigning readonly fields, so a type's dependency surface reads as one block.
  *Records are exempt* — positional parameters are the point of a record. No code fix.
- **`APB0002` — no default value on a `CancellationToken` parameter.** A defaulted token lets a caller
  drop cancellation without saying so, invisibly at the call site. `MA0032` polices the call site;
  this is the declaration half. An override or implementation keeping a default its base or
  interface member already declares is exempt, since `MA0061` requires it. No code fix.
- **`APB0003` — an interface implementation's parameter names must match the interface's.** `CA1725`
  requires this for base-class overrides, and reports some interface implementations too, but accepts
  a method implementing several interfaces if its name matches any one of them. The package therefore
  suppresses `CA1725` on interface implementations (suppression `APBS0001`), so a renamed interface
  parameter is reported once, by this stricter rule. An override that renames its base's parameter
  keeps its `CA1725` even if it also implements an interface. **Ships with a code fix**, which renames
  the symbol rather than the token, so every reference follows.
- **`APB0004` — a wrapped expression body's `=>` ends the declaration line.** `int M() =>` then the
  expression on the next line, not `int M()` then `=> expression`. Covers methods, constructors,
  operators, properties, indexers, accessors and local functions; lambdas and switch-expression arms
  are not covered. **Ships with a code fix** that supports fix-all, so one "Fix all occurrences in
  Solution" clears it. A comment that ended the declaration line stays on it, after the arrow. No fix
  is offered when a preprocessor directive sits between the declaration and the arrow; move that one
  by hand.
- **`APB0005` — a comment goes on its own line above the code it describes.** A `//` comment
  trailing code is reported, wherever it trails: after `;`, `,`, `)`, `{`, `=>` or anything else. Block
  comments (`/* … */`), documentation, comments on preprocessor directive lines, and tool directives
  starting `NOSONAR`, `ReSharper `, `lang=` or `language=` are exempt. **Ships with a code fix**,
  fix-all included, which moves the comment onto a new line directly above the line it trailed, at
  that line's indentation. Where the line does not start with a token of its own, such as after the
  closing line of a multi-line raw string or after a block comment that opens the line, the comment
  is still reported and no fix is offered. The rule follows from the
  [house comment policy](#the-house-comment-policy).

  `dotnet format analyzers --diagnostics APB0005` applies the fix across a repository, once
  `dotnet_diagnostic.APB0005.severity = warning` is in your `.editorconfig` for the run. Run it after
  `APB0004`'s fix: that one leaves a comment from the declaration line after the arrow, and this one
  moves it above the member.

The heading above still says three. It is the anchor every rule's help link points at, so renaming
it would break the links already shipped; it changes in the next major version.

### The house comment policy

- **Documentation** (XML doc) is welcome. It is written for the caller, and should be complete
  enough that an editor's tooltip explains the member without its body being read.
- **Comments** — any note that is not documentation — are rare by design. The code should explain
  itself: a member's name and parameters say what it does, and its body says how. A comment is
  justified only where the code cannot explain itself.
- A comment or documentation explains the code. It never refers to the decision, ticket, ADR or
  document section behind it.
- A justified comment sits on its own line above the code it describes, which is what `APB0005`
  enforces. A comment trailing a line of code is a **trailing comment**. Whether a comment is
  justified is a judgment for review, and no rule checks it.

### Two rules you might expect that are deliberately off

- **`MA0004` (`ConfigureAwait`)** — its rationale, that ASP.NET Core installs no synchronization
  context, is simply false for a class library. If your repository is all-web, turn it on locally.
- **`CA1707` (identifiers should not contain underscores)** — it would ban the underscores that the
  `_camelCase` private field convention requires.

### The list is a sealed allowlist

The configuration switches *everything* off, then names what is on. A rule added by a future
`Meziantou.Analyzer` or .NET SDK release therefore arrives **disabled**, and can only be adopted by a
deliberate change to this package. Upgrading a third-party analyzer, or your SDK, will not silently add
rules to your build.

## Overriding a rule

Set the severity in your own `.editorconfig`. It beats the package's configuration outright.

```ini
[*.cs]
dotnet_diagnostic.MA0051.severity = none      # turn a rule off
dotnet_diagnostic.MA0004.severity = warning   # or turn one on
```

Scope it to a path when the exception is about a *kind of code* rather than about the whole repository —
generated files, test doubles, migrations. Prefer a narrow glob over a repository-wide switch-off.

A convention about the *shape* of code has no path to narrow to, though, and `MA0048` above is the
common one. Where the exception genuinely runs through every folder, repository-wide is the honest
scope; a glob covering only part of it reads as a smaller deviation than the one actually taken.

There is no supported way to change the package's configuration from the outside, and that is
intentional: a deviation should be visible in the repository that takes it.

### Making it advisory

To adopt the ruleset without failing builds on it, set this in your `Directory.Build.props`:

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
</PropertyGroup>
```

Every rule then reports as a warning, except the five that ship at `error` — switch those to `warning`
individually if you need a fully green build while you work through the backlog.

### Switching the suppressor off

`APBS0001` is a suppression, not a rule, and an `.editorconfig` severity does not reach it:
`dotnet_diagnostic.APBS0001.severity = none` changes nothing. Name it in `NoWarn` instead, and
`CA1725` reports interface implementations again, alongside `APB0003`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);APBS0001</NoWarn>
</PropertyGroup>
```

## Adopting into an existing configuration

**Reconcile by subtraction.** Install the package, then delete every entry in your own `.editorconfig`
that the shipped configuration now sets identically. What is left is your genuine deviation set — and it
should be short enough to justify line by line.

A few things usually fall out:

- **Blanket category switch-offs are redundant.** The shipped configuration already starts from
  everything-off, so lines disabling a whole analyzer family do nothing. Delete them.
- **A different `Meziantou.Analyzer` version is fine.** Keep your pin. Rules your version does not
  implement are inert, and rules a newer one adds arrive disabled.
- **Keep anything path-scoped.** Those cannot travel in a package regardless.

## If it does not seem to be working

- **No new diagnostics at all.** The configuration is injected by an MSBuild `.props` file that NuGet
  wires up during restore, so it does nothing until a restore has succeeded. Run `dotnet restore`, and
  reload the solution if you are in an IDE.
- **Some projects are checked and others are not.** `PrivateAssets="all"` prevents the analyzers flowing
  between projects, so a `ProjectReference` does not carry them. Every project needs the reference
  itself, which a repository-root `Directory.Build.props` gives it.
- **Check it is live.** Add a primary constructor to a class — `internal sealed class Probe(int n);` —
  and build. You should get `APB0001`. Delete it afterwards.
- **A build error you cannot place, reported once per project with no file or line.** That is almost
  certainly `SA0001`; see [step 2](#2-add-these-editorconfig-entries).
- **The IDE flags something the build does not.** Expected for `IDE0003`, and only for that rule. See
  above.
- **The IDE offers to *introduce* something the build rejects.** Seen with ReSharper installed, and
  not a sign the ruleset is wrong. See
  [below](#resharper-offers-the-primary-constructor-apb0001-forbids).

### ReSharper offers the primary constructor `APB0001` forbids

With ReSharper installed — in Rider, or in Visual Studio — the editor offers **"Convert into primary
constructor"** on a type `APB0001` has just reported as an error. Same line, same moment. That reads
exactly like a misconfigured ruleset, and it is not one.

The offer comes from ReSharper's own engine, not from Roslyn. Roslyn's equivalent, `IDE0290`, is not
named in the sealed allowlist, so the blanket leaves it off; what ReSharper offers carries no
`IDExxxx` code, which is how you tell the two apart in the Alt+Enter menu. **Nothing this package
ships can reach it.** ReSharper reads its settings from `.editorconfig` files it discovers by walking
the directory tree, and the shipped configuration is not one of those — so switching the offer off is
an entry your repository makes, if it wants one.

Which entry depends on what you are being offered, and the editor tells you. If the code is
highlighted without pressing Alt+Enter *and* the Alt+Enter item has a **"Configure inspection
severity"** submenu, it is an *inspection*. Set it to `none` from that submenu and let the editor
write the `.editorconfig` key itself rather than typing the name out:

```ini
[*.cs]
resharper_convert_to_primary_constructor_highlighting = none
```

If instead there is no highlight until you press Alt+Enter and no severity submenu, it is a *context
action*: those carry no severity and have no `.editorconfig` representation at all, leaving
ReSharper's own context-action settings page or a team-shared `.sln.DotSettings` as the only levers.
Either way it is a per-repository editor choice rather than something a NuGet package delivers, and
either way ReSharper reads none of it unless EditorConfig support is enabled in the IDE.

`csharp_style_prefer_primary_constructors = false` is **not** the fix for this. It is a Roslyn style
option governing `IDE0290`, which the blanket already leaves off, and it says nothing to ReSharper's
engine.

## Versioning

A diagnostic ID is public API — you reference it by string in configuration you own — so it is versioned
as one.

| Change | Bump | Can it fail a build that was green? |
|---|---|---|
| New rule, or a raised severity | minor | **Yes**, by design — you opt in by upgrading |
| A rule catches cases it previously missed | patch | **Yes**, silently — same rule, more hits |
| A rule is removed or renamed | **major** | No, but it silently voids configuration you wrote |
| Code fix added, false positive fixed | patch | No |

Read the
[changelog](https://github.com/aprbrown-development/Aprbrown.Analyzers/blob/main/CHANGELOG.md) before
upgrading. Every entry is grouped by build impact and answers one question: *can this fail a build that
was green before?*

## Links

- [Repository](https://github.com/aprbrown-development/Aprbrown.Analyzers)
- [Changelog](https://github.com/aprbrown-development/Aprbrown.Analyzers/blob/main/CHANGELOG.md)
- [Report an issue](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues)
- MIT licensed — `Copyright (c) 2026 Andrew P R Brown`
