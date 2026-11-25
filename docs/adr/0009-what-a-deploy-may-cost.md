# A deploy may cost a portal session, and may never cost reading history

Phase 5 puts this server on a box other people read from. Every deploy after
the first one lands on a database holding real reading history, so "what does
an update cost" stops being rhetorical. The two answers are deliberately
asymmetric, because the two things cost different amounts to lose.

A **portal session** is cheap. Signing in again is a password and five
seconds, and a Device holds no session at all — it authenticates per request
(ADR 0005), so a reader's actual syncing is untouched by anything here.

**Reading history is not recoverable.** It is the only thing in this database
nobody can retype: a SyncEvent records where somebody had read to at a
moment that has passed. Books, Documents and Devices all rebuild themselves
from the next push. History does not.

So: a deploy may sign everybody out. A deploy may never lose a row of
history, and may not be *able* to, rather than merely being expected not to.

## Back up before migrating, automatically

Migrations run unattended on startup (`KosyncDbInitializer`), which is the
convenience that makes local development painless and the hazard that makes
deployment dangerous. The backup therefore belongs in the same place as the
`Migrate()` call, ahead of it — not in a deploy script, where it would be a
thing to remember and a thing a second deploy path could skip.

Three details that make this less obvious than it sounds, all established by
the step 1 survey (`docs/deploy/phase5-droplet-survey.md`) and the repo-side
pass (`docs/deploy/phase5-findings.md`):

- **The database is three files, not one.** SQLite runs in WAL mode here —
  set by the EF provider and persisted in the file header, so it is a
  property of the database the droplet carries rather than of our
  configuration. A backup that copies `kosync.sqlite3` and not its `-wal`
  restores with the most recent pushes silently missing, which is the worst
  possible failure: a restore that appears to work.
- **A file copy is not a backup of a live database.** The correct mechanisms
  are SQLite's own `.backup` or `VACUUM INTO`, which produce one consistent
  file with the WAL folded in.
- **Neither was available anywhere.** `sqlite3` is absent from the runtime
  image *and* from the droplet host. The image is where this gets fixed —
  `apk add --no-cache sqlite` in the runtime stage — because it makes the
  mechanism available to the app and to a `docker exec` without installing
  a package on a host shared with ai-hub and n8n.

## A destructive migration never runs unattended

**Decision: a deploy that finds a pending destructive migration backs up,
refuses to start, and says so.** It does not apply it and it does not
silently continue.

This is a deliberate limit on roadmap step 3. Build, test, image, deploy and
verify can all be automated; "apply a migration that deletes rows" cannot,
and a pipeline that does it anyway only makes the unrecoverable thing happen
faster and with nobody watching. Four of the nine migrations in the tree
today already touch rows rather than only schema, and one of nine `Down`
methods is genuinely safe — so this project has a restore-from-backup story
and not a rollback story. A gate is what makes the backup matter.

**What counts as destructive has to be declared, not detected.** EF Core can
answer *which* migrations are pending (`GetPendingMigrations`); nothing in it
can answer whether one deletes rows, because that lives in arbitrary
`migrationBuilder.Sql` and in `Up` method bodies. So the list is maintained
deliberately in the repo, alongside the other values this codebase refuses
to scatter (`Constants.cs`), and adding a row-touching migration means adding
its name there. That is a maintenance burden with teeth — a migration that
deletes rows and is *not* on the list passes the gate — and it is still
better than the alternatives:

- **Inferring it from the migration's SQL.** Rejected: it would have to parse
  `migrationBuilder.Sql` bodies, and a wrong answer in the safe direction is
  the one failure mode that matters.
- **Treating every migration as destructive.** Rejected: nine of nine
  migrations then gate, every deploy waits for a human, and the gate becomes
  a thing people route around. A gate that always fires teaches you to
  ignore it.
- **Gating in the deploy pipeline rather than at startup.** Rejected: it
  protects the one deploy path that knows about it. The check belongs where
  `Migrate()` is, because that is what actually applies the migration —
  including when somebody runs the container by hand.

The gate is overridable by explicit configuration, because the point is to
stop it happening *unattended*, not to stop it happening. A human who has
taken a backup, read what is pending, and set the flag is the attended case
this ADR exists to require.

## Portal sessions are not protected, and that is the decision

Data Protection keys are not persisted — nothing calls `AddDataProtection()`,
so the key ring lives in the container's writable layer or in memory. Every
deploy and every restart therefore invalidates every portal session cookie
and outstanding antiforgery token.

**Accepted as-is, and recorded here so it is a decision rather than a
surprise.** Three reasons it is tolerable:

- Devices are unaffected. They hold no session, so syncing continues across a
  deploy that signs every portal user out.
- The cost is one sign-in, and the portal is not a thing readers sit in all
  day — they open it to see why something went wrong.
- **Persisting the keys would not have bought what it looks like it buys.**
  The droplet runs Docker with `Live Restore` disabled, so an unattended
  `apt` upgrade of Docker itself bounces every container. Sessions on this
  box are already mortal for reasons no change of ours controls. Fixing
  deploys alone would have produced a weaker guarantee that *read* as a
  strong one.

The honest framing is that this is the same question as ADR 0003's dead
Blazor circuit mid-deploy, one layer down, and it gets the same answer: the
reader sees something go briefly wrong, and nothing is lost.

If this becomes annoying in practice, the fix is small and local —
`PersistKeysToFileSystem` pointed at the mounted data directory, where the
keys sit beside the database and survive both a deploy and a daemon restart.
Revisit it on that evidence, not pre-emptively.

## Amended, 2026-11-24

The decisions above all stand — back up before migrating, gate a Destructive
Migration, accept unprotected portal sessions. Two mechanisms the "Back up
before migrating, automatically" section committed to turned out, during the
implementation that followed (tasks 2-4 of issue #46, recorded in issue #55),
not to be what shipped:

- **No `sqlite3` CLI was added to the runtime image, and `apk add --no-cache
  sqlite` above was never run.** `Microsoft.Data.Sqlite` — already a
  dependency of the EF Core SQLite provider this project already uses —
  turned out to expose the same online backup mechanism in process, as
  `SqliteConnection.BackupDatabase`, called directly against the connection
  `Migrate()` is about to use. That was confirmed only once the
  implementation reached for it, not known when the section above was
  written. The image needs no new package and no `docker exec` step.
- **The backup does not run on every start.** It runs only when
  `GetPendingMigrations()` reports something pending, which also removes the
  accumulation problem the Consequences section below flags as open: a
  backup now exists only immediately ahead of a schema change, so the many
  ordinary restarts that find nothing pending write nothing, and there is
  nothing from them to retain against. `DatabaseBackup` still prunes to a
  bounded number of the backups it does write, after each successful one —
  a second line of defence, not the load-bearing fix the open consequence
  below describes.

## Consequences

- The startup path gains a reason to refuse to start. That is new: today a
  container that boots is a container that ran its migrations. A deploy must
  therefore treat "exited, with a message about pending migrations" as a
  distinct outcome from "crashed".
- A backup runs on every start, including the many starts where nothing is
  pending. It is a megabyte-scale database on a box with 31 GB free, so the
  cost is noise — but backups accumulate and need a retention rule, or the
  trap moves from data loss to disk exhaustion.
- The first deploy is unaffected and proves nothing. Nine migrations apply to
  an empty database, no gate fires, and the box comes up healthy. Every
  guarantee in this ADR is for deploy two onward, which is the ordering that
  makes it worth writing down.
- `run-local.sh --fresh` and the test fixtures keep working unchanged: both
  start from an empty database, where there is nothing to back up and nothing
  destructive to gate.
