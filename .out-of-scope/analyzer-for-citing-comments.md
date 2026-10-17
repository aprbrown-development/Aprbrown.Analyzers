# An analyzer for comments that cite tickets, ADRs or spec sections

The package does not ship a rule that flags a comment or documentation citing the ticket, ADR,
spec section or decision behind the code. That policy is a ground rule for whoever writes the code.
It is not something the build checks.

## Why this is out of scope

The house comment policy says a **comment** or **documentation** explains the code, and never refers
to the decision, ticket or document behind it. An analyzer can only enforce that by matching how
citations happen to be written today, and that catches the spelling rather than the habit.

The precise patterns are precise because they are narrow. Measured across the fleet's tracked `.cs`
files, ignoring generated code:

| Pattern | Matching lines | Real citations |
|---|---|---|
| `ADR-\d+` | 1,206 | ~100% |
| `AC ?#?\d+` | 1,648 | ~100% |
| `#\d+` | 2,997 | ~99% (misses: `#2 of 20`, upstream `aspnetcore#56742`) |
| `§` | 824 | ~96% (misses: RFC and OAuth section references) |
| `[A-Z]+-\d+` ticket keys | 1,774 | ~77% once ADR is excluded (SHA-256, UTF-8, ISO-8601, ISBN-13) |
| bare `spec` | 197 | ~94% (Playwright `.spec.ts`, "API spec") |
| `Step`/`Phase`/`D\d` | 356 | ~85% (numbered procedure steps) |

The narrow patterns already miss about 450 citing lines in that sample. Examples are `(Story 6.5)`,
`FR21`, `Epic 4`, "review round 3, D2", "Architecture line 884" and "per the spec record". Each one
needs a new pattern, and each new pattern is a detection fix that can turn a consumer's green build
red. Even then, the same reference can be reworded so that no pattern sees it: "as decided when we
picked the cache", "see the onboarding plan". The rule would teach people to cite differently, not to
stop citing.

There is also no code fix. Getting rid of a citation means rewriting the sentence so it explains the
code, or deleting it, and only a person or an agent reading the code can do that. A report-only rule
at `warning` would make the next upgrade a large manual job (thousands of sites in
`EveIndustryTools`). At `suggestion` it would enforce nothing.

The policy instead lives where code gets written: the **Comment** and **Documentation** entries in
`GLOSSARY.md`, the comment policy section that `APB0005` (#71) adds to `consuming.md`, the agent instructions that write most of the
fleet's code, and review. Existing citations are cleaned up once per repository by reading and
rewriting them, not by a diagnostic.

Look at this again only if a citation turns out to have a shape that is both unambiguous and
impossible to reword, and that review keeps missing. Even then, the question is whether one narrow
pattern pays for itself, not whether to police the policy as a whole.

## Prior requests

- #72: "Custom rule: a comment or documentation never cites a ticket, ADR or spec section"
