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
this capability itself. A handful exist at most. Being an Administrator is
a capability a User holds, not a separate kind of account, and it confers
no ability to read another User's reading.
_Avoid_: admin as a distinct entity, operator, owner

**Provisional Password**:
A password chosen for a User by somebody else — by an Administrator
creating the account, or by the installer for the very first `admin`. It
is a correct password and identifies the User, but grants nothing: both
the portal and the sync API refuse it until the User replaces it with one
of their own. A User holding one has an account that exists and does
nothing.
_Avoid_: temporary password, temp credentials, default password (only the
first admin's is a default; the rest are handed over deliberately)

### Reading and sync

**Book**:
The work a human means by "the book I'm reading" — a specific novel,
regardless of which file or edition it came from. One or more Documents
belong to a single Book. Every Document belongs to exactly one Book, even
before any automatic matching exists — a brand-new Document gets its own
Book until deliberately merged into another. When a Book has multiple
Documents, its overall progress is the furthest-along current progress of
any of them — whichever device has read deepest into the book wins, not
whichever synced most recently. A Book *has* a title; a title is not a
Book.
_Avoid_: Document, title (as a synonym for the Book itself)

**Document**:
A specific file as KOReader identifies it — keyed by a hash KOReader
computes locally (by default, a partial MD5 over sampled byte ranges of
the file's content; optionally, an MD5 of the filename instead). This hash
is sensitive to file conversion, DRM stripping, or renaming, so the same
Book can be represented by multiple Documents across devices when their
hashes diverge.
_Avoid_: Book, hash (on its own)

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

**Progress Reset**:
A deliberate action that clears a Document's current-progress pointer so
a reader can restart a book. Recorded as a timestamp on the Document
itself, not as a SyncEvent — a SyncEvent is specifically a push from a
Device, and a reset isn't one, even though it still needs to be visible
in the Document's history as the distinct event it is.
_Avoid_: SyncEvent (for this specific action), restart (fine as a verb,
but the record itself is a Progress Reset)

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
