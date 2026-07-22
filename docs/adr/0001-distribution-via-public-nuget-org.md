# ADR-0001: Distribute `Aprbrown.Analyzers` as a public nuget.org package

- **Status:** Accepted
- **Date:** 2026-07-22
- **Ticket:** [Decide the distribution mechanism: nuget.org, submodule, or GitHub Packages](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/3)
- **Map:** [Map: a portable house ruleset for .NET](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/1)

## Context

`Aprbrown.Analyzers` packages a house ruleset — three custom Roslyn analyzers, a curated set of
third-party analyzer references, and a shipped global analyzer config — for consumption by six
`net10.0` repos: `Mixologist`, `WordleHelper`, `EveEsiClient`, `FPL_Helper/web`, `WeddingSite`,
`EveIndustryTools`.

Three delivery mechanisms were considered: a public nuget.org package, a git submodule consumed via
`ProjectReference` + `OutputItemType="Analyzer"`, and a private GitHub Packages feed.

### What the environment actually looks like

- **All six consumer repos are private**, and they span **two owners** — `aprbrown` (`Mixologist`,
  `WeddingSite`, `FPL_Helper`) and the `aprbrown-development` org (`EveIndustryTools`,
  `EveEsiClient`, and this repo). `WordleHelper` has no remote at all yet.
- **Four of the six build in Docker** — `Mixologist`, `FPL_Helper` (×2), `EveIndustryTools`,
  `WeddingSite` — each running `dotnet restore` *inside* `mcr.microsoft.com/dotnet/sdk:10.0`.
- **CI is nearly absent.** Only `EveIndustryTools` has workflows, and its self-hosted runners
  (`gh-runner-1/2` in the homelab) are scoped `RUNNER_SCOPE: repo` against that one repo.
- The nuget.org ID `Aprbrown.Analyzers` is **unregistered**; a search for `Aprbrown` across
  nuget.org returns zero packages.

### The private-feed argument, taken seriously

The obvious case for nuget.org — "a private feed means auth friction in six repos" — is **weaker
than it first appears**, and was tested rather than assumed.

`dotnet nuget config paths` on this machine returns exactly one file:
`~/.nuget/NuGet/NuGet.Config`. A private source and its credentials can live there, and every
`dotnet restore` on the box picks it up with **no per-repo `nuget.config` at all**. That genuinely
solves local development and agent sessions.

It does not solve the other three places restore runs:

| Where `dotnet restore` runs | Sees the machine-global `NuGet.Config`? |
|---|---|
| Local dev / agent sessions on this host | **Yes** — configured once |
| Docker builds (4 of 6 repos) | **No** — separate filesystem |
| Self-hosted runner containers | Only inside the container; `~/.nuget` is not a mounted volume, so it is lost on image recreate — and the runners serve one repo |
| GitHub-hosted runners | **No** — fresh VM per run |

Two further findings, both verified on this machine:

1. **Credentials cannot be encrypted on Linux.** `dotnet nuget add source -p` fails with
   `Password encryption is not supported on .NET Core for this platform` /
   `Encryption is not supported on non-Windows platforms`. A private feed means a long-lived PAT
   in **cleartext** on disk.
2. **Docker is the binding constraint.** Reaching a private feed from inside an image build
   requires either baking a credential into a layer (`COPY`/`ARG` — both persist in image history)
   or plumbing a BuildKit `--mount=type=secret` through four Dockerfiles *and* every
   `docker build` / compose invocation that drives them. This failure is at least loud — a 401 at
   build time — unlike the silent class of failure documented for `TreatWarningsAsErrors` in the
   delivery research.

### Options rejected

- **Git submodule.** Works for the compiler — a global config resolved from a submodule path
  produces byte-identical diagnostics to the NuGet route. But it carries a silent failure mode:
  every fresh clone, CI checkout, container build and agent session needs `--recursive`, and
  forgetting it fails quietly. It additionally requires its props be imported from the consumer's
  `Directory.Build.props` rather than the `.csproj` body, or `TreatWarningsAsErrors` silently fails
  to travel (hazard H1 in the delivery research). And it would need adding to `Mixologist`'s
  hand-maintained Dockerfile `COPY` list — a trap that file's own comments already document for
  `.editorconfig`.
- **GitHub Packages.** Feeds are owner-scoped, so spanning two owners needs a PAT reaching across
  both. Combined with the cleartext-on-Linux constraint and the Docker problem, it costs more than
  it saves.
- **A self-hosted feed with anonymous LAN read** (BaGet or a Forgejo package registry behind the
  existing Caddy). This is the only private option needing no credential anywhere, and it was
  considered seriously. Rejected because it makes six repos **unbuildable when the homelab is down
  or when off the LAN**, and adds a service to the maintenance rotation — trading a publish step
  for an availability dependency across the whole fleet.

## Decision

**`Aprbrown.Analyzers` is published as a public package on nuget.org.**

Consumers reference it with an ordinary `PackageReference`, resolved from the default nuget.org
source that every machine, container image and CI runner already has. No feed configuration, no
credentials, and no changes to any Dockerfile.

**The package's contents are an explicit allowlist.** The packaging project declares exactly what
it packs rather than relying on SDK defaults.

## Consequences

### Accepted costs

- **The ruleset is public and permanent.** nuget.org has no true unpublish. A version that has been
  pushed remains downloadable by exact version forever; delisting only removes it from search and
  from version resolution. The rules and analyzer DLL were judged non-sensitive.
- **The decision is reversible forward, not backward.** Moving to a private feed later costs a
  publish pipeline and a consumer re-point — nothing traps the project. What cannot be undone is
  anything already shipped inside a `.nupkg`.
- **A low, largely voluntary maintainer surface** — the package is installable by anyone, and the
  ID-stability promise in the map's versioning policy (major version for removing or renaming a
  diagnostic ID) now extends to strangers, not only to the six repos.

### The allowlist constraint, and why it exists

The irrevocability above is the whole reason for it: an accidental inclusion cannot be withdrawn.
Anything that leaks into a `.nupkg` — a `.pdb` embedding local paths, a stray source file, a README
carrying an internal URL — is public for good. The delivery research also found NuGet packing to
behave non-obviously (`<None Include="build\**" Pack="true" />` silently omits dotfiles, producing a
package that built clean and enforced nothing), which is exactly the class of surprise an explicit
allowlist surfaces. The first push is to be treated as unrevocable.

### What this unblocks or forces

- **A licence is now mandatory**, not optional — nuget.org requires a `PackageLicenseExpression`.
  This also forces the question of whether the source repo goes public alongside the package. Both
  belong to [Choose a licence for the package](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/6).
- **The publish path is now a nuget.org push**, needing a nuget.org account and an API key as an
  implementation prerequisite. Shape is
  [Decide the publish and CI story](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/7).
- **The consumption spec** expresses onboarding as a plain `PackageReference` in each repo's
  `Directory.Build.props` (required — `PrivateAssets="all"` does not cover the direct consumer, so
  every project needs its own reference), and expresses the packing allowlist. See
  [Write the consumption spec](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/8).
- **The ID is available and unclaimed**, so the map's identity decision (package ID
  `Aprbrown.Analyzers`, diagnostic prefix `APB`) stands without adjustment.
