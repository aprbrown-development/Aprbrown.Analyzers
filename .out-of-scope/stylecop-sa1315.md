# StyleCop SA1315 (parameter names should match inherited names)

The house ruleset does not enable StyleCop's `SA1315`, and the prescribed `StyleCop.Analyzers`
is not upgraded to a 1.3 beta to get it.

## Why this is out of scope

`SA1315` arrived in the `StyleCop.Analyzers` 1.3 betas. It checks parameter names against the
inherited names in two places, and both are already covered better elsewhere:

- **Interface implementations** are `APB0003`'s job. `APB0003` is stricter: when a method
  implements several interfaces whose parameters are named differently, it checks the name against
  each interface, where `SA1315` accepts a match with any one of them. Its code fix renames the
  symbol across the solution, so references in the method body follow.
- **Overrides** are `CA1725`'s job. It ships with the .NET SDK, and the house ruleset takes it on
  at every accessibility from 1.1.0. `CA1725` reports some interface implementations too,
  and accepts a match with any one of several interfaces; the package suppresses it there
  (`APBS0001`), so `APB0003` stays the single, stricter report on interfaces.

Enabling `SA1315` alongside `APB0003` would report every interface mismatch twice. Enabling it
instead of `APB0003` would trade a stricter rule for a looser one, and `APB0003`'s ID is public
API (ADR-0004), so it could only go in a major version anyway.

Getting `SA1315` also means moving from one StyleCop beta (`1.2.0-beta.556`) to another
(`1.3.x-beta`). The only stable StyleCop release is `1.1.118`. A beta upgrade has to re-prove the
four shipped ordering rules, `SA1201`–`SA1204`, and nothing it brings is wanted.

Look at this again only if a stable StyleCop 1.3+ is adopted for its own reasons. Even then, the
question is whether `SA1315` adds anything over `APB0003` plus `CA1725`, not whether to upgrade.

## Prior requests

- #73: "StyleCop 1.3's SA1315 overlaps APB0003"
