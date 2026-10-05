# Roadmap: Dashboard & Beyond

Status: **Phases 0-2 implemented, code-reviewed, and merged to `main`**
(issues [#2](https://github.com/waynen12/kosync-dotnet/issues/2)-[#13](https://github.com/waynen12/kosync-dotnet/issues/13),
spec [#1](https://github.com/waynen12/kosync-dotnet/issues/1)). Manual
testing against real KOReader devices is next. Phases 3 (metadata) and 4
(AI) are not started — see Open Questions below. This doc exists to get
the plan out of chat and into the repo so it survives between sessions.
Domain vocabulary lives in [CONTEXT.md](../CONTEXT.md) — read that first,
this doc assumes its terms (Book, Document, Device, SyncEvent, Split Book,
Progress Reset).

Decisions with real reasoning behind them are recorded as ADRs in
[docs/adr/](adr/) rather than repeated here.

## Guiding constraint

The KOReader sync protocol surface (`/users/auth`, `/users/create`,
`PUT /syncs/progress`, `GET /syncs/progress/{hash}`) stays minimal and
protocol-faithful — same request/response shape a stock KOReader client
already expects. It is not the place for dashboard/metadata/AI logic.
Everything new lives in its own layer and reads the same underlying data.
The one exception is regression protection (Phase 0/2, below), which
changes what counts as canonical progress but never changes the wire
response.

## Decisions locked in

| Area | Decision | Confidence |
|---|---|---|
| Dashboard frontend | Blazor, `InteractiveServer` render mode throughout the authenticated app; `/login` is Static SSR (ADR 0003) | Firm |
| Dashboard data access | Components call domain/EF Core services directly, in-process — no HTTP hop through `/manage/*` (ADR 0003) | Firm |
| Component-level testing | No `bUnit` for v1 — service-layer integration tests cover the logic that matters | Firm, revisit if UI logic gets complex |
| Auth cookie `SecurePolicy` | `SameAsRequest` for V1 (plain HTTP, local machine only) — **must** become `Always` before the droplet deployment goes internet-facing | Firm for V1, flagged for the pre-deployment pass |
| Storage | Migrate LiteDB → SQLite + EF Core | Firm |
| Project layout | Single project, organized by folder | Firm for now, revisit if real seams appear |
| Dashboard auth | Hand-rolled cookie session reusing existing admin account (ADR 0002) | Firm |
| Test framework | xUnit + `WebApplicationFactory` integration tests | Firm |
| Device identity | `device_id` only; a reinstall is a brand-new Device, no auto-merge | Firm |
| Book/Document split | Modeled from the first migration, not deferred to metadata phase (ADR 0001) | Firm |
| Book/Document assignment | New Document auto-creates its own 1:1 Book; "same book" merges are a manual dashboard action | Firm |
| Automatic Book-matching (metadata-based) | Stays in Phase 3; manual merging covers Phase 1/2 | Firm |
| Regression protection | Document-scoped only (not across a Book's merged Documents) | Firm |
| Regression mechanism | Every push recorded as a SyncEvent; only promotes to current if percentage ≥ current. No auto-detected "restart" carve-out. | Firm |
| Restarting a book | Manual "delete progress" dashboard action, reusing the existing document-delete capability; clears the current-progress pointer but keeps SyncEvent history intact underneath | Firm |
| Regression visibility to KOReader client | None — response stays exactly `{document, timestamp}` regardless of whether the push became current. Discrepancy is dashboard-only. | Firm |
| Staleness (device gone quiet) | Not tracked as an "issue" — a last-synced timestamp is fine to display, no alerting built for it | Firm |
| Split Book discovery | Fully manual for v1 (no proactive "these might be the same book" heuristic) — false-positive rate too high before Phase 3 metadata gives a real signal | Firm |
| Issue surfacing UX | Inline flags on the devices/books they belong to, not a separate "Issues" inbox | Firm |
| Book-level progress display | Furthest-along current progress across a Book's merged Documents wins, regardless of which synced most recently | Firm |

## Phased plan

### Phase 0 — Foundations — ✅ Done ([#2](https://github.com/waynen12/kosync-dotnet/issues/2), [#3](https://github.com/waynen12/kosync-dotnet/issues/3))
- xUnit test project, `WebApplicationFactory` integration tests over the
  *existing* Sync/Management endpoints — a regression baseline before
  anything underneath them changes.
- Introduce EF Core + SQLite. Schema shape, as built:
  - `Users` — migrated as-is.
  - `Books` — new. A Document always belongs to exactly one Book.
  - `Documents` — hash, belongs to a Book. Also carries `LastResetAt`
    for Progress Reset (see Phase 2).
  - `Devices` (new) — identity is `device_id`; `device` label is mutable.
  - `SyncEvents` (new) — append-only: Device, Document, percentage,
    progress, timestamp, `IsCurrent`. This is the full history sync-issue
    tracking and diagnosis are built on.
- One-time LiteDB → SQLite migration runs automatically on startup if a
  LiteDB data file is found (`LiteDbMigrator`).
- Hand-rolled cookie auth for the dashboard login (ADR 0002); `/login` is
  Static SSR, everything past it is `InteractiveServer` (ADR 0003).
  `Cookie.SecurePolicy = SameAsRequest` for now — see the pre-deployment
  checklist below.
- Regression protection implemented in `PUT /syncs/progress`: every push
  inserts a SyncEvent, only promoted to the Document's current-progress
  pointer if percentage ≥ current.

### Phase 1 — Dashboard MVP — ✅ Done ([#4](https://github.com/waynen12/kosync-dotnet/issues/4), [#5](https://github.com/waynen12/kosync-dotnet/issues/5), [#6](https://github.com/waynen12/kosync-dotnet/issues/6))
- Devices view: last-synced timestamp, associated Books/Documents,
  editable label.
- Books/documents list with per-Document sync status.
- Book-level progress = furthest-along Document, once merges exist.
- Shared card-grid component (`Components/Shared/CardGrid.razor`) backs
  both tabs, extracted after the first review round found the two tabs'
  markup duplicated ([#11](https://github.com/waynen12/kosync-dotnet/issues/11)).
- **UI direction — resolved:** four layouts were prototyped as a throwaway
  HTML mockup (`docs/prototype/dashboard-mockup.html`, untracked/uncommitted,
  switchable via `?variant=A|B|C|D`). A sidebar table, a device-first card
  grid, and a split-pane diagnostic view were all rejected in favor of a
  hybrid (variant D): a card grid landing screen (Devices/Books tabs)
  where clicking a card drills into a full detail view (documents,
  per-device progress, and a regression/split-book history timeline).
  Two behaviors from the prototype should carry into the real Blazor
  implementation:
  - Drilling into a card is real navigation, not a client-side toggle —
    the URL reflects which Book/Device is open and the browser back
    button un-drills. In Blazor this is just routed pages/route params,
    not the pushState hack the static prototype needed.
  - Dark/light theme is a sticky per-user preference (survives
    navigation), not something encoded in the route.

### Phase 2 — Sync issue detection & diagnostics — ✅ Done ([#7](https://github.com/waynen12/kosync-dotnet/issues/7), [#8](https://github.com/waynen12/kosync-dotnet/issues/8), [#9](https://github.com/waynen12/kosync-dotnet/issues/9))
- Split Book flag: shown inline on affected Books/Devices, not a separate
  inbox. Discovery is manual — you notice mismatched hashes and merge them
  yourself; no auto-suggestion heuristic in v1.
- Progress Regression flag: a SyncEvent that didn't become current is
  visible inline on that Document's history.
- Manual merge action for two Books believed to be the same title
  (`BookDashboardService.MergeBooksAsync`, transactional after
  [#10](https://github.com/waynen12/kosync-dotnet/issues/10) found the
  first version could partially apply on failure).
- Progress Reset: a "delete progress" dashboard action distinct from a
  SyncEvent (see CONTEXT.md) — clears a Document's current-progress
  pointer, keeps its SyncEvent history intact, recorded as `LastResetAt`.

**Hardening after the initial build:** issues
[#10](https://github.com/waynen12/kosync-dotnet/issues/10)-[#13](https://github.com/waynen12/kosync-dotnet/issues/13)
were opened from code review and are now all closed — a merge
transaction gap, duplicated tab markup, hardcoded strings bypassing
`Constants.cs`, and a couple of minor text/naming nits.

### Phase 3 — Book metadata
- Metadata extraction — source not yet decided (on-device EPUB/OPF
  metadata? external API? user-supplied?).
- `BookMetadata` table; surface title/author/cover in the dashboard.
- This is also when automatic Book-matching (suggesting/performing merges
  from real title/author signal) becomes viable.

### Phase 4 — AI integration
- Summaries of in-progress books.
- Spoiler-safe "what to watch out for" recaps bounded by how far the
  reader has actually progressed.
- Needs a text/summary source, an LLM integration point, and careful
  scoping of the "no further than current progress" boundary.

## Deployment target

V1 runs on this machine only, plain HTTP, no reverse proxy. Long-term plan
is a DigitalOcean droplet for access away from home. The README's Nginx
Proxy Manager mention is inherited from the original repo this project was
cloned from, not a setup decision made for this fork.

### Before going internet-facing (droplet deployment)

Not needed for V1 — revisit as a dedicated pass before the droplet
deployment, not incrementally:

- Auth cookie `SecurePolicy` → `Always` (HTTPS-only).
- Reverse proxy in front of the droplet must forward WebSocket upgrade
  headers and not aggressively time out idle connections — Blazor
  `InteractiveServer` (ADR 0003) needs a persistent connection.
- Whatever the actual reverse proxy ends up being should get its own
  README section, replacing the inherited Nginx Proxy Manager mention if
  it's no longer accurate.

## Open questions

- Book metadata source (Phase 3).
- AI provider/integration shape and cost model (Phase 4).
