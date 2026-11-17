## Start here

A self-hosted KOReader progress-sync server, and a portal each reader logs
into with the same credentials their devices sync with.

**Read in this order:**

1. `GLOSSARY.md` — the domain vocabulary. Everything else assumes its terms
   (User, Administrator, Tenant, Provisional Password, Book, Document,
   Device, SyncEvent, Split Book, Observed/Curated Metadata, Progress Reset).
2. `docs/ROADMAP.md` — what is built, what is decided, and what is next.
3. `docs/adr/` — the eight decisions with real reasoning behind them. Read the
   ones touching your area before changing it; several look surprising until
   you read why (ADR 0005's bcrypt-over-MD5 especially).

**Where the code is.** Phases 0-3 are complete but live on the
`design/portal-redesign` branch, **53 commits ahead of `main` and unmerged**.
`main` is the pre-redesign server plus the design docs. Branch from
`design/portal-redesign`, not `main`, until that merge happens.

**What is verified.** `docs/manual-testing.md` — what three real readers have
actually proved, and what has not been tried. A green suite cannot tell you
whether two devices sync through this; that file tracks what can only be
learned from hardware.

**Running it locally.** `./run-local.sh` — binds the LAN so devices can reach
it, and points `Portal__BaseUrl` at this machine. `dotnet run` alone binds
localhost and leaves the portal URL wrong, both silently.

## Agent skills

### Issue tracker

Issues are tracked as GitHub Issues in this repo (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-label vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Coding standards

Do not hard code any values. Ideally use parameters that can be set in DB and
configured through the UI. If that is not possible use constants. Look for a
constants helper file in the solution (`Constants.cs`). If it does not exist,
create one.

When a decision has reasoning worth keeping, write it down where it belongs —
an ADR for an architectural choice, `GLOSSARY.md` for a term, a comment for a
local surprise. The comments in this codebase explain *why*, not *what*; match
that.

## Working alongside other agents

Agents commit directly into the shared working tree on the integration branch,
so another agent's in-progress work may already be staged. **`git commit`
commits the whole index, not just what you added** — check
`git diff --cached --name-status` before committing, or use
`git commit <pathspec>`. This has caused a broken branch tip before.
