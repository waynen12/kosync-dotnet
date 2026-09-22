# Introduce Book as distinct from Document from the first schema

KOReader identifies documents by a hash of either file content (default) or
filename — never by title, author, or any other metadata. This hash
fragments across file conversions, DRM stripping, or tools like Calibre
re-pushing a book to a device, so the same physical book can produce
multiple different Document hashes across a user's devices. We introduce
`Book` as a distinct concept from the first EF Core migration — a
`Document` always belongs to exactly one `Book` — even though automatic
Book-matching via metadata doesn't ship until a later phase. Retrofitting
an identity layer underneath an already-populated `Document` table later
would be far more disruptive than carrying one extra join from day one.

## Considered Options

- Defer `Book` to the metadata phase, treat `DocumentHash` as book identity
  until then. Rejected: the dashboard's core purpose — showing which
  devices are in sync on which books — would be actively wrong in the
  meantime, showing duplicate phantom books for anything KOReader hashed
  differently across devices.
- Introduce `Book` now, with matching entirely manual until metadata work
  lands. Chosen.

## Consequences

- Every new `Document` auto-creates its own 1:1 `Book`; merging Books that
  are actually the same title is a manual action until a later phase
  automates it.
- Regression protection (see sync-progress behavior) is intentionally
  scoped to a single `Document`, not across a `Book`'s merged Documents,
  since percentage isn't reliably comparable across different
  hashes/editions.
