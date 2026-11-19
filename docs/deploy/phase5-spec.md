# Phase 5 spec — deploy to the droplet without ever costing data

The implementation spec for Phase 5, settled over four rounds of design
questions on 2026-11-17. It is the draft of a spec issue and nine task
issues; nothing here has been created on GitHub yet.

**Read first:** [ADR 0009](../adr/0009-what-a-deploy-may-cost.md) for the
data-safety stance, [phase5-droplet-survey.md](phase5-droplet-survey.md) for
what the box actually is, and [phase5-findings.md](phase5-findings.md) for
the repo-side audit. This spec assumes all three and does not repeat them.

## Scope

**In:** making the application safe to redeploy onto a database holding real
reading history, the artefacts needed to run it on the droplet, a CI workflow
that finally runs the suite, and one hand-run first deploy.

**Out, deliberately:** the GitHub Actions deploy pipeline (roadmap step 3).
Two reasons, and they are not "we ran out of time". The gate below means a
fully automated deploy is *partially impossible by design* — the whole point
is that it stops and waits. And a smoke-test-and-rollback step cannot be
designed before anyone has done this deploy once by hand and seen what it
looks like. Revisit after the first deploy, with evidence.

Also out: Phase 4 (AI), and the unconfirmed hardware groups C/D/E in
[manual-testing.md](../manual-testing.md), which are independent of this.

## What was decided

Everything below is settled. The reasoning for the data-safety items is in
ADR 0009; the deployment-shape items are in the survey.

| # | Decision |
|---|---|
| 1 | The pre-migration backup runs **in-process** via `SqliteConnection.BackupDatabase`. No `sqlite3` CLI in the image — this **amends ADR 0009**, which called for `apk add sqlite` before the API was confirmed present |
| 2 | Phase 5 stops at a hand-run first deploy plus a build-and-test workflow |
| 3 | The gate **exits non-zero before the web host starts**. It cannot be mistaken for a working server |
| 4 | The data directory comes from a `Storage` config section bound to `StorageOptions`, following the `PortalOptions` pattern |
| 5 | kosync's Caddy block goes **directly into `/opt/n8n/Caddyfile`**, which already routes `hub.notrauto.org` and is therefore already the box's shared edge config rather than n8n's private one |
| 6 | Back up **only when `GetPendingMigrations()` is non-empty**, into `<data>/backups/`, keeping the most recent N (default 5). Also **amends ADR 0009**, which said every start |
| 7 | Destructiveness is declared as `Constants.Migrations.Destructive`, a `static readonly string[]` of full EF migration ids, with a test asserting every entry resolves to a real migration |
| 8 | The override **names the migration ids it permits** — `ALLOW_DESTRUCTIVE_MIGRATIONS=<id>[,<id>]`. Self-expiring in effect: the next destructive migration has a different id and the gate fires again, even with the variable still in the compose file |
| 9 | No file-backed database → **skip both backup and gate**, derived from the connection's `DataSource`, not from a path guess. One rule covers fresh installs and in-memory tests |
| 10 | A failed backup **fails startup**, on the same exit path as the gate |
| 11 | Cookie `SecurePolicy` defaults to **`Always`**, overridden to `SameAsRequest` for local HTTP. Fail loud in dev, fail safe in production |
| 12 | `ForwardedHeadersOptions` gets a **config-bound `KnownNetworks`, default empty** — empty meaning "rewrite nothing", which is exactly today's behaviour |
| 13 | Image is **`v2.0.0` on a public ghcr package**, pulled on the droplet. Major bump because the portal redesign breaks against the `v1.x` this forked from |
| 14 | CI runs `dotnet test` on `pull_request` and `push` to `main`, **with branch protection requiring it** |
| 15 | `mem_limit: 512m`, `restart: on-failure:3`, and a `wget`-based healthcheck (confirmed present in the alpine runtime image; `curl` is not) |
| 16 | `GLOSSARY.md` gains exactly one term: **Destructive Migration** |
| 17 | First deploy order: **DNS record → container up → Caddy block + reload** |
| 18 | A **strong `ADMIN_PASSWORD`** is set before the first `up` |
| 19 | ADR 0009 is **amended in place**, dated, following the ADR 0003 precedent |

## The tasks

Nine, under one spec issue, with the dependencies below as native GitHub
issue dependencies. Tasks 5, 6 and 7 are independent of everything and can
land in any order.

The 2/3/4 split is deliberate: backup, gate and retention are one *feature*
but three independently reviewable changes, and bundling them is how a subtle
gate bug rides in behind an obviously-correct backup.

---

### Task 1 — `StorageOptions`, and one spelling of the data path

**Blocked by:** nothing.

The path is hardcoded in two places, in two different forms:
`Program.cs:37-40` (absolute, from `ContentRootPath`) and
`Database/KosyncDbContextFactory.cs:13` (relative, design-time only). That
violates the no-hardcoded-values standard in `CLAUDE.md`, and it is the
duplication the `Constants.cs` rule exists to prevent.

- Add `Models/StorageOptions.cs`, bound from a `Storage` section, with
  `DataDirectory` defaulting to `Constants.Storage.DefaultDataDirectory`.
- Follow `Models/PortalOptions.cs` exactly — including **not** adding the
  section to `appsettings.json`. Read that file's comment before deciding
  otherwise: a value there would win in production while the tests, whose
  content root has no `appsettings.json`, went on exercising the constant.
- Point `KosyncDbContextFactory` at the same constant.
- Container override is then `Storage__DataDirectory`.

**Acceptance:** the string `kosync.sqlite3` appears in exactly one place in
the solution; a test asserts an unset `Storage` section still resolves to
today's directory, so no existing install moves its database.

---

### Task 2 — Back up before migrating

**Blocked by:** task 1.

In `KosyncDbInitializer`, immediately before `db.Database.Migrate()`
(`:26`) — not in a deploy script, per ADR 0009.

- Skip entirely when the database is not an existing file, read from the
  connection's `DataSource` (decision 9). This is what keeps all 475 tests
  working: they run against `DataSource=:memory:`
  (`KosyncWebApplicationFactory.cs:75`), where every migration is always
  pending. **It must not be a test-aware branch** — it is the fresh-install
  rule, which ADR 0009 already commits to.
- Skip when `db.Database.GetPendingMigrations()` is empty (decision 6).
- Otherwise back up via `SqliteConnection.BackupDatabase` into
  `<data>/backups/`, named for the UTC instant and the migration about to
  apply, so the file says what it is a backup *of*.
- **A failure here fails startup** (decision 10) — including an unwritable
  backups directory. Log what failed and what it was protecting.

**Acceptance:** a test that seeds a file-backed database one migration
behind, starts the app, and asserts a backup file exists and opens as a valid
SQLite database containing the pre-migration rows. A second test asserting
nothing is written for an in-memory database. A third asserting startup
throws when the backup target is unwritable. Note the WAL point from ADR
0009: assert against a database with *uncheckpointed* WAL content, because a
mechanism that silently dropped it would pass a naive test.

---

### Task 3 — The destructive-migration gate

**Blocked by:** nothing (independent of task 2, same area).

- `Constants.Migrations.Destructive`, a `static readonly string[]` of full
  migration ids. Four entries today: `AddBookUserId`,
  `AddUserPasswordState`, `AddProgressResets`,
  `AddDocumentHashUniqueIndex` — each at its full
  `<timestamp>_<Name>` spelling, which is what `GetPendingMigrations()`
  returns.
- Each entry carries a comment saying *what* it does to rows. A bare list of
  ids tells a future reader nothing about why these four.
- `ALLOW_DESTRUCTIVE_MIGRATIONS` in `Constants.EnvironmentVariables`, parsed
  as a comma-separated list of ids, trimmed, matched case-sensitively against
  pending ids.
- When a pending migration is on the list and not permitted by the variable:
  log what is pending, what would be lost, and the exact variable value that
  would permit it — then **exit non-zero before the web host starts**
  (decision 3). `Initialize` already runs before the pipeline is built
  (`Program.cs:84-100`), so this needs no restructuring.
- Same skip rule as task 2: no file-backed database, no gate.

**Acceptance:** a test asserting every id in the list resolves to a real
migration in the assembly — this is the one that catches a typo or a rename
silently disabling the gate, and it is the most valuable test in the task.
Tests for: gate fires on a listed pending migration; does not fire when the
variable names it; does not fire for an unlisted pending migration; does not
fire on a fresh database. The reverse direction is untestable by
construction — nothing can detect a destructive migration nobody listed —
which ADR 0009 already records as the honest cost.

---

### Task 4 — Retention

**Blocked by:** task 2.

ADR 0009's own Consequences flag this: unbounded backups move the failure
from data loss to disk exhaustion.

- Keep the most recent N, default 5, configurable on `StorageOptions`.
- **Prune only after a successful backup, never before**, so a failed backup
  cannot also destroy the previous one.
- Prune only files this mechanism created — match the naming from task 2
  rather than deleting whatever is in the directory.

**Acceptance:** a test that runs the backup path N+2 times and asserts N
files remain and that they are the newest ones; a test asserting a failed
backup prunes nothing.

---

### Task 5 — Cookie `SecurePolicy` defaults to `Always`

**Blocked by:** nothing.

This inverts today's default deliberately — flagged as the one contentious
decision during design, put to the operator twice, and confirmed.

`Program.cs:54` is `CookieSecurePolicy.SameAsRequest`. Forgetting the setting
in production under that default sends session cookies over plain HTTP and
nothing complains; forgetting it locally under an `Always` default breaks
login immediately and visibly.

- Bind it from configuration, defaulting to `Always`.
- `run-local.sh` sets the override — it already sets four such variables, and
  CLAUDE.md already treats bare `dotnet run` as a misconfiguration.
- `KosyncWebApplicationFactory` sets it too; roughly two dozen tests log in
  over HTTP.

**Acceptance:** a test asserting the shipped default is `Always`, and that
the existing login tests still pass via the fixture override. The point of
the first assertion is that a future edit to `run-local.sh` or the fixture
cannot quietly become the production default.

---

### Task 6 — `ForwardedHeadersOptions`

**Blocked by:** nothing.

`app.UseForwardedHeaders()` at `Program.cs:102` is a no-op: options are never
configured, so the default is `ForwardedHeaders.None` and nothing rewrites
`Request.Scheme`. Caddy sets `X-Forwarded-Proto`/`-For`/`-Host`.

- Configure `ForwardedHeaders.XForwardedProto | XForwardedFor`, with
  `KnownNetworks` bound from configuration and **defaulting to empty**
  (decision 12). Empty rewrites nothing, which is exactly today's behaviour,
  so this degrades to the status quo rather than to something permissive.
- The droplet compose names the `n8n_demo` subnet. Do not trust any proxy: a
  compromised n8n or ai-hub container on that shared network could otherwise
  spoof a client IP into the logs and `ManagementController`.
- Do not conflate this with `TRUSTED_PROXIES` (`Services/ProxyService.cs:80`),
  which is a separate hand-rolled mechanism for client-IP attribution and
  does not touch the scheme. A comment should say so; the two will be
  confused otherwise.

**Acceptance:** a test asserting an `X-Forwarded-Proto: https` header is
ignored with no configured networks and honoured with the request's network
configured.

---

### Task 7 — CI build and test

**Blocked by:** nothing.

There is no build or test workflow; `.github/workflows/` holds only
`docker-publish.yml`, which fires on `v*.*.*`. The 475 tests have never run
in CI, which makes the existing publish workflow capable of shipping an image
whose suite was never green.

- `dotnet test` on `pull_request` and on `push` to `main`.
- Branch protection requiring it — a repo setting, so it needs the operator
  or a prepared `gh api` call, not a commit.

**Acceptance:** the workflow runs green on a PR, and a deliberately failing
test blocks merge.

---

### Task 8 — Deployment artefacts

**Blocked by:** tasks 1, 5, 6 — all three add settings the compose file has
to carry.

- `docker-compose.yml` for `/opt/kosync`: the `v2.0.0` ghcr image, the
  `n8n_demo` network as **external**, **no published host port** (on this box
  a published port is public — the DO firewall allows 22, 443 and 3000 only,
  and Caddy reaches the container by name), the data bind mount,
  `mem_limit: 512m`, `restart: on-failure:3`, and the `wget` healthcheck.
- Every setting from the roadmap's per-environment table, with
  **`Portal__BaseUrl=https://kosync.notrauto.org`** — `https`, because port
  80 is firewalled shut so `http://` times out rather than redirecting, and
  ADR 0006 names this URL to a reader whose password is Provisional.
  `SyncTrace__Enabled` off. `SELF_REGISTRATION_ENABLED` unset.
- The three-line Caddy block, as a documented snippet to add to
  `/opt/n8n/Caddyfile`.
- Replace the README's Nginx Proxy Manager mention with what is actually in
  front of this: Caddy, automatic ACME, and the TLS-ALPN-01 consequence of a
  closed port 80 — including that **proxying this hostname through Cloudflare
  would break renewal silently**, up to 60 days later.
- The README's compose example still names the upstream
  `ghcr.io/jberlyn/kosync-dotnet` image (`:38`). Decide deliberately whether
  it documents upstream or this fork.

**Acceptance:** `docker compose config` validates; a reviewer can follow the
README from a clean box. Nothing here is verified until the first deploy.

---

### Task 9 — Docs

**Blocked by:** tasks 2, 3, 4 — it records what they actually did.

- **Amend ADR 0009 in place, dated** (decision 19): the backup is in-process,
  so no `sqlite3` in the image; and it runs only when migrations are pending,
  not on every start. Say what changed and why — both followed from facts
  found after it was written. The decisions themselves stand.
- **`GLOSSARY.md`: Destructive Migration**, under a new short heading (none of
  People and access / Reading and sync / Design system fits). Definition plus
  the `_Avoid_` line the file's format uses. The distinction worth pinning is
  schema-only versus row-touching — that is what people get wrong.
- **`manual-testing.md`: a droplet section**, including the two things that
  will otherwise never be checked: that `http://kosync.notrauto.org` *times
  out* rather than redirecting, and that a backup file appears in
  `<data>/backups/` after the first deploy with a pending migration. Record
  plainly that **deploy one proves none of this** — nine migrations onto an
  empty database skip both backup and gate by decision 9, so deploy *two* is
  the first real test of any of it.
- **Unrelated, found during design:** `GLOSSARY.md`'s *Design token* entry
  says the `--ks-*` tokens are "kept as aliases". #39 deleted them;
  `app.css` has zero occurrences and `DesignSystemTests.cs:57-58` asserts
  their absence. One-line fix, folded in here.

---

## The first deploy, by hand

Not a task issue — nothing in the repo can do it. Order matters (decision 17):

1. **Cloudflare A record** `kosync.notrauto.org` → `104.248.168.202`,
   **grey cloud**. Not a preference: TLS-ALPN-01 needs Caddy to be the thing
   terminating TLS, and HTTP-01 is unavailable with port 80 shut, so proxying
   would break issuance outright.
2. **Tag `v2.0.0`**, let `docker-publish.yml` build and push, then make the
   ghcr package public.
3. **`/opt/kosync/`**: compose file and a writable `data/`. Set a **strong
   `ADMIN_PASSWORD`** now — it is seed-only and applies exactly once (ADR
   0006), and between Caddy routing the hostname and the first login, anyone
   who guesses the seed can take the admin account. Unset, the seed is
   `admin`.
4. **`docker compose up -d`** and confirm healthy before anything routes to
   it.
5. **Caddy block** into `/opt/n8n/Caddyfile`, then
   `docker exec caddy caddy reload` — graceful, no downtime for n8n or
   ai-hub.
6. **Verify**: certificate issued, portal loads over HTTPS, `http://` times
   out, log in as admin and replace the Provisional Password, then point one
   device at `https://kosync.notrauto.org` and push a book.
7. **Worth one look while in there:** that the issued certificates name
   `tls-alpn-01`. The survey inferred that from Caddy's documented fallback
   rather than reading it off the box.

## Known limits of this spec

- **Deploy one proves almost nothing.** Every guarantee here is for deploy
  two onward. That ordering is why it is worth writing down.
- **The destructive list can be wrong by omission** and nothing detects it.
  Task 3's test catches typos and renames, not forgetfulness.
- **`mem_limit: 512m` is a guess** — roughly double expected usage. It wants
  checking against `docker stats` once real traffic exists, which on a
  single-reader box may take a while to mean anything.
- **Nothing here measures bcrypt on that hardware.** The single-vCPU concern
  was retired because the box serves one reader against a 300-second
  credential cache; it would return if this ever stopped being
  single-reader.
