# A User owns at most one Document per hash, enforced by a unique index

A `DocumentHash` identifies a file to the one User who owns it, so a second
`Document` with the same hash for the same User is not another file — it is
the same one, twice. `Devices` has carried a unique index on
`(UserId, DeviceId)` from the first migration for exactly this reason;
`Documents` had only plain, non-unique indexes, and nothing stopped the pair
being written.

Three call sites read a Document by hash with `SingleOrDefault` — the
progress push, `GET /syncs/progress/{hash}`, and
`DELETE /manage/users/documents` — so a pair was not a cosmetic duplicate.
It was permanent breakage of one Book: every one of those three threw
`InvalidOperationException` on that hash forever, which meant it could never
sync again, never be read, and not be deleted through the API. Recovery
needed manual SQL. The window was narrow — two Devices pushing the same
**brand-new** hash in the same instant, both reading nothing and both
inserting — but "sync two devices after a trip" is when it opens, and since
ADR 0007 a pair also splits a Book's Observed Metadata across two rows, so
whichever one a reader happened to look at might show nothing.

The invariant is therefore enforced by a **unique index on
`(UserId, DocumentHash)`**, and the three readers stay `Single`. A `First`
would answer from one of two rows and let a violated invariant show up as a
reader quietly seeing the wrong progress; `Single` is provably safe with the
index in place, and loud if the index is ever lost.

## Collapsing the duplicates an existing database holds

The index cannot be created over an existing pair, so the
`AddDocumentHashUniqueIndex` migration collapses every set it finds first.
The rule, in full:

- The **oldest** Document of the set survives — the lowest `Id` is the row
  the race inserted first, so it is the one later pushes found and read
  against until the pair broke it, and it carries the longer history.
- Every **SyncEvent** and **Progress Reset** on the losing rows is repointed
  at the survivor. They are real pushes and real restarts of that file, and
  deleting them would delete reading history.
- The survivor keeps **one current-progress pointer**: the furthest-along of
  the pointers the duplicates each held, ties going to the most recent.
  Within a single Document, progress becomes current only by being at least
  as far along as what it replaced, so the highest percentage is the only
  pointer consistent with the merged history.
- **Observed Metadata** is taken per field, newest observation wins — ADR
  0007's most-recent-observation tie-break, applied across rows that should
  have been one. Per field, because a device that reported a title but no
  author said nothing about the author (`ObservedMetadataRule`).
  `MetadataObservedAt` becomes the newest of the set.
- A losing Document's **Book** is left with no Documents and is removed, the
  way `RemoveOrphanedBooks` removes one after a Document delete. Its
  **Curated Metadata** moves onto the survivor's Book first, per field and
  never over an answer that Book already has — the same rule a Book merge
  follows, and for the same reason: a human typed it, no Document observed
  it, and no push will bring it back (ADR 0007).
- A losing Document's Book that still holds **other** Documents is a Book
  somebody merged this file into. It stays, with its curated title: the
  collapse repairs one duplicated file, it does not undo a merge.

The curated-metadata step is deliberately more than `RemoveOrphanedBooks`
does. That path runs after `DELETE /manage/users/documents`, where a reader
asked for the Document to go and the Book going with it is the answer they
asked for. Nobody asked for the duplicate row, so the collapse must not
charge them a title they typed for a defect they did not cause.

`Down` restores the plain index and does **not** restore the duplicates. The
rows it removed were a Book that could not sync, be read, or be deleted;
putting them back would break it again.

## Losing the race now that the database refuses the second insert

The push path catches the refused insert and re-reads, rather than letting it
500 (`DocumentRegistry`). The losing push then lands as a SyncEvent on the
Document that won — including its Observed Metadata — so nothing about where
somebody had read to is lost, and the ordinary regression rule decides which
of the two pushes is the current progress.

This is why a brand-new Document is saved on its own, before the SyncEvent
that follows: the collision has to surface while the push can still be
redirected, which it cannot be once a SyncEvent has been built pointing at a
rejected row. The cost is that a push failing between the two saves leaves a
Document with no progress on it — empty, not broken, and the next push for
that hash records against it.

## Considered Options

- **Leave the readers on `SingleOrDefault` and add no index.** Rejected:
  that is the state that produced the defect.
- **Change the three readers to `First` and leave the pair possible.**
  Rejected. It stops the exception without stopping the corruption, and
  turns "this Book is broken" into "this Book shows one of two answers",
  which nobody reports.
- **Serialise the push path instead of indexing** (a lock around
  check-then-insert). Rejected: it protects one process against itself and
  nothing else, while the index holds regardless of what writes the row.

## Consequences

- A second Document with the same hash is now a database error rather than
  accepted data, which is what makes `Single` at the three readers a
  statement about the schema instead of a hope.
- `IX_Documents_UserId` is dropped: SQLite can serve a lookup on the leading
  column of the composite index, which is all the plain one was for.
- The push path writes twice when it creates a Document, and the second
  write is where a push can still be lost (as it already could be).
- Two Users reading the same file remains two Documents with the same hash
  and nothing wrong — the invariant is per User, like the Device one.
