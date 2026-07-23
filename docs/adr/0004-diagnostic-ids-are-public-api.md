# ADR-0004: A diagnostic ID is public API, and versioning follows from that

- **Status:** Accepted
- **Date:** 2026-07-23
- **Ticket:** [Write the consumption spec](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/8)
- **Map:** [Map: a portable house ruleset for .NET](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/1)
- **Depends on:** [ADR-0002](0002-sealed-allowlist-ruleset-architecture.md) (the sealed allowlist),
  [ADR-0003](0003-tag-triggered-trusted-publishing-with-pr-time-gates.md) (release mechanics)

## Context

An analyzer package's observable surface is not its assembly API — no consumer calls into it. What
consumers actually depend on is the set of diagnostics it emits and the severities it assigns, and
they depend on it *by string*: `dotnet_diagnostic.APB0001.severity = none` in a repository's own
`.editorconfig`, a `#pragma warning disable APB0002` at a call site, a suppression in a fixture.

That makes semantic versioning ambiguous unless the question "what is the public API?" is answered
explicitly. It matters more here than for most packages because
[ADR-0003](0003-tag-triggered-trusted-publishing-with-pr-time-gates.md) sets
`TreatWarningsAsErrors` in every consuming repository: a rule that starts firing does not produce a
warning someone may notice, it produces a broken build.

## Decision

**A diagnostic ID is public API.** Versioning follows:

| change | bump | why |
|---|---|---|
| New rule added to the allowlist | **minor** | Additive, but can fail a previously green build. Opted into by upgrading. |
| Severity raised | **minor** | Same. |
| Rule removed or ID renamed | **major** | Breaks config a consumer owns. Their `dotnet_diagnostic.APB0002.severity` line silently stops meaning anything. |
| Existing rule catches more cases (**detection fix**) | **patch** | Rule table unchanged, but a green build can go red. |
| Fixer added, false positive removed, docs | **patch** | Cannot fail a build that was green. |

The asymmetry is the point: removing a rule is *more* breaking than adding one. Adding a rule fails a
build loudly, at a specific line, with an ID to look up. Removing one silently voids a consumer's
configuration — their deviation still parses, still sits in their `.editorconfig`, and now does
nothing. Nothing tells them.

This policy is only enforceable because of ADR-0002's seal. Without the blanket, a consumer's rule
set could change because *upstream* shipped a new rule, with no version of this package moving at
all — and then "new rule means minor bump" would be a promise about something outside this package's
control.

### Release tracking

`AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` exist from day one, machine-checked
by the RS2000-series (which ADR-0003 decision 7 re-enables in this repository's local
`.editorconfig`, since the shipped blanket would otherwise suppress them here).

They cover only the `APB` rules — the diagnostics this package *defines*. The bulk of the house
ruleset is third-party IDs this package merely *configures*, and release tracking has no concept of
that. **`CHANGELOG.md` is therefore the authoritative record of what a version does to a consumer**,
and it is where a Meziantou or `CA` rule entering the allowlist gets recorded. Its job is to answer
one question per version: *can this fail a build that was green on the previous version?*

Detection fixes are the reason the changelog cannot be delegated to release tracking. A patch where
`APB0003` starts catching a case it previously missed adds nothing to `Shipped.md`, because the rule
table is unchanged — yet under `TreatWarningsAsErrors` it is the release most likely to break
someone. The most dangerous class of change is the one release tracking is structurally blind to.

## Consequences

- `APB0001` shipping without a code fix (settled on evidence in
  [#5](https://github.com/aprbrown-development/Aprbrown.Analyzers/issues/5)) is a **minor** bump to
  correct later, not a breaking change. Deferring a fixer costs nothing under this policy.
- Renaming an `APB` ID is expensive by design. IDs should be chosen once, at v1, with that in mind.
- A rule that turns out to be wrong is cheaper to **relax to `none` in the shipped config** (minor)
  than to remove from the allowlist (major). Removal is reserved for retiring a diagnostic the
  package no longer defines at all.
- Every changelog entry must classify build impact, which is authored, not generated. ADR-0003
  already rejected commit-message-derived release notes for this reason.
