# kosync-dotnet

A self-hosted sync server for KOReader reading progress, and a portal where
each reader logs in with those same sync credentials to see their own books,
devices, and sync issues.

## Language

### People and access

**User**:
A person with sync credentials. The same username and password serve two
purposes: their KOReader Devices authenticate with it to sync, and the
person themselves logs into the portal with it to see their own reading.
Every Book, Document, and Device belongs to exactly one User, and a User
only ever sees their own.
_Avoid_: account, reader, operator, sync user (all one thing — a User)

**Administrator**:
A User who can additionally manage other Users — creating them, resetting
their passwords, deactivating or deleting them, and granting or revoking
this capability itself. May also delete another User's Document, which is
a diagnostic power rather than an account one. A handful exist at most.
Being an Administrator is a capability a User holds, not a separate kind
of account, and it confers no ability to *read* another User's reading.
_Avoid_: admin as a distinct entity, operator, owner

**Tenant**:
The one User every database read is scoped to for the duration of an
operation. Set explicitly at the start of each operation — a sync request
from its `x-auth-user` header, a portal page from the circuit's
authentication state — because the callers that know "who" know it by
incompatible means (ADR 0004). No Tenant set means no rows, not all rows:
the three paths that legitimately run outside any User (startup seeding,
the LiteDB import, `dotnet ef` at design time) say so by name instead.
_Avoid_: current user (ambient-sounding, and the thing ADR 0004 rejected),
customer, organisation (there is no grouping above a User)

**Provisional Password**:
A password chosen for a User by somebody else — by an Administrator
creating the account, or by the installer for the very first `admin`. It
is a correct password and identifies the User, but grants nothing: both
the portal and the sync API refuse it until the User replaces it with one
of their own. A User holding one has an account that exists and does
nothing.
_Avoid_: temporary password, temp credentials, default password (only the
first admin's is a default; the rest are handed over deliberately)

**Auth Key**:
What a KOReader Device actually puts on the wire in place of a password:
`MD5(password)`, sent as the `x-auth-key` header. The protocol fixes this,
so the server never sees the plaintext and the MD5 cannot be removed. It
is a password-equivalent, not password storage — the Stored Credential
wraps it (ADR 0005). The portal, which does see plaintext, MD5s it first
so both paths verify the same value.
_Avoid_: password hash, password (it is neither — it is the credential the
protocol carries)

**Stored Credential**:
What the `User.PasswordHash` column holds: `bcrypt(md5(password))` — bcrypt
over the Auth Key (ADR 0005). One column serves both the sync API and the
portal. Verifying one is a deliberately slow KDF rather than a comparison,
so a Stored Credential that has just been verified against a given Auth Key
is remembered briefly in memory and the KDF skipped on repeat sync pushes.
_Avoid_: password hash when the distinction from the Auth Key matters

**Self-Registration**:
A Device creating its own User through KOReader's own "Register" button
(`POST /users/create`), with no Administrator involved. The one route whose
password is the person's own from the start rather than Provisional, which
is why it is off unless the installer turns it on with
`SELF_REGISTRATION_ENABLED` (ADR 0006): open, it is a second way an account
can come into existence, and one the forced first password change never
touches.
_Avoid_: sign-up, registration unqualified (the Administrator route is also
creating a User — the distinguishing part is *self*)

**Server Setting**:
A durable fact about this server, keyed by name — not configuration.
Configuration arrives fresh on every boot from environment variables and
`appsettings`, so it cannot remember anything; a Server Setting is what
survives a restart, which is the only thing that can make a once-only
action happen once. The first is the `ADMIN_PASSWORD_RESET` value already
acted on, because that variable stays set in the compose file that
triggered it.
_Avoid_: config, option, preference (all three suggest an operator sets it;
these are written by the server, about itself)

### Reading and sync

**Book**:
The work a human means by "the book I'm reading" — a specific novel,
regardless of which file or edition it came from. One or more Documents
belong to a single Book. Every Document belongs to exactly one Book, even
before any automatic matching exists — a brand-new Document gets its own
Book until deliberately merged into another. When a Book has multiple
Documents, its overall progress is the furthest-along current progress of
any of them — whichever device has read deepest into the book wins, not
whichever synced most recently. A Book no Document belongs to is removed
rather than kept: there is nothing left to read, and it would show in its
owner's library as a book that is not there. A Book *has* a title; a
title is not a Book.
_Avoid_: Document, title (as a synonym for the Book itself)

**Document**:
A specific file as KOReader identifies it — keyed by a hash KOReader
computes locally (by default, a partial MD5 over sampled byte ranges of
the file's content; optionally, an MD5 of the filename instead). This hash
is sensitive to file conversion, DRM stripping, or renaming, so the same
Book can be represented by multiple Documents across devices when their
hashes diverge. A User owns at most one Document per hash and the database
enforces it (ADR 0008) — a second one is not another file, it is the same
file twice, and it used to break that Book permanently. Two *Users* with
the same hash is two Documents and nothing wrong.
_Avoid_: Book, hash (on its own)

**Duplicate Document**:
Two Documents with the same hash owned by one User — the same file stored
twice, which the database refuses outright (ADR 0008). Not a Split Book
and not a Merge Suggestion: those are different *files* of one Book, and
both are ordinary states of a healthy library. This one is a defect, and
while a pair existed that Book could not sync, be read, or be deleted.
_Avoid_: Split Book, duplicate book, merge candidate

**Device**:
A single KOReader installation, identified by the opaque `device_id` it
sends with every sync. `device` (the display label, e.g. "Kobo_nova") is
mutable and not part of its identity. A reinstall or cleared settings
produces a new Device — there is no automatic merging with a prior one.
_Avoid_: device_id alone

**SyncEvent**:
An append-only record of a single progress push from a Device for a
Document. A Document's current progress is its most recent SyncEvent that
didn't regress the percentage — a lower-percentage push is still recorded
in history but never becomes current. There is no automatic "this was a
deliberate restart" detection; a reader who wants to restart a book
deletes its progress explicitly rather than the server guessing at intent.
_Avoid_: sync, push (too generic alone)

**Progress Regression**:
A push that was refused promotion when it arrived, because it was behind
the Document's current progress at that moment. A property of one push at
one instant, and permanent once true — not a property of the Document, and
not the same thing as "is not the current progress now", which is also
every ordinary forward push that a later one has since superseded. Only
one SyncEvent per Document holds the current-progress pointer, so most of
a healthy Document's history is not current and none of it regressed.
_Avoid_: not current, superseded, stale (all describe the pointer, not the
verdict on the push)

**Split Book**:
A Book whose Documents disagree on progress across devices because
KOReader gave them different hashes for what is, to the reader, the same
book. The signal that two Documents need a manual merge.
_Avoid_: sync issue (too generic — this is one specific kind)

**Observed Metadata**:
The filename, title, and authors a Device reported for one Document on a
progress push. An observation about a single file, not a statement about
the Book — two Documents on the same Book can report different titles.
Always optional: the reader must have opted into sending it, and only
recent KOReader versions can.
_Avoid_: metadata (unqualified), book metadata, pushed title

**Curated Metadata**:
The title and author a User set on a Book deliberately — typed by hand,
or filled from a lookup. Outranks Observed Metadata always, because a
person who typed a title is asserting they know the correct answer, while
a push only reports what one device happened to extract from one file.
_Avoid_: metadata (unqualified), override, manual title

**Merge Suggestion**:
Another of the same User's Books that reports an identical title and
author to this one — case-insensitive and trimmed, on both fields, and
only among Books that have metadata at all. A proposal shown inline on
both Books, never an action: merging stays something a reader chooses,
because a merge is transactional but not undoable. The narrowness is the
point, not a first pass at fuzzy matching.
_Avoid_: duplicate, a match or a candidate (as names for the thing —
matching is how one is found, not what it is), auto-merge (nothing here
merges anything)

**Document Label**:
What a Document is called on the two detail pages — the filename a Device
observed for it, or the first characters of its hash where none was
observed. A Document *is* a file, so its filename is the most honest name
for it. Never replaces the hash: both are on the page, because the hash is
what tells a Split Book's files apart. A Document has no curated tier and
no `Book #N`-style fallback; its hash is always there to name it with.
_Avoid_: title (that is the Book's), filename (the observation, not the
resolved label), name

**Progress Reset**:
A deliberate action that clears a Document's current-progress pointer so
a reader can restart a book. Recorded once per occurrence, not as a
SyncEvent — a SyncEvent is specifically a push from a Device, and a reset
isn't one, even though it still needs to be visible in the Document's
history as the distinct event it is. A Document reset twice holds two
records and shows both.
_Avoid_: SyncEvent (for this specific action), restart (fine as a verb,
but the record itself is a Progress Reset)

**Sync Trace**:
A log of the whole exchange with a sync client — what each request carried
and what went back — that an operator switches on to diagnose a Device
that will not sync, and off again afterwards. Not the endpoints' own
logging and not a replacement for it: that reports what an action decided,
which means it cannot see a request that reached no action at all. The
Trace is specifically for those: a path this server does not serve, a verb
it does not accept, a body it would not bind. Nothing persists it and
nothing in the portal shows it; it is log output, and a Device's reading
positions and document hashes are in it verbatim.
_Avoid_: audit log (nothing here is kept or accountable to anything),
request log (too generic — this is the sync surface only), debug logging
(names a level, not this thing)

### Design system

**Hub design language**:
The ai-hub look and feel this portal adopts — shadcn's achromatic oklch
surface ramp, Geist / Geist Mono, the orange→indigo gradient, `#F57C00` as
the single accent, `--radius: 0.625rem`. Parity is *branding* only: ai-hub
and kosync are separate apps that share no components, no accounts and no
interaction, so the hub arrives here as a hand-written stylesheet rather
than a dependency. Everything belonging to it is namespaced `hub-`.
_Avoid_: ai-hub (that is the other application, not this look), theme (a
Theme here is light/dark)

**Design token**:
A CSS custom property in `wwwroot/css/app.css` that names a design
decision — a colour, radius, gutter or font stack — so rules reference the
decision instead of the value. `--hub-*` are the live tokens; `--ks-*` are
the pre-hub names, kept as aliases onto them so the never-prototyped
detail pages keep working (ADR-adjacent; see docs/ROADMAP.md).
_Avoid_: variable, constant (`Constants.cs` is the C# helper, a different
thing)

**Theme**:
The reader's light/dark preference, a sticky per-user setting applied by
`wwwroot/js/theme.js` as `[data-theme="dark"]` on the root element. Purely
client-side and deliberately so — it must never round-trip to the server
(ADR 0003).
_Avoid_: dark mode class, `.dark` (ai-hub's switch, not ours)
