# ADR-0003: Publish from tags via trusted publishing, and gate everything at PR time

- **Status:** Accepted
- **Date:** 2026-07-23
- **Ticket:** [Decide the publish and CI story](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/7)
- **Map:** [Map: a portable house ruleset for .NET](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/1)
- **Supersedes nothing. Depends on:** [ADR-0001](0001-distribution-via-public-nuget-org.md) (public nuget.org distribution)

> ADR-0002 is reserved for the ruleset architecture warranted by
> [Audit the Mixologist ruleset against the universality test](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/4)
> and is not yet written. This ADR is numbered 0003 to avoid claiming that slot.

## Context

ADR-0001 settled distribution: `Aprbrown.Analyzers` ships as a public package on nuget.org. It
left the publish *path* open — how a version gets built, gated, versioned, and pushed.

The repository has **no CI of any kind** today (no `.github/` directory). Across the six-repo
fleet only `EveIndustryTools` has workflows, and those are project-management rather than
build/test. Its two self-hosted homelab runners (`homelab-eit-1/2`) exist because heavy iteration
was exhausting the GitHub Actions allowance, and are scoped to that repo. This repository is on the
`aprbrown-development` org (free plan) and is **private**, while the package it produces is public.

### The property that drives every decision below

**nuget.org versions are immutable and unrevocable.** A published `1.0.0` cannot be replaced; it can
only be delisted, which removes it from search and version resolution but leaves it downloadable
forever by exact version. ADR-0001 accepted this for *contents*. The consequence for *process* is
sharper: a version number is spent the moment it is pushed, and a gate that first fires at tag time
has already cost one.

This inverts the usual CI economics, in which release-time checks are a reasonable last line of
defence. Here, release-time is too late by construction.

### Credentials

ADR-0001 rejected private feeds partly because **Linux cannot encrypt NuGet credentials**
(`Password encryption is not supported on .NET Core for this platform`, verified on this machine), so
any long-lived token would sit in cleartext. A hand-run `dotnet nuget push` reintroduces exactly that
failure mode one layer up, in `~/.nuget/NuGet/NuGet.Config`.

nuget.org has offered **Trusted Publishing** since September 2025. A GitHub Actions job with
`permissions: id-token: write` calls `NuGet/login@v1`, which exchanges GitHub's signed OIDC token for
a **single-use API key valid for one hour**. No long-lived secret is created, stored, or rotated.

## Decision

### 1. Publishing is tag-triggered and keyless

A `v*` tag push runs `release.yml`, which builds, tests, packs, and pushes to nuget.org using an
OIDC-issued ephemeral key. No `NUGET_API_KEY` secret exists in this repository.

### 2. The git tag is the sole source of the version number

No `<Version>` property in `Directory.Build.props` or anywhere else. `release.yml` packs with
`-p:Version=${GITHUB_REF_NAME#v}`.

Rejected: **MinVer**. It would make every untagged build mint a height-based prerelease
(`0.0.0-alpha.0.7`), which forces a second decision — whether CI ever pushes prereleases, and where —
that this project does not need. Given the first push is unrevocable, the repository should contain
**no mechanism that mints publishable version numbers on its own**. A version exists only when one is
typed into `git tag`.

Rejected: a hand-edited `<Version>` property, which makes a release a three-part ritual (edit props,
edit `AnalyzerReleases.Shipped.md`, tag) in which two parts can silently disagree with the third.

### 3. Two workflows; `release.yml` is the only privileged one

`ci.yml` runs on pull requests and pushes to `main`. `release.yml` runs on `v*` tags and is the only
file that ever holds `id-token: write`. The trusted publishing policy names `release.yml` and leaves
the environment blank.

The file that can mint a nuget.org key and push something unrevocable should be short enough to read
in one screen and boring enough that it almost never changes. A single workflow with a conditional
publish job means every future edit to PR CI happens inside the file holding publish rights.

**`release.yml` must not delegate its build to a reusable workflow** (`workflow_call`). OIDC workflow
claims are subtle across that boundary and the failure would surface at the push. If the build steps
grow enough to warrant sharing, use a **local composite action** (`.github/actions/…`), which runs
inside the calling job and leaves the OIDC claim untouched. Until then, duplicating a few setup lines
across the two files is the cheaper trade.

Runners are GitHub-hosted `ubuntu-latest`. The homelab runners are offline, scoped to another repo,
and — per ADR-0001's reasoning against a self-hosted feed — the fleet's ruleset should not depend on
the homelab being up. If the Actions allowance ever becomes a constraint here as it did for
`EveIndustryTools`, self-hosted runners remain available **without redesigning the publish story**:
OIDC token issuance works identically on self-hosted runners.

### 4. Every gate runs at PR time; `release.yml` re-runs the same set and discovers nothing new

Because a version number is spent on push, `release.yml` is a re-run, never a first look.

### 5. A pack-consume smoke test is a first-class gate, not a nicety

Unit tests over `DiagnosticAnalyzer` classes test the *analyzer*. They cannot test the *package*. The
delivery research
([#2](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/2)) produced a package that
built clean and **enforced nothing**, because `<None Include="build\**" Pack="true" />` silently omits
dotfiles. Nothing in an analyzer unit test suite catches that.

`scripts/verify-package.sh` packs to a temporary folder feed, restores a fixture consumer project
against it, builds, and asserts the exact diagnostic ID set. The fixture covers one case per failure
class the design can produce:

| Assertion | Proves |
|---|---|
| An `APB` violation fires | The analyzer assembly is packed and loads |
| A `CA` violation fires | The `AnalysisMode` tier survives the blanket `none` (#4's open worry) |
| A `Meziantou` violation fires with the package installed | Config-without-assembly binds when the assembly arrives |
| An IDE naming violation fires | Enumeration-by-ID works (#4 verified these are silently unenforced otherwise) |
| `MA0004` does **not** fire | The sole deliberate exclusion stays excluded |
| `TreatWarningsAsErrors` / `EnforceCodeStyleInBuild` arrive | Hazard H1 in #2 is a *silent* failure |

A shell script rather than an xunit test shelling out to `dotnet build`: the check's precondition is a
packed artifact and a temp feed, which is a shell job by nature, and a script runs identically on a dev
machine — the gate is not something only CI can execute.

This fixture doubles as the **executable form of the onboarding snippet**, so the consumption spec can
point at something provably working rather than describing it.

### 6. The repository is held to its own ruleset, by consuming the shipped file in place

The repository's projects reference **the same physical config file the packaging project packs**, via
a `GlobalAnalyzerConfigFiles` item — not a copy. One file, no drift, and every build exercises the
artifact being shipped. The repository takes the same pinned `Meziantou.Analyzer` and
`StyleCop.Analyzers` `PackageReference`s the spec prescribes, making it the **reference implementation
of the onboarding snippet**.

**A project cannot be its own analyzer.** `src/Aprbrown.Analyzers` cannot have `APB0001` enforced
against it; there is no build ordering that allows it short of committing a bootstrap DLL, which is
rejected. The `APB` rules therefore gate `Aprbrown.Analyzers.CodeFixes`, the packaging project, and the
tests via `ProjectReference` + `OutputItemType="Analyzer"`; the analyzer project itself is held to the
config and third-party rules only. This hole is inherent, not a compromise.

When a house rule trips on this repository's own code, **the fix is a local `.editorconfig` deviation,
not a change to the shipped config** — the same escape hatch every consumer gets. The analyzer projects
target `netstandard2.0` and will hit rules the `net10.0` fleet never does. This repository is a
consumer; if the deviation mechanism is uncomfortable here, that is signal about the mechanism.

### 7. The sealed blanket suppresses this repository's own safety net, and the local config restores it

`Microsoft.CodeAnalysis.Analyzers` ships the RS-series that keeps analyzer authors honest — RS1001
("Missing diagnostic analyzer attribute", category `MicrosoftCodeAnalysisCorrectness`, default-on
warning) through the RS2000-series that keeps `AnalyzerReleases.Shipped.md` / `.Unshipped.md`
truthful, including RS2008 "Enable analyzer release tracking".

The shipped config **opens with `dotnet_analyzer_diagnostic.severity = none`** and enumerates only what
is on. The RS-series is not in that allowlist. Combined with decision 6, dogfooding therefore
**silently switches off the entire analyzer-authoring ruleset in the one repository in the fleet where
it matters** — release tracking included, leaving `Unshipped.md` free to rot with nothing complaining.

**This repository's local `.editorconfig` re-enables the RS categories** —
`MicrosoftCodeAnalysisCorrectness`, `MicrosoftCodeAnalysisDesign`, `MicrosoftCodeAnalysisReleaseTracking`,
`MicrosoftCodeAnalysisPerformance` — at **category** level, one line each, so RS rules added in future
package versions are covered automatically. Precedence supports this: an `.editorconfig` entry beats a
global AnalyzerConfig outright, and a category severity beats the blanket
([configuration files](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files#precedence)).

**Not** in the shipped config. The tempting counter-argument is the `MA0115` precedent from #4 — an
inert rule ships on, "covering the day a repo adopts it". But map Note 10 already ruled per-repo local
analyzer projects a **non-feature**, so there is no day to cover; shipping the RS-series would quietly
contradict it.

### 8. Release tracking is gated by the build, plus one release-time assertion

Given decision 7, release tracking is enforced by `dotnet build` + `TreatWarningsAsErrors` — no bespoke
CI step. `release.yml` adds one assertion of its own: **`AnalyzerReleases.Unshipped.md` must contain no
rule rows**.

The two are independent and catch different mistakes: the build check catches rules that were never
*recorded at all*; the release check catches rules recorded but never promoted to *shipped*. The
release check is deliberately version-agnostic rather than "the tag matches a `## Release X.Y` header",
which would wrongly fail every patch release, since a detection-fix patch adds no section.

### 9. `AnalyzerReleases.Shipped.md` is not the changelog

`Shipped.md` records rule *table* changes — IDs added, removed, severities changed — and is
machine-enforced, so it cannot rot. But map Note 11 defines a third release class: **patch for detection
fixes**. A patch where an existing rule starts catching a case it previously missed adds *nothing* to
`Shipped.md`, because the rule table is unchanged. Under `TreatWarningsAsErrors` that is precisely the
release that turns a consumer's green build red. **The most dangerous class of change is the one
`Shipped.md` is structurally blind to.**

A `CHANGELOG.md` therefore answers one question per version: **can this fail a build that was green on
the previous version?**

- **New rule or raised severity** — yes, by design. Opted into by upgrading.
- **Improved detection** — yes, *silently*. Same rule, more hits. Named out loud or it ambushes people.
- **Fixer added, false positive removed, docs** — no. Safe bump.

`release.yml` cuts a GitHub Release whose body is that version's CHANGELOG section, so there is exactly
one authored source and the release page cannot disagree with the repository.

Rejected: release notes auto-generated from commit messages. Commits describe what the implementer did;
this changelog must answer what happens to the consumer's build, and no commit convention reliably
encodes "this now catches more".

### 10. Package metadata is an explicit, reviewed allowlist

- **No `.snupkg`, no `.pdb` in the package** for v1. ADR-0001 names a `.pdb` embedding local paths as an
  unrevocable leak, and debugging an analyzer from a consumer machine is a non-scenario — the place to
  debug it is this repository's test project. Shipping symbols later is purely additive.
- **`ContinuousIntegrationBuild=true` in CI** regardless: it normalises embedded paths and costs nothing.
- **A package-specific README** (`PackageReadmeFile`), not the repository README. Different audiences, and
  the repository README is where an internal URL would plausibly wander in. It carries the onboarding
  snippet, so the nuget.org page *is* the install instructions.
- **`PackageProjectUrl`, `RepositoryUrl` and `HelpLinkUri` point at their eventual public locations.**
  They are correct the day the repository goes public and need no package rebuild; if it stays private
  they are a cosmetic wart on an audience of one. Omitting `HelpLinkUri` buys nothing — the IDE's fallback
  for an unknown `APB0001` is a web search that finds nothing either.

### 11. The first tag is `v1.0.0-preview.1`, and it is expected to be thrown away

Four things can only be tested against production nuget.org: the trusted publishing policy actually
matching (owner, repository and workflow filename must all line up), the rendered package contents, a
real consumer restoring from the real feed, and — because this repository is **private** — the **7-day
pending-activation window**. A policy created against a private repo is only temporarily active until a
successful publish stamps the GitHub repository and owner IDs into it, guarding against resurrection
attacks; without a publish inside 7 days it goes inactive (restartable at any time).

A disposable preview costs nothing — nobody references it, and burning a preview version number is what
preview version numbers are for. It moves every first-run failure onto a version *designed* to be
disposable, and it stamps the policy permanently active before the release that matters. `Mixologist`,
already chosen under map Note 9 as the onboarding canary because it should light up at zero, then
consumes the preview from real nuget.org as the final validation. Only then is `v1.0.0` tagged.

## Consequences

### What this forces

- **Implementation prerequisites**, to be performed close to the first publish rather than in advance
  (the 7-day window actively argues against creating the policy early): a nuget.org account; a trusted
  publishing policy naming owner `aprbrown-development`, repository `Aprbrown.Analyzers`, workflow file
  `release.yml`; and the nuget.org **profile name** (not email) available to `NuGet/login@v1`, stored as
  a repository secret.
- **`.github/workflows/ci.yml`, `.github/workflows/release.yml`, `scripts/verify-package.sh`, a fixture
  consumer project, `CHANGELOG.md`, and a package README** all become deliverables of the consumption
  spec.
- **The repository's own `.editorconfig` becomes load-bearing**, holding both the RS-category re-enables
  and any `netstandard2.0`-driven deviations.

### Accepted costs

- **The first release is a two-step ritual** (preview, validate against `Mixologist`, then release) rather
  than one tag.
- **A few duplicated setup lines** between `ci.yml` and `release.yml`, accepted deliberately over a
  reusable-workflow indirection that could confuse the OIDC claim.
- **No approval gate on the unrevocable push.** GitHub's docs state *"Users with GitHub Free plans can
  only configure environments for public repositories"*, so required reviewers are unavailable while this
  repository is private on the free org plan. The PR-time gates and the preview rehearsal carry that
  weight instead. Noted as an input to
  [Choose a licence for the package](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/6):
  taking the repository public would make the gate available at zero cost.
