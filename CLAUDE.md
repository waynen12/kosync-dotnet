## Agent skills

### Issue tracker

Issues are tracked as GitHub Issues in this repo (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-label vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Cosing Standards
Do not hard code any values. Ideally use parameters that can be set in DB and configured through the UI. 
If that is not possible use constants. Look for a constants helper file in the solution. If it does not exist, create one. 
