# ADR-0010: Analyse same-repository pull requests with a scoped SonarQube token

- **Status:** Accepted
- **Date:** 2026-10-25
- **Ticket:** [Analyse Aprbrown.Analyzers pull requests in SonarQube with a scoped token](https://github.com/aprbrown/containers/issues/49)
- **Amends:** [ADR-0006](0006-sonarqube-analysis-from-a-contained-repo-scoped-homelab-runner.md)
  (which events run the analysis, and whether it gates)

## Context

The private repositories in the fleet analyse each pull request into a throwaway SonarQube project,
`<repository>-pr-<n>`, and fail the pull request when that project has issues main's doesn't. Here,
pull requests got feedback from the in-build analyzers alone; SonarQube saw a change only after it
merged.

Doing the same here meets three constraints:

- The fleet's pull request token, `ci-pr`, can create projects and read every project, including
  the private repositories' code and findings. This repository's workflows run on a runner that
  may carry fork code, so that token must not be stored here.
- The compare and cleanup live in the `Standards` repository, which is private, and a public
  repository can call only public reusable workflows and actions.
- Fork pull requests receive no secrets, nor do Dependabot's.

## Decision

1. **A SonarQube user of its own, `ci-pr-public`**, whose token is the repository secret
   `SONAR_PR_TOKEN`. It holds only the global Create Projects permission. The default permission
   template gives a project's creator Browse, See Source, Execute Analysis and Administer, so it
   can analyse, read and delete the pull request projects it creates and nothing else.
2. **The private repositories' projects are private**, readable only by the `private-projects`
   group, and the default template and visibility make every new project the same. The built-in
   `sonar-users` group, which every user is in, holds no permissions. `Aprbrown.Analyzers` stays
   public, so the new user can read main's issues.
3. **Only same-repository pull requests not opened by Dependabot are analysed.** The others have no
   token, so their jobs are skipped rather than failed.
4. **The analysis, the compare and the cleanup run on the repository's own runner**, with copies of
   `Standards`' Sonar actions under `.github/actions`. The compare runs as the last step of the
   analysis job, since the runner takes one job at a time and the scanner has already waited for
   the analysis to finish.
5. **The compare fails the pull request** when its project has an issue main's doesn't, or when its
   latest analysis isn't of the pull request's head commit.

## Considered options

- **Reusing `ci-pr`'s token.** A leak would expose every private repository's code and findings.
- **Removing `sonar-users` from each private project and leaving the template alone.** Every
  project created later, including the private repositories' pull request projects, would again be
  readable by every user.
- **Making `Standards` public** so this repository can call its workflows. Its other workflows and
  baseline are private by choice.

## Consequences

- Main pushes still publish the analysis main is compared against; a pull request opened while that
  analysis runs can show issues main has just fixed as new. Re-running the job after main's analysis
  finishes clears them.
- Fork and Dependabot pull requests get no SonarQube check, only the in-build analyzers.
- A pull request's analysis and compare run from its own branch, so a pull request can weaken its
  own check. Only branches pushed to this repository get the token, which takes write access.
- The copies under `.github/actions` drift from `Standards` until they are updated by hand.
- Each pull request is a full build and test on the single contained runner, one job at a time.
