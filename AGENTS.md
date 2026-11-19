# AGENTS.md

## Build and test

- `dotnet test Empower2Asm.Core.slnf` builds and tests Core anywhere. Use this by default.
- `dotnet build Empower2Asm.sln` also builds the Empower adapter and the CLI, and needs `EmpowerToolkitInteropDir` in a git-ignored `local.props` (see README.md).
- Everything is .NET Framework 4.8, x86. Core must never reference the Empower Toolkit.
- Never commit anything from the Waters Empower Toolkit (`/Toolkit/` is ignored).

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
