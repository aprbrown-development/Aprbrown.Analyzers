# How a shared ruleset reaches a consuming project, and what outranks what

Research resolving [issue #2](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/2).
Facts only — no recommendation about which distribution mechanism to adopt (that is issue #3).

**Method.** Every claim below is either quoted from a primary source (Microsoft Learn, the Roslyn
compiler source, the shipped .NET SDK targets on this machine, NuGet's own docs/design wiki) or
established by building a throwaway project and observing which diagnostics fired. Empirical results
are labelled with a test ID; the lab scripts live in the session scratchpad, and every test is
reproducible from the description given. No blog post is cited anywhere.

**Environment for all empirical results:** .NET SDK `10.0.110`, Linux, `dotnet build
--no-incremental`. SDK targets quoted from `/usr/share/dotnet/sdk/10.0.110/`.

---

## Verdict on the map's assumed architecture

Map Note 6 assumes: *the package ships a `.globalconfig` baseline; the consumer's local
`.editorconfig` outranks it; path carve-outs like `[tests/**.cs]` stay local because global configs
cannot express them.*

**All three halves of that assumption are correct, and are now verified rather than assumed.** Two
things the map did *not* anticipate are load-bearing and are recorded as Hazards below:

- **H1.** `TreatWarningsAsErrors` is *not* freely settable as an overridable default from any import
  position. The .NET SDK assigns it `false` in `Microsoft.NET.Sdk.props`, so a
  `Condition="'$(TreatWarningsAsErrors)' == ''"` guard only ever fires if the setter runs *before*
  that. A NuGet package's `build/*.props` does; an `<Import>` in the project body does **not**. This
  is a real behavioural difference between the package route and the naive submodule route.
- **H2.** A file literally named `.globalconfig` is auto-discovered by the SDK *and* defaults to
  `global_level = 100`. Shipping the package's baseline under that name would collide head-on with
  any consumer's own `.globalconfig` and silently unset the colliding keys. The package's file must
  be named `<PackageId>.globalconfig`.

A third, unrelated finding is worth flagging to the human because it makes a comment in
Mixologist's `.editorconfig` factually wrong: **naming-rule declaration order does not matter**
(Finding 5b).

---

## 1. How a config file physically reaches the compiler

### 1.1 The item-group chain

There is exactly one channel. From the shipped SDK,
`/usr/share/dotnet/sdk/10.0.110/Roslyn/Microsoft.Managed.Core.targets` lines 138–147:

```xml
<_AllDirectoriesAbove Include="@(Compile->GetPathsOfAllDirectoriesAbove())" ... />
<PotentialEditorConfigFiles Include="@(_AllDirectoriesAbove->'%(FullPath)'->Distinct()->Combine('.editorconfig'))" ... />
<EditorConfigFiles Include="@(PotentialEditorConfigFiles->Exists())" ... />

<GlobalAnalyzerConfigFiles Include="@(_AllDirectoriesAbove->'%(FullPath)'->Distinct()->Combine('.globalconfig'))" ... />
<EditorConfigFiles Include="@(GlobalAnalyzerConfigFiles->Exists())" ... />
```

`/usr/share/dotnet/sdk/10.0.110/Microsoft.CSharp.CurrentVersion.targets` line 243 then hands the
combined list to the compiler task:

```xml
<Csc ... AnalyzerConfigFiles="@(EditorConfigFiles)" ... />
```

and `ManagedCompiler.AddAnalyzerConfigFilesToCommandLine` in `dotnet/roslyn` emits one
`/analyzerconfig:<path>` switch per item. There is no separate `/globalanalyzerconfig:` switch.

**So:**

- `GlobalAnalyzerConfigFiles` and `EditorConfigFiles` are **not** two different mechanisms.
  `GlobalAnalyzerConfigFiles` is a discovery/opt-in item that the SDK folds *into* `EditorConfigFiles`;
  `EditorConfigFiles` is what actually reaches the compiler. Adding to either works.
- `GlobalAnalyzerConfigFiles` is the **documented public extension point** for package authors
  ([Microsoft Learn, *Configuration files for code analysis rules*](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)):
  > "Global AnalyzerConfig files can be distributed with NuGet packages. To do so, add a .props file
  > to the NuGet package. In the .props file, add a `GlobalAnalyzerConfigFiles` item under the
  > `Project` node."
- Microsoft's *own* analyzer packages nevertheless append to `EditorConfigFiles` directly, from a
  `.targets` file, inside a target ordered `BeforeTargets="CoreCompile"` —
  `/usr/share/dotnet/sdk/10.0.110/Sdks/Microsoft.NET.Sdk/analyzers/build/Microsoft.CodeAnalysis.NetAnalyzers.targets`:
  ```xml
  <Target Name="AddGlobalAnalyzerConfigForPackage_MicrosoftCodeAnalysisNetAnalyzers"
          BeforeTargets="CoreCompile" Condition="'$(SkipGlobalAnalyzerConfigForPackage)' != 'true'">
    ...
    <ItemGroup Condition="Exists('$(_GlobalAnalyzerConfigFile_MicrosoftCodeAnalysisNetAnalyzers)')">
      <EditorConfigFiles Include="$(_GlobalAnalyzerConfigFile_MicrosoftCodeAnalysisNetAnalyzers)" />
    </ItemGroup>
  </Target>
  ```
  **Both idioms were tested and both work** (Test G and Test A respectively). The
  `GlobalAnalyzerConfigFiles`-in-`.props` form is the documented one; the `EditorConfigFiles`-in-a-
  `.targets`-target form is the one Microsoft ships, and it has the advantage that the file can be
  selected from MSBuild properties evaluated late (which is how the SDK picks an
  `AnalysisLevel`-specific config).

### 1.2 What makes a file "global"

Decided by the *compiler*, after parsing, not by the item group. `dotnet/roslyn`,
`src/Compilers/Core/Portable/CommandLine/AnalyzerConfig.cs`:

```csharp
internal bool IsGlobal => _hasGlobalFileName || GlobalSection.Properties.ContainsKey(GlobalKey);
```

— i.e. the filename is exactly `.globalconfig` (case-insensitive), **or** the file contains
`is_global = true`. The MSBuild item group only decides whether the file is passed to the compiler at
all.

### 1.3 Auto-discovery, and the duplicate-include trap

The `Combine('.globalconfig')` over `_AllDirectoriesAbove` above means the SDK **already** picks up
any file named `.globalconfig` in the project directory *or any ancestor directory*, with no item
group needed.

**Test (empirical).** A `.globalconfig` dropped in the project directory containing
`dotnet_diagnostic.CA1822.severity = error`, with no MSBuild changes at all, produced `error CA1822`.
Adding the *same* file explicitly via `<GlobalAnalyzerConfigFiles Include="...">` produced:

```
CSC : warning MultipleGlobalAnalyzerKeys: Multiple global analyzer config files set the same key
'dotnet_diagnostic.ca1822.severity' in section 'Global Section'. It has been unset.
Key was set by the following files: '.../.globalconfig, .../.globalconfig'
```

— the file collided with itself and **the setting was dropped**. Do not hand-add a file named
`.globalconfig`; it is already included. (Disable with `DiscoverGlobalAnalyzerConfigFiles=false` if
ever needed.)

---

## 2. Precedence: `.globalconfig` vs `.editorconfig`

### Documented

[Microsoft Learn, *Configuration files for code analysis
rules*](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files),
precedence table:

> **In an EditorConfig file and a Global AnalyzerConfig file** | The entry in the EditorConfig file
> wins.

This is **explicitly documented, not incidental.** The design rationale is in
[dotnet/roslyn#48634](https://github.com/dotnet/roslyn/issues/48634):

> "Standard `.editorconfig`s can resolve conflicts via their natural filesystem hierarchy… Global
> analyzer configs can exist in completely different hierarchies, so a NuGet provided config in
> `c:\users\user\.nuget\cache\MyPackage\1.0.0\.globalconfig` could be 'further away' than
> `c:\myproject\.globalconfig` but should clearly not take precedence."

### Verified, both directions

| Test | `.globalconfig` | `.editorconfig` | Result |
|---|---|---|---|
| 2a | `CA1822 = error` | `[*.cs] CA1822 = none` | **no diagnostic** — editorconfig won |
| 2b | `CA1822 = none` | `[*.cs] CA1822 = error` | **2 × `error CA1822`** — editorconfig won |
| 4 | `CA1822 = error` | `[*] CA1822 = none` | **no diagnostic** — a bare `[*]` section is enough |
| 3 | `CA1822 = error`, **`global_level = 9999`** | `[*.cs] CA1822 = none` | **no diagnostic** — editorconfig *still* won |

**Test 3 is the important one:** `global_level` cannot lift a global config above an `.editorconfig`.
It only breaks ties *between global configs*. The consumer's local `.editorconfig` is
unconditionally the top of the stack.

### Precedence among multiple global configs

Learn, same page:

> **In two global AnalyzerConfig files** | **.NET 6 and later versions**: The entry from the file with
> a higher value for `global_level` takes precedence. If `global_level` isn't explicitly defined and
> the file is named *.globalconfig*, the `global_level` value defaults to `100`; for all other global
> AnalyzerConfig files, `global_level` defaults to `0`. If the `global_level` values … are equal, a
> compiler warning is reported and both entries are ignored.

Confirmed by `AnalyzerConfig.cs`:

```csharp
if (GlobalSection.Properties.TryGetValue(GlobalLevelKey, out string? val) && int.TryParse(val, out int level))
    return level;
else if (_hasGlobalFileName)
    return 100;
else
    return 0;
```

**Verified (Tests F, F2, H2).** Package config named `Aprbrown.Analyzers.globalconfig` (level 0, and
separately level −100) versus a consumer `.globalconfig` (level 100): consumer won cleanly, no
warning. Package config forced to `global_level = 100` versus consumer `.globalconfig` (also 100):
`MultipleGlobalAnalyzerKeys` warning and the colliding key unset on both sides.

The SDK's own shipped baseline hedges the same way —
`Sdks/Microsoft.NET.Sdk/analyzers/build/config/analysislevel_10_default.globalconfig` opens with:

```ini
is_global = true

global_level = -100
```

> **Hazard H2.** Ship the package baseline as `<PackageId>.globalconfig`, never as `.globalconfig`.
> The bare name is both auto-discovered (§1.3) and level-100 by default, which is a direct collision
> with the consumer's own file.

---

## 3. Path scoping: can carve-outs travel?

**No.** Section headers in a global config must be **exact absolute file paths**. Globs are rejected
outright.

Roslyn, `src/Compilers/Core/Portable/CommandLine/AnalyzerConfigSet.cs`:

```csharp
if (IsAbsoluteEditorConfigPath(section.Name)) { ...MergeSection... }
else { diagnostics.Add(Diagnostic.Create(InvalidGlobalAnalyzerSectionDescriptor, ..., section.Name, config.PathToFile)); }
```

with the message from `CodeAnalysisResources.resx`:

> Global analyzer config section name '{0}' is invalid as it is not an absolute path. Section will be
> ignored.

**Verified:**

| Test | Section header in `.globalconfig` | Result |
|---|---|---|
| 5 | `[*.cs]` | `warning InvalidGlobalSectionName`, **section dropped entirely** |
| 6 | `[/abs/path/to/proj/tests/**.cs]` | `warning InvalidGlobalSectionName` — **an absolute prefix does not rescue a glob** |
| 7 | `[/abs/path/to/proj/tests/TestWidget.cs]` | **works** — that one file exempted, the other still flagged |

Matching is literal string equality against the normalized source path, not glob matching
(`AnalyzerConfigSet.cs`: `if (normalizedPath.Equals(section.Name, Section.NameComparer))`).

Since a shipped package cannot know a consumer's absolute paths, **path carve-outs cannot travel in a
global config. They are local `.editorconfig` territory, exactly as Note 6 assumes.**

**Test C (end-to-end)** confirms the local carve-out works *against* a package-shipped global config:
consumer `.editorconfig` with `[tests/**.cs] dotnet_diagnostic.CA1822.severity = none` suppressed the
package's `CA1822 = error` under `tests/` only, leaving it firing elsewhere.

---

## 4. Bulk category switches in a global config

**They work, identically to `.editorconfig`.**

Syntax and precedence from [Microsoft Learn, *Configuration options for code
analysis*](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options):

> `dotnet_analyzer_diagnostic.category-<rule category>.severity = <severity value>`

> An entry for a category takes precedence over an entry for all analyzer rules. An entry for an
> individual rule by ID takes precedence over an entry for a category.

**Verified:**

| Test | `.globalconfig` contents | Result |
|---|---|---|
| 8 | `dotnet_analyzer_diagnostic.category-Performance.severity = error` | 2 × `error CA1822` — bulk switch works |
| 8b | same, plus `dotnet_diagnostic.CA1822.severity = none` | **no diagnostic** — specific ID beats category, same file |

**Test K — Mixologist's actual StyleCop block.** All eight
`dotnet_analyzer_diagnostic.category-StyleCop.CSharp.*.severity = none` lines, plus
`dotnet_diagnostic.SA0001.severity = none` and the four `SA1201`–`SA1204` re-enables, against a real
`StyleCop.Analyzers 1.2.0-beta.556` reference:

- baseline, no config: `SA0001 SA1101 SA1201 SA1502 SA1516 SA1633`
- same block in a **`.globalconfig`**: `SA1201` only
- same block in an **`.editorconfig`**: `SA1201` only

Byte-identical outcome. **Dotted category names survive.** Mixologist's eight-category switch-off and
four-rule switch-back-on port to a global config unchanged.

---

## 5. Naming conventions in a global config

### 5a. They work

**Test 9a/9b.** The full `dotnet_naming_rule.*` / `dotnet_naming_symbols.*` / `dotnet_naming_style.*`
block lifted verbatim out of Mixologist's `.editorconfig` (private fields `_camelCase`, private
statics `PascalCase`, plus `dotnet_diagnostic.IDE1006.severity = warning`), placed in an
`.editorconfig` and then in a `.globalconfig`. Identical output both times — same three diagnostics,
same messages, same locations:

```
Widget.cs(5,17):     IDE1006: Naming rule violation: Missing prefix: '_'
Widget.cs(6,24):     IDE1006: Naming rule violation: These words must begin with upper case characters: badStatic
tests/TestWidget.cs(5,17): IDE1006: Naming rule violation: Missing prefix: '_'
```

Note in particular that `badStatic` was matched by the *more specific* `required_modifiers = static`
rule and reported as a PascalCase violation, not swallowed by the general private-field rule — the
discrimination Mixologist depends on survives.

**Test A (end-to-end through a real package)** reproduces this from a `.nupkg`.

### 5b. But declaration order does not mean what Mixologist's comment says

Mixologist's `.editorconfig` carries this comment:

> "Private *statics* stay PascalCase, so the first rule below must precede the second — naming rules
> are matched in order, and the general one would otherwise swallow them."

**That is not true of this SDK.** [Microsoft Learn, *Naming
rules*](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules):

> The order in which naming rules are defined in an EditorConfig file doesn't matter. The naming
> rules are automatically ordered according to the definitions of the rules themselves. More specific
> rules regarding accessibilities, modifiers, and symbols take precedence over less specific rules.

**Verified two ways:**

- **Test 10.** Reversing the two Mixologist rules (general private-field rule declared *first*)
  changed nothing — `badStatic` was still reported as a PascalCase violation. True in both
  `.editorconfig` and `.globalconfig`.
- **Tie-break test.** Two rules of *identical* specificity (both `applicable_kinds = field`,
  `applicable_accessibilities = private`), one demanding prefix `aaa_`, one demanding `zzz_`:

  | file type | declared `aaa` then `zzz` | declared `zzz` then `aaa` |
  |---|---|---|
  | `.editorconfig` | `Missing prefix: 'aaa_'` | `Missing prefix: 'aaa_'` |
  | `.globalconfig` | `Missing prefix: 'aaa_'` | `Missing prefix: 'aaa_'` |

  Declaration order is ignored in **both** file types; the winner is chosen by a deterministic
  order-independent rule.

**Consequence for this map:** the "ordering is lost in a dictionary-backed global config" worry that
would have blocked moving naming rules into a `.globalconfig` **does not apply**, because ordering is
not used in the first place. Naming conventions travel cleanly. Separately, Mixologist's comment
should be corrected when it is next touched — the behaviour it describes is not the behaviour it
gets. (Mixologist is nevertheless *correct* today: the specificity rule produces the outcome it
wanted, for a different reason than the comment claims.)

---

## 6. MSBuild properties as overridable defaults

### The idiom, and where it is safe

[Microsoft Learn, *Customize your
build*](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build): MSBuild is
import-order dependent and the last definition of a property wins; defaults for things a project may
customize belong in `.props`.

The import chain, from the shipped SDK
(`Sdks/Microsoft.NET.Sdk/Sdk/Sdk.props`, whose own comment states the ordering explicitly):

> "This must be set here (as early as possible, before `Microsoft.Common.props`) so that everything
> that follows can depend on it. **In particular, `Directory.Build.props` and nuget package props
> need to be able to use this flag and they are imported by `Microsoft.Common.props`.**"

```xml
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" ... />
<Import Project="$(MSBuildThisFileDirectory)..\targets\Microsoft.NET.Sdk.props" />
```

So the order is:

1. `Microsoft.Common.props` → imports `Directory.Build.props`, **then** the project's
   `obj/*.nuget.g.props` (which imports every package's `build/<PackageId>.props`)
2. `Microsoft.NET.Sdk.props` → SDK defaults
3. the project file body

### Hazard H1 — `TreatWarningsAsErrors` is already set by the time the body runs

`/usr/share/dotnet/sdk/10.0.110/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.props` line 114:

```xml
<TreatWarningsAsErrors Condition="'$(TreatWarningsAsErrors)'==''">false</TreatWarningsAsErrors>
```

That is step 2 above. Anything running at step 3 sees `TreatWarningsAsErrors` as `false`, never
empty, so `Condition="'$(TreatWarningsAsErrors)' == ''"` **silently never fires**.

By contrast `EnforceCodeStyleInBuild` is defaulted much later, in
`Microsoft.NET.Sdk.Analyzers.targets` line 99 — a `.targets` file — so it is safe to default from
anywhere in the props phase.

**Verified:**

| Test | Where the package props were imported from | `EnforceCodeStyleInBuild` | `TreatWarningsAsErrors` |
|---|---|---|---|
| A | NuGet `build/<PackageId>.props` (step 1) | `true` ✔ | `true` ✔ |
| I | `<Import>` in the `.csproj` body (step 3) | `true` ✔ | **`false` ✘** |
| J | `<Import>` inside consumer `Directory.Build.props` (step 1) | `true` ✔ | `true` ✔ |

Test I's failure is visible in the diagnostics, not just the property: the IDE1006 naming violations
came out as *warnings* instead of *errors*.

> **Hazard H1.** A disk-path/submodule delivery must be imported from the consumer's
> `Directory.Build.props`, not from the project file body, or `TreatWarningsAsErrors` silently does
> not travel. A NuGet package has no such constraint — `build/*.props` is already in the right
> place.

### Consumer override still works

**Test D.** Consumer `Directory.Build.props` setting `TreatWarningsAsErrors=false` beat the package's
guarded default (naming violations demoted from error to warning). Because
`Directory.Build.props` is imported *before* the NuGet props in step 1, the package's
`Condition="'$(Prop)' == ''"` correctly sees the consumer's value and stands down. The idiom is
sound.

**Test E.** Consumer setting `EnforceCodeStyleInBuild=false` removed the IDE1006 diagnostics
entirely while CA-prefixed rules kept firing — an independent confirmation of map Note 5's claim that
without `EnforceCodeStyleInBuild` the IDE rules silently do not run at build time.

---

## 7. Package plumbing (secondary, but it bit during testing)

- **Folder conventions.** `build/<PackageId>.props` / `.targets` applies to the direct consumer;
  `buildTransitive/` is what flows to indirect consumers
  ([NuGet docs](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets),
  [NuGet/Home design wiki](https://github.com/NuGet/Home/wiki/Allow-package--authors-to-define-build-assets-transitive-behavior)).
  The `<PackageId>.props` filename is a hard requirement — NuGet's convention-based auto-import finds
  the file by that name.
- **`PrivateAssets="all"` does not block the direct consumer.** Per the [PackageReference
  reference](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files),
  `PrivateAssets` controls only what flows *onward*. **Test A** confirms: a package referenced with
  `PrivateAssets="all"` still had its `build/*.props` imported and its global config applied.
- **…but it does block downstream flow, as intended. Test (transitivity):** project `A` references
  the package with `PrivateAssets="all"`; project `B` has a `ProjectReference` to `A`. `B` compiled
  with `TWAE=[false] ENFORCE=[false]` and none of the ruleset's diagnostics. Every project that wants
  the ruleset must reference the package itself — in practice via a repo-wide `PackageReference` in
  `Directory.Build.props`, which is already how Mixologist wires StyleCop.
- **Packing trap.** `<None Include="build\**" Pack="true" .../>` **does not pick up dotfiles.** A file
  named `.globalconfig` was silently absent from the produced `.nupkg` (verified by `unzip -l`),
  producing a package that appeared to work and enforced nothing. A second reason to use
  `<PackageId>.globalconfig`.

---

## 8. Submodule / disk path vs package: are they the same file?

**For the compiler, yes.** `AnalyzerConfig.Parse` requires only that the config's own path be
rooted:

```csharp
if (pathToFile is null || !Path.IsPathRooted(pathToFile) || string.IsNullOrEmpty(Path.GetFileName(pathToFile)))
    throw new ArgumentException("Must be an absolute path to an editorconfig file", nameof(pathToFile));
```

MSBuild resolves item `Include` paths to full paths before the compiler sees them, so a config in
`~/.nuget/packages/...` and one at `../shared/x.globalconfig` arrive identically. Section headers are
absolute-or-ignored either way (§3) — never resolved relative to the config file's own location.

**Verified (Test I2).** `<GlobalAnalyzerConfigFiles Include="..\submodule\Aprbrown.Analyzers.globalconfig" />`
written straight into a `.csproj`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` set
explicitly in the same project, produced **exactly the same five diagnostics** as the NuGet package
route in Test A.

**For MSBuild, no — with one specific caveat.** The two routes differ in exactly one respect, and it
is Hazard H1: a package's `build/*.props` is imported at a point where `TreatWarningsAsErrors` is
still empty; a project-body `<Import>` is not. Routing the submodule props through the consumer's
`Directory.Build.props` (Test J) restores parity completely.

Second, smaller difference: the two `global_level` defaults are name-based, not origin-based, so a
submodule file named `.globalconfig` and a package file named `.globalconfig` are equally
collision-prone (§2). Origin is irrelevant; **the filename is what matters.**

---

## Source list

Primary sources, all fetched and quoted:

- Microsoft Learn — [Configuration files for code analysis rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)
- Microsoft Learn — [Configuration options for code analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options)
- Microsoft Learn — [Naming rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules)
- Microsoft Learn — [Customize your build](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build) and [Customize by directory](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)
- Microsoft Learn / NuGet — [MSBuild props and targets in a package](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets), [PackageReference in project files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
- NuGet/Home wiki — [Allow package authors to define build assets transitive behavior](https://github.com/NuGet/Home/wiki/Allow-package--authors-to-define-build-assets-transitive-behavior)
- dotnet/roslyn — `src/Compilers/Core/Portable/CommandLine/AnalyzerConfig.cs`, `AnalyzerConfigSet.cs`, `CodeAnalysisResources.resx`, `src/Compilers/Core/MSBuildTask/ManagedCompiler.cs`
- dotnet/roslyn — [issue #48634](https://github.com/dotnet/roslyn/issues/48634) (global analyzer config precedence design), [PR #49834](https://github.com/dotnet/roslyn/pull/49834) (`global_level`)
- dotnet/msbuild — `src/Tasks/Microsoft.Common.props`, `src/Tasks/Microsoft.Common.targets`
- Shipped .NET SDK 10.0.110 on this machine — `Roslyn/Microsoft.Managed.Core.targets`,
  `Microsoft.CSharp.CurrentVersion.targets`,
  `Sdks/Microsoft.NET.Sdk/Sdk/Sdk.props`,
  `Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.props`,
  `Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.Analyzers.targets`,
  `Sdks/Microsoft.NET.Sdk/analyzers/build/Microsoft.CodeAnalysis.NetAnalyzers.targets` and its
  `config/*.globalconfig`

Note: `dotnet/roslyn/docs/features/globalconfig.md` — referenced in the ticket — **does not exist**
and never has (`gh api repos/dotnet/roslyn/commits?path=docs/features/globalconfig.md` returns
empty). The design narrative lives in issue #48634 and PR #49834; the normative behaviour lives in
`AnalyzerConfig.cs` / `AnalyzerConfigSet.cs`.

Reference implementation read but not modified: `/home/aprbrown/Projects/Mixologist` —
`.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`.
