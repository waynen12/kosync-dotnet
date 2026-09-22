# kosync-dotnet

A self-hosted sync server for KOReader reading progress, expanding into a
dashboard for monitoring devices, diagnosing sync issues, and eventually
book metadata and AI reading features.

## Language

**Book**:
The work a human means by "the book I'm reading" — a specific novel,
regardless of which file or edition it came from. One or more Documents
belong to a single Book. Every Document belongs to exactly one Book, even
before any automatic matching exists — a brand-new Document gets its own
Book until deliberately merged into another. When a Book has multiple
Documents, its overall progress is the furthest-along current progress of
any of them — whichever device has read deepest into the book wins, not
whichever synced most recently.
_Avoid_: Document, title

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
