# ADR-0006: Analyse with the homelab SonarQube from a contained, repo-scoped runner

- **Status:** Accepted
- **Date:** 2026-10-18
- **Ticket:** [Publish a SonarQube analysis of main from a repo-scoped homelab runner](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/94)

## Context

The rest of the fleet publishes SonarQube analyses to the homelab's Community Build, from the org's
self-hosted runners on the same host. This repository is the only public one, and three facts
stand in the way of doing the same:

- The SonarQube server is reachable only from the LAN and WireGuard, so a GitHub-hosted runner
  cannot reach it.
- The org is on GitHub Free, which allows no runner group but `Default`. Labels route jobs but do
  not restrict them, so opening `Default` to public repositories would hand every org runner to
  whatever a fork pull request asks for.
- The org runners mount the Docker socket and use host networking. Either one gives a job the
  whole host.

## Decision

The analysis runs on a runner of its own, registered to this repository alone and built to contain
what it runs rather than merely to be revocable:

1. **Repo-scoped registration** with its own fine-grained token. `Default` stays closed to public
   repositories, and revoking this runner touches nothing else.
2. **No Docker socket, no host network, ephemeral.** It reaches SonarQube over a Docker network
   shared only with the `sonarqube` container, and every job starts from a fresh runner.
3. **A host firewall chain** drops its subnet's traffic to private address ranges, other Docker
   networks and the host's own addresses. A bridge network alone still reaches the LAN, including
   names whose only authorisation is being reachable.
4. **Approval for every outside collaborator.** The analysis workflow runs only on pushes to `main`
   and by dispatch, so it never runs fork code. The approval setting keeps a fork pull request
   from adding a workflow of its own that targets this runner.

## Considered options

- **SonarQube Cloud.** Free for public repositories and reachable from GitHub-hosted runners, but it
  splits this repository's analysis from the rest of the fleet's.
- **Joining the hosted runner to WireGuard, or exposing SonarQube publicly.** Each puts a path into
  the LAN within reach of a public repository's workflows, or of the internet.
- **Making the repository private.** It would contradict publishing a public package (ADR-0001).

## Consequences

- The runner, its network and the firewall chain live in the homelab `Containers` repository. The
  analysis cannot run until they exist.
- Build, test and every release gate stay on GitHub-hosted runners. The analysis is a signal, not a
  gate: nothing waits for its quality gate.
