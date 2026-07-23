# Aprbrown.Analyzers

One .NET coding standard, delivered as a package. It ships three custom Roslyn analyzers and — the
larger half — a configuration that decides which of ~130 rules from `Meziantou.Analyzer`,
`StyleCop.Analyzers` and the .NET SDK are switched on.

There is a single universal ruleset. No profiles, no severity levels, nothing to configure. If you
need something different, you override it in your own `.editorconfig`, which always wins.

> This file is packed as the package README. It is the installation instructions.

## Installing

Everything goes in the repository's `Directory.Build.props`, so it applies to every project, current
and future.

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Aprbrown.Analyzers"  Version="1.0.0"          PrivateAssets="all" />
    <PackageReference Include="Meziantou.Analyzer"  Version="3.0.123"        PrivateAssets="all" />
    <PackageReference Include="StyleCop.Analyzers"  Version="1.2.0-beta.556" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Three references, not one. `Aprbrown.Analyzers` deliberately takes **no dependency** on the other two:
it configures their rules without bundling their assemblies, so your repository owns those versions
and can pin whatever it likes. A configuration entry for an analyzer you have not installed is inert,
so nothing breaks if you drop one — those rules simply stop being enforced.

Put the references in `Directory.Build.props` rather than one project. `PrivateAssets="all"` stops
analyzers flowing to *your* consumers, but it does not cover the project that declares the reference —
so every project needs its own, which is what a repository-wide `Directory.Build.props` gives you.

### Then add this `.editorconfig`

```ini
# Test doubles hold values, not injected dependencies, and throwing is the correct way
# to say "not part of this fake's contract".
[tests/**.cs]
dotnet_diagnostic.APB0001.severity = none
dotnet_diagnostic.MA0025.severity = none
```

**This part is not optional.** Roughly 84% of primary-constructor usage in practice is the test-double
idiom (`private sealed class StubShopper(ShoppingBoard board) : IShopper`), and path-scoped rules
cannot travel inside a package — a global config can only address exact absolute file paths, which a
package can never know. Adjust the glob if your tests live elsewhere.

Two more, if your project has the relevant folders:

```ini
[{Pages,Data/Migrations}/**.cs]
dotnet_diagnostic.MA0048.severity = none   # Razor / EF file naming

[Data/Migrations/**.cs]
dotnet_diagnostic.MA0051.severity = none   # generated code is long
```

## What arrives with the package

**Two MSBuild properties**, as defaults you can override:

| Property | Default | Why it travels |
|---|---|---|
| `EnforceCodeStyleInBuild` | `true` | Without it the IDE rules silently do not run at build time |
| `TreatWarningsAsErrors` | `true` | The ruleset is a gate, not a suggestion |

Both are part of how the ruleset *functions*, which is why they ship. Override either by setting it in
your `Directory.Build.props` — that runs after the package's own defaults and wins.

`Nullable` and `ImplicitUsings` do **not** travel. They are decisions about your project's shape, not
about code style.

**A configuration file** listing every enabled rule. It is a *baseline*: your own `.editorconfig`
outranks it, always. That is a documented guarantee of the compiler, not an accident of file layout.

## The rules

| Tier | What is on |
|---|---|
| `APB0001`–`APB0003` | The three custom rules below |
| `Meziantou.Analyzer` | All 103 default-on rules, plus `MA0032`, minus `MA0004` |
| .NET SDK `CA` | The 27 rules the SDK enables by default |
| `StyleCop.Analyzers` | `SA1201`–`SA1204` only — member ordering. This is not a StyleCop adoption |
| IDE / naming | `_camelCase` private fields, `PascalCase` private statics, explicit accessibility modifiers, no `this.` qualification |
| Expression-bodied members | Seven rules at `suggestion` — nudges, never build failures |

**The custom rules:**

- **`APB0001`** — no primary constructors on classes or structs. Injected dependencies belong in an
  explicit constructor assigning readonly fields. *Records are exempt* — positional parameters are the
  point of a record.
- **`APB0002`** — no default value on a `CancellationToken` parameter. A defaulted token lets a caller
  drop cancellation without saying so, invisibly at the call site.
- **`APB0003`** — an interface implementation's parameter names must match the interface's. `CA1725`
  covers base-class overrides but is blind to interface implementations; this is that missing half.
  Ships with a code fix.

**`MA0004` (`ConfigureAwait`) is deliberately off.** Its rationale — that ASP.NET installs no
synchronization context — is false for a class library. If your repository is all-web, turn it on
locally.

The list is a **sealed allowlist**: the configuration switches everything off, then names what is on.
A rule added by a future `Meziantou` or SDK release therefore arrives *disabled*, and can only be
adopted by a deliberate change to this package. Upgrading a third-party analyzer will not silently add
rules to your build.

## Deviating

Set the severity in your own `.editorconfig`. It beats the package's configuration outright.

```ini
[*.cs]
dotnet_diagnostic.MA0051.severity = none      # turn a rule off
dotnet_diagnostic.MA0004.severity = warning   # or turn one on
```

Scope it to a path when the exception is about a *kind of code* rather than the whole repository —
generated files, test doubles, migrations. Prefer a narrow glob over a repository-wide switch-off.

There is no supported way to change the package's configuration from the outside, and that is
intentional: a deviation should be visible in the repository that takes it.

## Adopting in a repository that already has an `.editorconfig`

**Reconcile by subtraction.** Install the package, then delete every local entry the shipped
configuration now sets identically. What is left is your genuine deviation set — and it should be
short enough to justify line by line.

A few things that usually fall out:

- **Blanket category switch-offs are redundant.** The configuration already starts from
  everything-off, so lines that disable a whole analyzer family do nothing. Delete them.
- **A different `Meziantou.Analyzer` version is fine.** Keep your pin. Rules your version does not
  implement are inert; rules it adds arrive disabled.
- **Keep anything path-scoped.** Those cannot travel in a package regardless.

Adoption is **big-bang**: install, fix the violations, done. There is no severity ratchet and no
baseline-suppression file. In practice the naming and accessibility rules cost nothing — measured at
zero violations across six real repositories — and violations concentrate in the `Meziantou` and `CA`
tiers.

## Versioning

A diagnostic ID is public API — you reference it by string in configuration you own.

| Change | Bump |
|---|---|
| New rule, or a raised severity | minor — can fail a previously green build, by design |
| A rule catches cases it previously missed | patch — can fail a green build *silently* |
| A rule is removed or renamed | **major** — it voids configuration you wrote |
| Fixer added, false positive fixed | patch |

Read [`CHANGELOG.md`](https://github.com/aprbrown-development/Aprbrown.Analyzers/blob/main/CHANGELOG.md)
before upgrading. Every entry answers one question: *can this fail a build that was green before?*

## Links

- [Repository](https://github.com/aprbrown-development/Aprbrown.Analyzers)
- [Issues](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues)
- MIT licensed — `Copyright (c) 2026 Andrew P R Brown`
