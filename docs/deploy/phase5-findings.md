# Phase 5 — the repo-side half

What this repository can tell us about deploying to the droplet, and what it
cannot. Everything below is read off `main` at `e410718`. Nothing on the
droplet was touched, looked at, or inferred from; where an answer needs the
box, it is in the last section instead of guessed at here.

Two of the roadmap's own premises turn out to be slightly wrong, and one risk
nobody has written down yet is bigger than either. Those are in items 1 and 4.

---

## 1. Where SQLite lives, and what a redeploy does to it

The path is computed from the content root and hardcoded:

- `Program.cs:37` — `var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");`
- `Program.cs:39-40` — `options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "kosync.sqlite3")}")`
- `Database/KosyncDbInitializer.cs:21-24` — creates the directory if absent, so a
  missing `data/` is never an error, only an empty database.
- `Database/KosyncDbContextFactory.cs:13` — the design-time copy, `Data Source=data/kosync.sqlite3`,
  relative to wherever `dotnet ef` is run. A second spelling of the same
  location, which is the thing that makes it a hardcoded value rather than a
  setting.

`WORKDIR /app` and `COPY --from=build-env /app/output .` put the content root
at `/app` (`Dockerfile:10-11`), so in the container the database is
`/app/data/kosync.sqlite3`.

**The roadmap's framing is right about the mechanism and the repo is worse off
than it looks, but the README is better.** There is no `VOLUME` in the
`Dockerfile` (12 lines, reproduced in full by that file — it declares nothing
but the two stages and an `ENTRYPOINT`), and there is no compose file anywhere
in the tree:

```
$ find . -name 'docker-compose*' -o -name 'compose*.y*ml'   # nothing
```

What does exist is a compose *example in the README* that mounts the directory:
`README.md:40-41`, `volumes: - </path/to/data>:/app/data`, with
`README.md:49` saying "`/app/data` will contain the database". So the
documented deployment is a bind mount, not a named volume — which is actually
the safer of the two against the trap the roadmap names (`docker compose down -v`
cannot take a bind mount with it). That example is inherited from upstream and
has never been run against this fork.

**What a `docker run`-replace deploy does today, exactly.** Three cases, and
only the first is the one anybody wants:

| Deploy | Effect on existing data |
|---|---|
| `-v /srv/kosync/data:/app/data` (README's shape) | Survives. The new container opens the same file; `Database.Migrate()` at `KosyncDbInitializer.cs:26` runs any pending migrations against it. |
| Named volume `-v kosync-data:/app/data` | Survives `docker rm`/`docker run`. Does **not** survive `docker compose down -v`, which is the trap as written. |
| No `-v` at all | **Total loss, silently.** The database is written into the container's writable layer, `KosyncDbInitializer` recreates `/app/data` on the next boot, `Migrate()` applies all nine migrations to nothing, `CreateDefaults` (`KosyncDbInitializer.cs:67-93`) seeds `admin` with a Provisional Password, and the new install looks healthy. Every Book, Device and SyncEvent is in a dangling layer. No warning is logged, because there is nothing anomalous to log — a fresh database is a legitimate state. |

The third case is one forgotten flag away at all times, and nothing in the
repository prevents it. A `VOLUME /app/data` in the `Dockerfile` would at least
convert it into an anonymous volume that `docker rm` keeps; today the floor is
"gone".

**Against the coding standard.** `CLAUDE.md:45-48` asks for values that are
settable in the DB and configurable through the UI, falling back to constants
in `Constants.cs`. The database path is none of the three. It is not
configurable (no `ConnectionStrings` section is read; `appsettings.json` has no
connection string at all), it is not a constant, and it is written out twice in
two different forms (`Program.cs:40` absolute-from-content-root,
`KosyncDbContextFactory.cs:13` relative) — the duplication the constants rule
exists to prevent. Compare how the same codebase treats every other deployment
fact: `PortalOptions`, `SyncTraceOptions`, `PasswordHashingOptions` and
`PasswordPolicyOptions` are all bound from configuration with their defaults in
`Constants.cs`, and `Models/PortalOptions.cs:8-12` goes out of its way to
explain where the single default is allowed to live and why.

So this is a standards violation, and it is the one place the standard would
have paid for itself: a `ConnectionStrings__Kosync` the droplet sets explicitly
is a value an operator can see in their compose file, whereas
`ContentRootPath/data` is a value only a reader of `Program.cs` knows. That
said — Phase 5 is an investigation, and making the path configurable is a
change, so it belongs in the implementation that follows this, not in it.

One incidental: there is no `.dockerignore`, so `COPY . ./` (`Dockerfile:4`)
pulls the developer's own `data/kosync.sqlite3` — 98 KB of real reading
history, present right now — plus `bin/`, `obj/` and `.git/` into the build
stage whenever the image is built locally. It does not reach the runtime stage
(only `/app/output` is copied) and CI never sees it (`data/` is gitignored,
`.gitignore:480`), but a locally built image carries somebody's library in an
intermediate layer.

---

## 2. Which migrations are destructive

All nine read in full. Five do nothing but add nullable columns or create
tables; four touch rows. The summary first, the detail after.

| Migration | `Up` destroys | `Down` | Usable to go back? |
|---|---|---|---|
| `20260926191003_InitialCreate` | nothing (creates 5 tables) | drops all 5 tables (`:156-169`) | **No** — total data loss by design. Only correct against an empty database. |
| `20260929190713_AddDocumentLastResetAt` | nothing (nullable column, `:14-18`) | `DropColumn` (`:24-26`) | Yes, loses only reset timestamps. |
| `20261020194356_AddBookUserId` | **yes** — `DELETE FROM "Books"` where no Document points at it (`:31-32`) | drops FK, index, column (`:84-94`) | Schema reverts; the deleted Books do not. Lossy but near-empty in practice. |
| `20261025190645_AddUserPasswordState` | transforms — `UPDATE "Users" SET "PasswordState" = 1` on every existing row (`:27`) | `DropColumn` (`:33-35`) | **One-way in a way that matters** — see below. |
| `20261025190907_AddServerSettings` | nothing (new table, `:13-23`) | `DropTable` (`:29-30`) | Lossy: discards `AdminPasswordReset.Applied`, so a still-set `ADMIN_PASSWORD_RESET` fires again on the next boot. |
| `20261031183446_AddDocumentObservedMetadata` | nothing (4 nullable columns) | drops all 4 (`:42-56`) | Schema reverts; all Observed Metadata is gone and only a device re-pushing restores it. |
| `20261101143708_AddBookCuratedMetadata` | nothing (2 nullable columns) | drops both (`:27-36`) | **Effectively one-way.** Curated Metadata was typed by a human and no push will bring it back (ADR 0007). |
| `20261103204304_AddProgressResets` | **drops a column** (`LastResetAt`, `:64-66`) after carrying its values across (`:17-20`) | recreates the column and back-fills `MAX(ResetAt)` (`:26-29`, `:72-78`) then drops the table | Partially. `Up` is lossless; `Down` keeps one reset per Document and discards the rest, as its own comment says (`:22-25`). |
| `20261105204525_AddDocumentHashUniqueIndex` | **deletes rows** (`:102-104`, `:146-150`) | index only (`:192-199`) | **No, deliberately.** |

### The known one: `AddDocumentHashUniqueIndex`

Verified against the file, step by step, in the order `Up` runs them
(`:162-169`):

1. `FindDuplicatesSql` (`:22-32`) — a temp table of (loser, loser's Book,
   survivor), where survivor is `MIN(Id)` per `(UserId, DocumentHash)` and a
   loser is any row above that minimum.
2. `CarryObservedMetadataSql` (`:45-69`) — per field, newest observation onto
   the survivor; `MetadataObservedAt` becomes the `MAX` of the set.
3. `RepointHistorySql` (`:74-82`) — every `SyncEvents` and `ProgressResets` row
   on a loser is re-pointed at the survivor. No history is deleted.
4. `KeepOneCurrentProgressSql` (`:91-100`) — of the now-several `IsCurrent`
   pointers on the survivor, the highest `Percentage` keeps it (ties to the
   most recent, then highest id).
5. `RemoveLosersSql` (`:102-104`) — **`DELETE FROM "Documents"`**. This is the
   row deletion.
6. `CarryCuratedMetadataSql` (`:117-144`) — a losing Book's curated
   title/author moves onto the survivor's Book, per field, never over an
   existing answer, and only from a Book left with no Documents.
7. `RemoveOrphanedBooksSql` (`:146-150`) — **`DELETE FROM "Books"`** for a
   losing Book now holding nothing.
8. Drops `IX_Documents_UserId`, creates the unique
   `IX_Documents_UserId_DocumentHash` (`:174-182`).

So two `DELETE`s, both guarded to the duplicate set, with every piece of
reading history moved off the doomed rows first. `Down` (`:186-200`) restores
the plain index and says in a comment that it will not restore the duplicates,
because those rows were a Book that could not sync, be read, or be deleted
(ADR 0008, "Collapsing the duplicates an existing database holds"). That is a
deliberate one-way, not an oversight.

It is also tested: `tests/Kosync.Tests/DocumentHashUniquenessMigrationTests.cs`,
over `TestSupport/MigrationTestDatabase.cs`, which migrates to the schema
*before* the migration under test, writes rows the old way in raw SQL, and runs
forwards. `BookUserIdMigrationTests.cs`, `UserPasswordStateMigrationTests.cs`
and `ProgressResetMigrationTests.cs` do the same for the other three
row-touching migrations. All four data transforms have coverage; what no test
can cover is "did this fire on real data nobody expected", which is item 3's
problem.

### The one worth more attention than it gets: `AddUserPasswordState`

`Up` sets every pre-existing User to `PasswordState = 1` (`UserChosen`),
`:27`, for the stated and correct reason that upgrading a server is not a
reason to lock people out. `Down` drops the column (`:33-35`).

Down-then-up does not round-trip, and the direction it fails in is the unsafe
one: after a revert and a re-apply, **every** User reads as `UserChosen`,
including anyone who was mid-onboarding holding a Provisional Password. ADR
0006's entire gate — the portal redirect and the sync 401 — keys off that
column, so a password an Administrator typed silently becomes a working
long-lived device credential. No data is lost; a security property is. Nothing
in the file is wrong, but "has a `Down`" should not be read as "can be rolled
back" here.

### What "one-way" means across the set

Only `AddDocumentHashUniqueIndex` is one-way because rows were deleted.
`AddBookCuratedMetadata` and `AddDocumentObservedMetadata` are one-way because
of what the dropped columns held — a human-typed title has no source to
re-derive from, and Observed Metadata needs a device to push again with
KOReader's opt-in setting still on. `AddProgressResets`'s `Down` loses all but
the newest reset per Document. `AddUserPasswordState`'s is the state-not-data
case above. `InitialCreate`'s `Down` is the whole database.

So: of nine `Down` methods, one is genuinely safe (`AddDocumentLastResetAt`),
and the honest reading of the rest is that **this project does not have a
rollback story, it has a restore-from-backup story.** Which makes item 3 the
load-bearing one.

Two mechanical notes for the deploy that will run these:

- EF Core applies each migration in its own transaction, so a failure part-way
  through migration 7 of 9 rolls *that* migration back and leaves 1-6 applied.
  "Partially migrated" is a reachable state, and the thing it is partial
  against is the image that was being deployed.
- SQLite cannot drop a column in place; the EF SQLite provider rebuilds the
  table. `AddProgressResets` and every `Down` with a `DropColumn` therefore
  need roughly twice the table's size in free space while they run. At today's
  98 KB that is nothing; it is a thing to remember only if this database ever
  gets large.

---

## 3. What must be backed up

### WAL is real, and nothing in this repo asks for it

Grep finds no `journal_mode`, no `PRAGMA`, and no `Pooling`/`Cache` keyword in
any connection string — `Program.cs:40` is a bare `Data Source=`. The repo's
only acknowledgement of WAL is a comment and three filenames in
`run-local.sh:37-39`:

```
  # SQLite runs in WAL mode, so the -wal and -shm files are part of the
  # database. Removing only the .sqlite3 leaves the other two behind.
  rm -f data/kosync.sqlite3 data/kosync.sqlite3-wal data/kosync.sqlite3-shm
```

So where does WAL come from? **The EF Core SQLite provider sets it when it
creates the file.** `Microsoft.EntityFrameworkCore.Sqlite.dll` 10.0.12 — the
version pinned at `Kosync.csproj:19` — contains the literal
`PRAGMA journal_mode = 'wal';`:

```
$ strings -a -el ~/.nuget/packages/microsoft.entityframeworkcore.sqlite.core/10.0.12/lib/net10.0/Microsoft.EntityFrameworkCore.Sqlite.dll | grep -i journal
PRAGMA journal_mode = 'wal';
```

And the live database is in WAL: bytes 18 and 19 of the SQLite header (the
file-format read and write versions) are both `2`, which is the on-disk marker
for WAL.

```
$ od -An -tu1 -j 18 -N 2 data/kosync.sqlite3
   2   2
```

That matters more than it sounds. The journal mode is **persisted in the file
header**, not re-asserted per connection, so it is a property of the database
the droplet will be carrying rather than of the code deploying to it. Nobody
can turn it off by editing `Program.cs`, and a future maintainer grepping for
`WAL` will find only a shell comment. That is worth a line in a comment or an
ADR when the deployment lands.

### A complete backup

Three files, not one:

| File | Why it is part of the database |
|---|---|
| `kosync.sqlite3` | the pages that have been checkpointed |
| `kosync.sqlite3-wal` | committed transactions **not yet** checkpointed — i.e. the newest writes, which is to say today's reading |
| `kosync.sqlite3-shm` | the shared-memory index into the WAL; not durable data, but a `-wal` without it is harder to replay and some tooling refuses the pair |

Copying only `kosync.sqlite3` yields a database rolled back to the last
checkpoint. SQLite checkpoints opportunistically (around a 1000-page WAL by
default), and this is a low-write server — a handful of rows per page-turn
batch — so the WAL can sit uncheckpointed for a long time. The failure mode is
therefore not "corrupt", which somebody would notice; it is **"restores, and
is quietly missing the last however-many pushes"**, which nobody notices until
a reader says their progress went backwards. That is the same class of silent
wrongness the roadmap flags for `Portal__BaseUrl`.

### A hot copy is not safe

`cp`/`tar`/`rsync` of a live `.sqlite3` + `-wal` is not atomic: the two files
are read at different instants, with writers free in between, and the result
can be a `-wal` that does not match the base file's change counter. SQLite's
own documentation is unambiguous that the supported ways to copy a live
database are the backup API (`sqlite3 … ".backup"`), `VACUUM INTO`, or a
`.dump`. Each of those takes a read lock and produces one self-consistent
file — and for `VACUUM INTO` and `.backup`, a single file with no sidecars,
which is exactly what you want to ship off the box.

Stopping the container first and copying all three is also correct, and is the
only approach that needs no tooling at all. It costs a window of downtime, and
on a clean shutdown the `-wal` is checkpointed and removed anyway — which is
why `data/` on this machine currently holds only `kosync.sqlite3`.

### The tool is not in the image

`sqlite3` is **not present** in the runtime image, and the `Dockerfile` installs
nothing (no `RUN apk add` of any kind — `Dockerfile:8-12` is four lines).
Verified directly against the pinned base image:

```
$ docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0-alpine \
    -c 'command -v sqlite3 || echo "sqlite3 NOT PRESENT"; apk info | grep -i sqlite || echo "no sqlite package"'
sqlite3 NOT PRESENT
no sqlite package
```

So `docker exec kosync sqlite3 … ".backup"` will not work. The options, in
rough order of how much they change:

1. **Run `sqlite3` on the host** against the bind-mounted path. Needs the
   droplet to have the package (unknown — see the last section) and needs the
   data to be a bind mount rather than a named volume, which the README's
   example already is.
2. **A sidecar container** (`alpine` + `apk add sqlite`) sharing the same
   volume, run on demand before each deploy. No change to this image.
3. **`apk add --no-cache sqlite` in the runtime stage** of the `Dockerfile`.
   Smallest operational surface, but it puts a tool in the production image
   purely for backups, and the Phase 5 brief says not to design the pipeline
   yet.
4. **An endpoint or startup hook in the app** that runs `VACUUM INTO` through
   the connection it already holds. No new packages and no new containers;
   it is also new application code, and something has to decide where the
   output goes and who may ask for it.

Not a decision this investigation should make. Worth noting that option 4 is
the only one that works identically whether the data is a bind mount, a named
volume, or neither.

---

## 4. Configuration that must be right per environment

### The roadmap's five, as the code actually reads them

| Setting | Read at | Default | Wrong value fails… |
|---|---|---|---|
| `Portal__BaseUrl` | `Program.cs:15-16` binds the `Portal` section; `Models/PortalOptions.cs:15` | `Constants.Portal.DefaultBaseUrl` = `http://localhost:8080` (`Constants.cs:215`) | **Silently.** Only a reader whose device was just refused ever sees it, in a KOReader toast, and they have no way to know the URL is wrong rather than the server. `PortalOptions.cs:29-31` falls blank/whitespace back to the localhost default, so even an empty value reads as "configured". |
| `ASPNETCORE_URLS` | the host, before any of this code | *in the container,* `ASPNETCORE_HTTP_PORTS=8080` is already set by the base image (verified: `env` inside `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` prints it), which binds all interfaces | **Loudly, if at all.** See the correction below. |
| `ADMIN_PASSWORD` | `Database/KosyncDbInitializer.cs:150-152`, straight off the process environment | `Constants.Admin.DefaultPassword` = `admin` (`Constants.cs:672`) | **Silently**, and only once. Consulted solely where an admin row is created (`:78`) or the one-shot reset fires (`:123`). Editing it later does nothing — deliberate (ADR 0006), documented (`README.md:53`), and tested (`AdminPasswordSeedingTests.cs:92`). The hazard is the reverse: first boot with it unset leaves `admin`/`admin`, Provisional, so sync is refused for everybody until somebody logs in. |
| `SyncTrace__Enabled` | `Program.cs:18-19`; `Models/SyncTraceOptions.cs:28-32` | unset = off | **Safely.** Compared case-insensitively against the string `"true"` (`Constants.cs:63`), so every typo leaves it off — and `SyncTraceOptions.cs:9-14` explains that this is why it binds as a string rather than a `bool`, which would throw per request on `Enabled=yes`. On, it announces itself once at startup at Warning level (`Program.cs:96-99`, `Constants.cs:100-102`). The risk is not a wrong value, it is a *correct* value left on: the trace writes reading positions and document hashes verbatim. |
| `SELF_REGISTRATION_ENABLED` | `Controllers/SyncController.cs:134-137`, straight off the process environment | unset = off | **Safely.** Same case-insensitive `"true"` comparison (`Constants.cs:427`); anything else refuses with the message at `Constants.cs:432-433`. |

### Two corrections to that table

**`ASPNETCORE_URLS` is not the risk the roadmap describes, in a container.**
The roadmap says it "must bind `0.0.0.0`, not localhost, or nothing outside the
container reaches it". The base image already ships `ASPNETCORE_HTTP_PORTS=8080`
and nothing in the `Dockerfile` or `appsettings.json` overrides it, so the
default container binding is all interfaces on 8080 — `README.md:114-116` says
the same. The failure mode is specifically *setting* `ASPNETCORE_URLS` to a
localhost value, which is what `Properties/launchSettings.json` does for local
`dotnet run` (`http://localhost:5181`) and what `run-local.sh:54` exists to
override. It is a local-development hazard that has already cost a session, and
it is a hazard again only if the droplet's compose file sets the variable by
hand. Worth keeping in the table, re-worded.

**The auth cookie's `SecurePolicy` will not do what the roadmap expects, even
after it is changed.** Today:

```csharp
// Program.cs:50-55
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = AppRoutes.Login;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
```

`SameAsRequest` marks the cookie `Secure` only when `Request.IsHttps`. Behind a
reverse proxy that terminates TLS, the request arriving at Kestrel is plain
HTTP, so `Request.Scheme` is `http` unless something rewrites it from
`X-Forwarded-Proto`. The only candidate is `Program.cs:102`:

```csharp
app.UseForwardedHeaders();
```

**That call is a no-op as written.** `ForwardedHeadersOptions` is never
configured — grep across the whole tree finds exactly three hits for
`ForwardedHeaders`, all in `Program.cs`, two of them a comment — and the
default `ForwardedHeadersOptions.ForwardedHeaders` is `ForwardedHeaders.None`.
The ASP.NET Core documentation states this twice over: "if no
`ForwardedHeadersOptions` are specified … the default headers to forward are
`ForwardedHeaders.None`. The `ForwardedHeaders` property must be configured
with the headers to forward."

So three consequences, which compound:

1. **Today:** the session cookie is never `Secure`, on any deployment.
2. **After flipping to `CookieSecurePolicy.Always`** — the roadmap's stated
   pre-deployment action — the cookie *is* marked `Secure`, which is correct
   and works, because `Always` does not consult the scheme. So that specific
   fix is not blocked by this. Good.
3. **But every other scheme-dependent behaviour stays wrong**: redirects
   generated by the framework (`LoginPath` among them), `Request.Scheme` in any
   absolute URL, and the Blazor circuit's own negotiated endpoint. These are
   the things that fail as `http://` links on an `https://` site.

The fix is one of: configure the options explicitly with
`ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto` plus
`KnownProxies`/`KnownIPNetworks` for the droplet's proxy, or set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, which the host honours (the config
key `ForwardedHeaders_Enabled` is present in
`Microsoft.AspNetCore.dll` 10.0.13) and which enables XFF+XFP while *clearing*
`KnownProxies` and `KnownNetworks` — convenient, and trusting of whoever can
reach the port. Either way this is an **environment-sensitive setting the
roadmap's table does not have**, and it is coupled to the proxy survey in
item 1 of the phase.

Note that client-IP logging is unaffected and will keep working: `IPService`
reads `X-Forwarded-For` itself, gated on `TRUSTED_PROXIES` via `ProxyService`
(`Services/IPService.cs`, `Services/ProxyService.cs`, documented at
`README.md:87`). So `TRUSTED_PROXIES` is a *sixth* missing row in the
roadmap's table — it governs whether log lines name the reader's device or the
proxy, and an unset value fails silently by logging the proxy's address for
everybody.

### The bigger one the table misses: Data Protection keys

Nothing in this repository calls `AddDataProtection()` — grep for
`DataProtection` and `PersistKeysTo` across every `.cs`, `.json` and `.razor`
returns nothing. So key management is the framework default, which on Linux
persists keys to `$HOME/.aspnet/DataProtection-Keys`, and in a container that
directory must be on a volume or the keys are regenerated on every start.

Both of the plausible container identities break it:

- **Default (root).** The image runs as `uid=0`, `HOME=/root` (verified). Keys
  land in `/root/.aspnet/DataProtection-Keys`, inside the container's writable
  layer — i.e. discarded on exactly the `docker run`-replace deploy this phase
  is about.
- **`user: 1000:1000`, which is what `README.md:46` tells operators to use.**
  There is no `/etc/passwd` entry for 1000, so Docker sets `HOME=/`, and `/` is
  not writable by that uid:

  ```
  $ docker run --rm --user 1000:1000 --entrypoint sh \
      mcr.microsoft.com/dotnet/aspnet:10.0-alpine \
      -c 'echo HOME=$HOME; mkdir -p $HOME/.aspnet/DataProtection-Keys && echo "WRITE OK" || echo "WRITE REFUSED"'
  HOME=/
  mkdir: can't create directory '//.aspnet/': Permission denied
  WRITE REFUSED
  ```

  With no writable key location the data-protection system falls back to keys
  held in memory for the lifetime of the process.

The effect either way: **every deploy and every container restart invalidates
every portal session cookie and every outstanding antiforgery token.** Readers
are signed out and have to log in again; a form posted from a page rendered
before the restart fails its antiforgery check. Devices are untouched — they
authenticate per request from their Stored Credential and hold no session
(GLOSSARY: Sign Out) — so this is a portal-only annoyance, and arguably an
acceptable one. But it is the same shape as the roadmap's own "what happens to
an open Blazor circuit mid-deploy" bullet: it should be a decision, not a
surprise, and it is currently not written down anywhere.

It also sharpens the `user:` question. If the droplet uses the README's
`user: 1000:1000` against a bind mount, that uid must own the host directory or
SQLite cannot write at all — a loud failure, which is fine — and `HOME` has to
be set deliberately (`-e HOME=/app/data`, say) for keys to persist at all.

### Everything else that is environment-sensitive

| Setting | Read at | Default | Notes |
|---|---|---|---|
| `Passwords__WorkFactor` | `Program.cs:9-10`; `Models/PasswordHashingOptions.cs:8` | 12 (`appsettings.json:10`, `Constants.cs:180`) | Shipped in `appsettings.json`, unlike the others. Out-of-range values are clamped to bcrypt's 4-31 (`Constants.cs:184-186`) rather than throwing. Raising it on a small droplet costs CPU on every uncached sync auth. |
| `Passwords__VerifiedCredentialCacheSeconds` | same | 300 (`appsettings.json:11`) | How long a verified credential skips the KDF. A long value is a second credential store; a zero runs bcrypt on every page-turn batch. |
| `Passwords__MinimumLength` | `Program.cs:12-13`; `Models/PasswordPolicyOptions.cs:19` | 8 (`Constants.cs:198`) | **Fails loudly and badly.** Deliberately absent from `appsettings.json`, and `SyncTraceOptions.cs:9-14` notes in passing that this one binds straight to `int` and so *throws* on an unparseable value — contradicting its own comment. A typo'd `Passwords__MinimumLength=eight` faults the options binding wherever the policy is first resolved. |
| `SyncTrace__MaxBodyCharacters` | `Models/SyncTraceOptions.cs:37-48` | 2048 (`Constants.cs:68`) | Safe: unparseable falls back, negative clamps to 0. |
| `ADMIN_PASSWORD_RESET` | `KosyncDbInitializer.cs:106` | unset | Acted on once per distinct value, recorded as a Server Setting (`Constants.cs:680`). Safe to leave in a compose file — that is the design (ADR 0006). |
| `SINGLE_LINE_LOGGING` | `Program.cs:71`, exact `== "true"`, case-**sensitive** | unset | The one odd one out: every other flag in the codebase compares case-insensitively. `SINGLE_LINE_LOGGING=True` silently does nothing. Cosmetic, but it is also the only env var in the app that is not named in `Constants.EnvironmentVariables` (`Constants.cs:645-662`). |
| `TRUSTED_PROXIES` | `Services/ProxyService.cs` | unset | See above. Governs whose IP appears in logs. |
| `AllowedHosts` | `appsettings.json:8`, `"*"` | `*` | Host filtering off. Fine behind a proxy that only routes `kosync.notrauto.org`; worth a thought once that proxy is known. |
| `ASPNETCORE_ENVIRONMENT` | the host | `Production` in the container (unset) | `appsettings.Development.json` only changes log levels, so this is low-stakes here. `run-local.sh:53` defaults it to `Development`. |

### Other things hardcoded for local HTTP

Beyond `SecurePolicy`, the search comes up almost empty, which is the good
answer: no `UseHttpsRedirection`, no `UseHsts`, no hardcoded `http://` in any
route or component, and `AppRoutes.cs` is relative paths throughout. The portal
URL in a sync refusal is the one absolute address the server ever emits, and it
is configuration (`PortalOptions`). The only other local-HTTP assumptions are
the two local-development files that exist to be local — `launchSettings.json`
and `run-local.sh` — and `Constants.Portal.DefaultBaseUrl`, whose comment
(`Constants.cs:208-215`) already says it is only right for somebody running the
server on the machine they are reading it from.

---

## 5. CI reality

`.github/workflows/docker-publish.yml` is the only file under `.github`:

```
$ find .github -type f
.github/workflows/docker-publish.yml
```

It triggers on tag pushes only (`:3-6`), and its single job does checkout →
QEMU → Buildx → registry login → metadata → `docker/build-push-action` (`:19-49`).
There is no `dotnet test` step, no `dotnet build` step, and no other trigger —
no `push` to a branch, no `pull_request`, no `workflow_dispatch`.

**Confirmed: the suite never runs in CI.** Nothing has ever run it except a
human at a terminal. The only check a tag push performs is that
`dotnet publish` inside the `Dockerfile` compiles, which catches syntax and
type errors and nothing else — and it compiles only `Kosync.csproj`, which
excludes `tests/**` (`Kosync.csproj:9-13`), so the test project is not even
built. `Kosync.slnx` knows about it; the image build does not.

For roadmap item 3 — "can GitHub Actions own it end to end" — this means the
honest starting position is *less* than the roadmap implies. "Build, test,
image, deploy, migrate, verify" is six stages and exactly one of them exists.
Three things follow:

1. **Test has to come before image, and it is new work.** It is not
   reorganisation of an existing pipeline; the pipeline has no concept of the
   test project. `dotnet test` over `Kosync.slnx` on `push` and
   `pull_request` is the cheap half, and it is worth having whether or not
   anything else in Phase 5 happens, because it is the only part that does not
   depend on the droplet survey.
2. **The tag trigger is the deploy trigger by accident.** `v*.*.*` publishing
   an image is the whole of today's release process, and a pipeline that also
   deployed and migrated on that trigger would mean a mistyped tag reaches
   production data. The migration safety question (item 2) and the CI question
   are the same question once the pipeline can run `Migrate()` on the droplet.
3. **A smoke test exists to build on.** `GET /healthcheck` returns
   `{"state":"OK"}` unauthenticated (`Controllers/SyncController.cs:74-81`),
   as does `GET /` (`:68-72`). Both are genuinely useful post-deploy probes,
   with one caveat worth stating: migrations run inside
   `KosyncDbInitializer.Initialize` before `app.Run()` (`Program.cs:84-100`,
   `KosyncDbInitializer.cs:26`), so a failed migration means the process never
   serves and the probe never answers. A 200 from `/healthcheck` therefore does
   prove "migrations applied and the app booted" — but a *timeout* conflates
   "migration threw", "container is still starting" and "proxy misrouted", and
   the roadmap's question about what to do when the smoke test fails after a
   migration has applied needs the container logs to tell those apart.

---

## Questions only the droplet can answer

Everything above came out of the repository. These did not, and no amount of
reading this tree will settle them. `survey-droplet.sh` in this directory
collects the evidence for all of them; it is read-only and safe to read before
running.

1. **What is in front of ai-hub and n8n.** Both answer on subdomains, so
   something terminates TLS and routes by host — but the README's Nginx Proxy
   Manager mention is inherited from upstream (`docs/ROADMAP.md:764-766`) and
   nobody has confirmed it describes this box. Whether it is nginx, Caddy,
   Traefik or NPM changes the config format, where WebSocket upgrade settings
   live, and who issues the certificate.
2. **Whether that proxy forwards WebSocket upgrades and tolerates idle
   connections.** ADR 0003 (`:81-84`) makes this a hard requirement, not a
   preference: without it the portal's circuit drops and the page dies. A
   working ai-hub says nothing either way — ai-hub is a Next.js app and may
   never need an upgrade.
3. **What that proxy sends as `X-Forwarded-Proto`, and from which IP.** Needed
   to configure `ForwardedHeaders` correctly rather than by setting
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and trusting everything. The
   proxy's address on the Docker network is also the value `TRUSTED_PROXIES`
   wants.
4. **How TLS is issued and renewed.** certbot timer, acme.sh cron, Caddy's
   automatic issuance, or NPM's built-in Let's Encrypt client — and whether
   adding `kosync.notrauto.org` is a dashboard click, a config file plus a
   reload, or a DNS-01 dance.
5. **Whether Docker is there, what it already runs, and whether ai-hub or n8n
   hold volumes this might collide with.** `docker volume ls` and
   `docker compose ls` answer it. n8n in particular is usually deployed with
   its own compose project, and whether kosync joins it or gets its own matters
   for `down -v` blast radius.
6. **Whether `sqlite3` exists on the host.** It does not exist in the runtime
   image (proved above), so backup option 1 in item 3 lives or dies on this
   one fact. `command -v sqlite3` settles it.
7. **Disk headroom, and where.** Not just `df -h /` — the free space on the
   filesystem that will hold the bind mount *and* the backups, given that
   Docker images on this box already consume some of it and a column-dropping
   migration transiently needs twice the table.
8. **Memory headroom.** Blazor `InteractiveServer` holds per-circuit state
   server-side, and bcrypt at work factor 12 is deliberately expensive. On a
   droplet already running ai-hub and n8n, `free -h` decides whether work
   factor 12 and the 300-second credential cache (`appsettings.json:10-11`)
   are the right numbers here.
9. **DNS: does `kosync.notrauto.org` exist yet,** and is `notrauto.org`
   managed somewhere that makes adding a record a two-minute job.
10. **What uid the existing deployments run containers as, and what owns
    `/srv`-or-wherever.** Decides the `user:` line, which decides whether
    SQLite can write and whether Data Protection keys can persist (item 4).
11. **Whether anything already backs this box up.** If there is a DigitalOcean
    snapshot schedule or an existing cron, the "back up before migrating"
    requirement may be a hook into something that exists rather than a new
    mechanism. Worth knowing before designing one.
