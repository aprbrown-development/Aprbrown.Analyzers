# CLAUDE.md

Guidance for Claude Code when working in this repository.

Aprbrown.Analyzers is a Roslyn analyzer + code fix package for .NET. See `README.md`
for the planned layout and build commands.

Run tests with `dotnet test --solution Aprbrown.Analyzers.slnx -c Release` (the .NET 10 SDK
rejects a positional solution).

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in `aprbrown-development/Aprbrown.Analyzers`,
managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label string equal to its name. See
`docs/agents/triage-labels.md`.

### Domain docs

Single-context — `GLOSSARY.md` and `docs/adr/` at the repo root. See
`docs/agents/domain.md`.
