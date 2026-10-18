# Pushed metadata is observed per Document; edited metadata is curated per Book

KOReader can optionally send `metadata` (filename, title, authors) with a
progress push. That metadata describes one specific file, and a Book may
span several Documents whose hashes diverged across conversions or
editions — so two Documents on the same Book can legitimately report
different titles. Pushed metadata is therefore stored on the **Document**,
as an observation about a file, and never written to the Book.

A User can also set a Book's title and author by hand, and later fill them
from an ISBN or title lookup. That is **curated** metadata and lives on the
**Book**. A Book's displayed title resolves curated, then derived from its
Documents' observations, then the `Book #N` fallback.

Curated metadata always wins, and an incoming push never overwrites it. A
push reports whatever that device's KOReader happened to extract from a
file; a human who typed a title is asserting they know the correct answer.

When a Book has no curated title and its Documents observed different
ones, the **most recent** observation wins. The Book-progress rule
("furthest-along Document wins") deliberately does not carry over: that
rule works because progress is a quantity where further is better, whereas
no sense exists in which a title from a further-read file is more correct.

## Consequences

- Metadata is an enrichment, never a guarantee. KOReader's "send document
  metadata" setting is opt-in and only exists from 2026.07, so Documents
  with no observed metadata are a permanent case and `Book #N` stays a
  real fallback rather than a transitional one. Because that setting is off
  by default, a Book with no metadata carries an inline hint telling the
  reader to enable it — without that, the feature looks broken rather than
  switched off.
- Observed filename becomes a Document's human-readable label, falling
  back to a truncated hash. A Document *is* a file, so its filename is the
  most honest name for it. This applies to the **detail pages** only: the
  dashboard's table shows a Document *count* and does not label individual
  Documents at all, so there is nothing there for a filename to replace.
  The full hash stays visible on the detail pages regardless — it is what
  you actually need to diagnose a Split Book.
- Merge *suggestions* require an exact match — case-insensitive and
  trimmed — on both title and author, among Books belonging to one User
  that have metadata at all. Title-only matching pairs up books in a
  series and anything called "Collected Stories"; fuzzy matching is its
  own project and should be decided on real data gathered from the exact
  rule. The roadmap deferred automatic matching precisely because false
  positives were too expensive, so near-zero false positives is what earns
  the feature the right to exist.
- `authors` arrives as a single string, not a list, and is stored verbatim
  rather than parsed into individual authors.
- Merging two Books never has to destructively reconcile conflicting
  titles, because the observations stay attached to their own Documents.
- Having real title and author signal is what the roadmap said automatic
  Book-matching was waiting for. Matching may now *suggest* merges, but
  must not perform them: a merge is transactional but not undoable, so a
  false positive is manual cleanup.
