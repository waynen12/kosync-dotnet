# Task: Phase 5 — deploy to the droplet

A handoff brief for a fresh session. Phase 5 is the deployment phase, and
the roadmap is deliberate that **it is an investigation before it is an
implementation** — it begins with questions nobody has answered yet.

This file exists so that session does not spend its context re-deriving
what is already known. It is a starting point, not a plan: the plan is
what you produce.

## Read first, in this order

1. `CLAUDE.md` — orientation and the coding standards (note the
   no-hardcoded-values rule; it is directly implicated below).
2. `GLOSSARY.md` — the domain vocabulary. Everything assumes its terms.
3. `docs/ROADMAP.md` — **"Phase 5 — Deployment to the droplet"** and
   **"Deployment target"**. These hold the three numbered steps and the
   per-environment settings table. Do not restate them here; work from them.
4. `docs/adr/0008-one-document-per-user-and-hash.md` — the destructive
   migration precedent.
5. `docs/adr/0003` (Blazor circuits — bears on reverse-proxy WebSocket
   config and on what a mid-deploy reader sees), `0005` and `0006`
   (credentials and first-login; they set what must be true before this is
   internet-facing).

## Where the project actually stands (2026-11-17)

- Phases 0-3 complete and **merged to `main`** (PR #45). Branch from `main`.
  `design/portal-redesign` is fully contained in it and survives as history.
- **475 tests green**, **zero open issues**.
- Cross-device sync, regression protection and metadata are **confirmed on
  real hardware**. Groups C, D and E of `docs/manual-testing.md` are not yet
  confirmed — that is reading-room work, independent of this task, and it is
  not a blocker for the survey.

## You have SSH access to the droplet

The previous session did not, which is why only the repo-side half of the
investigation was done. Confirm what the access actually is before relying
on it (whether `sudo` is available matters for reading proxy configs).

**The box is in production.** It runs ai-hub (`hub.notrauto.org`) and n8n
(`n8n.notrauto.org`), and other people use both. Therefore:

- **Step 1 is strictly read-only.** Survey, do not change. No installs, no
  restarts, no `docker compose` anything, no edits to a proxy config.
- Do not deploy kosync on this pass. Surveying and deploying are separate
  sessions; the roadmap's step 2 has to be answered before a deploy is safe
  (see the hazard below).
- If a finding tempts you to "just fix it while I'm here" — write it down
  instead and confirm with the user.

## Step 1: survey, before designing anything

Run `docs/deploy/survey-droplet.sh` on the droplet and work from its output.
It is read-only, every probe guarded by a `command -v` check, and safe to
inspect before running — do read it first. It has been syntax-checked and
test-run on the development machine, but never against the droplet, so
expect to fix at least one assumption about what is installed there.

What the survey has to establish, per roadmap step 1 — **what can be reused
rather than what to build**:

- What is in front of ai-hub and n8n. Something must be, since both answer
  on subdomains. `README.md` mentions Nginx Proxy Manager, but the roadmap
  records that as **inherited from the upstream repo this was forked from,
  not a decision made here** — so treat it as a hypothesis to test, not a
  fact. Whatever it turns out to be needs its own README section replacing
  that mention.
- How TLS is issued and renewed, and whether adding a fourth subdomain is a
  DNS record plus a config block or something more involved.
- Whether Docker is installed, what else uses it, and how those apps are
  deployed. kosync is to run **containerised** even though ai-hub is not —
  that is already decided, for isolation on a shared box.
- Disk and memory headroom.

Two things are **decided and must not be re-litigated**: the own-subdomain
routing (`kosync.notrauto.org`, not a path prefix) and the container. The
roadmap explains both; the subdomain reasoning in particular is load-bearing.

## Already found, so you need not re-derive it

From a repo-side pass on `main`. Verify rather than trust — but start here.

**The urgent one: a deploy that forgets one flag looks like a success.**

- The SQLite path is hardcoded to `ContentRootPath/data/kosync.sqlite3`
  (`Program.cs:40`) — `/app/data` in the container. There is no `VOLUME` in
  the `Dockerfile` and no compose file in the repo. The `README.md` compose
  example *does* bind-mount it (`:40-41`), which is safer than a named
  volume — a bind mount is outside the `docker compose down -v` trap the
  roadmap warns about. Note it points at the **upstream** image, not ours.
- So the live hazard is narrower than "every redeploy": it is a `docker run`
  with no `-v`, or a compose file written from scratch without the mount.
  Then the database lands in the container's writable layer,
  `KosyncDbInitializer.cs:21-26` recreates `data/`, nine migrations apply to
  nothing, `admin` is reseeded — and **the box comes up looking like a
  healthy fresh install, with nothing logged to say otherwise.** That
  silence is the dangerous part.
- Migrations run **unattended on every startup**:
  `KosyncDbInitializer.cs:26` calls `db.Database.Migrate()`.
- **Four** of the nine migrations touch rows, not just schema —
  `AddBookUserId` deletes orphaned Books, `AddUserPasswordState` rewrites
  every row, `AddProgressResets` drops a column after carrying its data,
  and `AddDocumentHashUniqueIndex` runs two `DELETE`s (ADR 0008). All four
  are covered by dedicated migration tests. They are correct, and they
  still delete rows.
- Of the nine `Down` methods, **one** is genuinely safe. Treat this project
  as having a restore-from-backup story, not a rollback story — and note
  that it does not have the backup half yet either.

The hardcoded path is also a standards violation in its own right
(`CLAUDE.md:45-48`): not configurable, not a constant, and written twice in
two different forms — `Program.cs:40` and
`Database/KosyncDbContextFactory.cs:13` (the latter relative, design-time
only). Every *other* deployment fact in this codebase is bound from
configuration, so this one is the outlier rather than the convention.

**SQLite is in WAL mode**, so `-wal` and `-shm` are part of the database;
`run-local.sh:39` already has to account for this. Any backup strategy that
copies only `kosync.sqlite3` is wrong — it restores with recent pushes
silently missing. Two things make this harder than it looks: nothing in the
repo *configures* WAL (it comes from the EF Sqlite provider and is persisted
in the file header, so it is a property of the database the droplet will
carry, not of our code), and **`sqlite3` is not installed in the runtime
image** — so `.backup` and `VACUUM INTO`, the two correct hot-copy
mechanisms, are not available in the container as it is built today.

**There is no CI build or test workflow.** `.github/workflows/` contains
only `docker-publish.yml`, which fires on `v*.*.*` tags and pushes a
multi-arch image to ghcr.io. Those 475 tests have never run in CI. That is
a precondition for roadmap step 3, not a detail: a pipeline trusted to
deploy should not be the first thing that ever runs the suite.

**The auth cookie is `CookieSecurePolicy.SameAsRequest`** (`Program.cs:54`).
The roadmap flags it as needing to become `Always` before this is
internet-facing, as part of a dedicated pre-deployment pass rather than
incrementally.

**Two environment-sensitive settings the roadmap's table does not have.**
Both found by the repo-side pass; both are reverse-proxy-shaped, so the
survey's answer about what fronts the box feeds straight into them.

- **`app.UseForwardedHeaders()` at `Program.cs:102` is a no-op.**
  `ForwardedHeadersOptions` is never configured and the default is
  `ForwardedHeaders.None`, so nothing rewrites `Request.Scheme` from
  `X-Forwarded-Proto`. Flipping `SecurePolicy` to `Always` still works (it
  does not consult the scheme), but framework-generated redirects and
  absolute URLs would stay `http://` on an HTTPS site. Do not confuse this
  with `TRUSTED_PROXIES` (`Services/ProxyService.cs:80`), which is a
  separate hand-rolled mechanism for identifying the client IP in logs and
  in `ManagementController` — it does not touch the scheme.
- **Data Protection keys are not persisted.** Nothing calls
  `AddDataProtection()` anywhere in the tree, so the key ring lives in the
  container's writable layer (or in memory, if the README's
  `user: 1000:1000` denies the write — there is no passwd entry for that
  uid, so `HOME` resolves to `/`). Either way **every deploy and restart
  invalidates every portal session cookie and outstanding antiforgery
  token.** Devices are unaffected, since they hold no session — so it is
  survivable, and it may well be acceptable. But it is undocumented, and it
  is the same shape as the roadmap's "open Blazor circuit mid-deploy"
  bullet: it should be a decision, not a surprise. Readers being logged out
  by a deploy is a thing to decide about deliberately.

See `docs/deploy/phase5-findings.md` for the per-migration destructiveness
table, the full configuration audit and the backup-mechanism analysis.

## What to produce

Judgement call, but roughly: the survey's findings written down where they
belong, and the roadmap's step 2 questions answered rather than left open.
Per CLAUDE.md, a decision with real reasoning behind it belongs in an ADR —
the backup-and-migration-safety rule is very likely one, since it is a
constraint every future deploy inherits.

Resist writing the GitHub Actions pipeline on this pass. It is roadmap step
3 and it is downstream of 1 and 2 for a stated reason: *a pipeline that
automates an unsafe migration just makes the unsafe thing happen faster and
unattended.*

## Open questions worth putting to the user

- Whether a destructive migration may **ever** run unattended, or whether
  deploys stop and wait for a human when one is pending.
- What the rollback story is when a migration is one-way. EF Core has `Down`
  methods; the data they would need to reconstruct may be gone.
- What a reader with the portal open should experience mid-deploy. A dead
  circuit is acceptable (ADR 0003) — but it should be a decision, not a
  surprise.
