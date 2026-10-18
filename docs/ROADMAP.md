# Roadmap: Dashboard & Beyond

Status: **Phases 0-2 implemented, code-reviewed, and merged to `main`**
(issues [#2](https://github.com/waynen12/kosync-dotnet/issues/2)-[#13](https://github.com/waynen12/kosync-dotnet/issues/13),
spec [#1](https://github.com/waynen12/kosync-dotnet/issues/1)).

A design pass after that merge reframed the project substantially: the
dashboard is **not** a single-operator monitoring tool but a multi-tenant
portal every reader logs into with their own sync credentials (ADR 0004).
That, plus a forced first-login password change (ADR 0006), real password
storage (ADR 0005), and the discovery that KOReader can now push book
metadata (ADR 0007), are designed and specified but **not yet
implemented**. Phase 2.5 and Phase 3 below carry that work.

Two prototyping sessions then settled the UI: layouts and design language
(ai-hub branding, dense tables over card grids), and the framework
question — Blazor stays. The prototype itself lives on the
`design/portal-redesign` branch. **Folding that UI in is the next piece of
work**, ahead of Phase 2.5, since all of it except the Users page is
backend-independent.

This doc exists to get the plan out of chat and into the repo so it
survives between sessions. Domain vocabulary lives in
[GLOSSARY.md](../GLOSSARY.md) — read that first, this doc assumes its
terms (User, Administrator, Provisional Password, Book, Document, Device,
SyncEvent, Split Book, Observed/Curated Metadata, Progress Reset).

Decisions with real reasoning behind them are recorded as ADRs in
[docs/adr/](adr/) rather than repeated here.

**Implementation tickets** for everything below that isn't done live under
[#14](https://github.com/waynen12/kosync-dotnet/issues/14) — the spec
issue, with each phase's tickets as sub-issues and blocking recorded as
native GitHub issue dependencies. The ready-to-start frontier is whichever
of them has no open blocker.

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
| Dashboard frontend | Blazor, `InteractiveServer` render mode throughout the authenticated app; `/login` is Static SSR. Reopened after the second prototype and reaffirmed; local UI state must not round-trip (ADR 0003) | Firm, review at droplet cutover |
| Design language | ai-hub parity as *branding* only — shadcn oklch ramp, Geist, orange→indigo gradient, `#F57C00` accent, `--radius: 0.625rem`. Token block copied, **not** components. Two separate apps, no interaction, no shared accounts | Firm |
| Droplet routing | Own subdomain (`kosync.notrauto.org`), not a path prefix | Firm |
| `/login` layout | ai-hub's login verbatim: full-bleed gradient, centred `max-w-sm` card | Firm (2026-10-12 prototype) |
| Dashboard + Users layout | Sidebar shell + dense table, not a card grid — cards go ragged at realistic density | Firm (2026-10-12 prototype) |
| Dashboard data access | Components call domain/EF Core services directly, in-process — no HTTP hop through `/manage/*` (ADR 0003) | Firm |
| Component-level testing | No `bUnit` for v1 — service-layer integration tests cover the logic that matters | Firm, revisit if UI logic gets complex |
| Auth cookie `SecurePolicy` | `SameAsRequest` for V1 (plain HTTP, local machine only) — **must** become `Always` before the droplet deployment goes internet-facing | Firm for V1, flagged for the pre-deployment pass |
| Storage | Migrate LiteDB → SQLite + EF Core | Firm |
| Project layout | Single project, organized by folder | Firm for now, revisit if real seams appear |
| Dashboard auth | Hand-rolled cookie session (ADR 0002) — but **every User** logs in with their sync credentials, not just an admin (ADR 0004) | Firm |
| Portal tenancy | Multi-tenant. EF Core global query filters; tenant set explicitly per operation, unset = deny-all. `Book` gains a `UserId` (ADR 0004) | Firm |
| Administrator scope | Manages accounts only — never reads another User's library, in the portal or over `/manage/*` (ADR 0004) | Firm |
| Password storage | `bcrypt(md5(password))` — protocol-faithful outside, real hashing inside. Work factor 12, configurable, with a short-lived verified-credential cache on the sync path (ADR 0005) | Firm |
| First login | Every User starts with a Provisional Password that blocks **both** portal and sync until replaced. `ADMIN_PASSWORD` is seed-only (ADR 0006) | Firm |
| Self-registration | `POST /users/create` disabled by default; Administrators provision Users (ADR 0006) | Firm |
| Book metadata source | KOReader's opt-in `metadata` push, observed per Document; manual curation per Book; ISBN/title lookup later (ADR 0007) | Firm |
| Test framework | xUnit + `WebApplicationFactory` integration tests | Firm |
| Device identity | `device_id` only; a reinstall is a brand-new Device, no auto-merge | Firm |
| Book/Document split | Modeled from the first migration, not deferred to metadata phase (ADR 0001) | Firm |
| Book/Document assignment | New Document auto-creates its own 1:1 Book; "same book" merges are a manual dashboard action | Firm |
| Automatic Book-matching (metadata-based) | Now viable on real metadata, but **suggestions only** — exact case-insensitive match on title *and* author, never an automatic merge (ADR 0007) | Firm |
| Regression protection | Document-scoped only (not across a Book's merged Documents) | Firm |
| Regression mechanism | Every push recorded as a SyncEvent; only promotes to current if percentage ≥ current. No auto-detected "restart" carve-out. | Firm |
| Restarting a book | Manual "delete progress" dashboard action, reusing the existing document-delete capability; clears the current-progress pointer but keeps SyncEvent history intact underneath | Firm |
| Regression visibility to KOReader client | None — response stays exactly `{document, timestamp}` regardless of whether the push became current. Discrepancy is dashboard-only. | Firm |
| Staleness (device gone quiet) | Not tracked as an "issue" — a last-synced timestamp is fine to display, no alerting built for it | Firm |
| Split Book discovery | Manual in Phases 1/2. Phase 3 adds exact title+author *suggestions* now that metadata provides the real signal this was waiting on (ADR 0007) | Firm |
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
- **UI direction — ⚠️ superseded by the second prototyping session. See
  "UI direction — resolved" below; the card grid described here is gone.**
  Recorded for the history, because the two sessions reached opposite
  conclusions and the reason matters: this first session prototyped as a
  static HTML mockup with a handful of sample rows and rejected a sidebar
  table in favour of a card grid. The second session rendered the same
  layouts against *real* data — 13 Devices, 36 Books — and rejected the
  card grid in favour of a sidebar table, because a grid row is as tall as
  its tallest card and the voids compound at density. Judging at realistic
  density is what flipped the answer. Don't flip it back without data.

  What the first session got right and still holds: drilling in is real
  navigation, and the theme is a sticky per-user preference. What it got
  wrong was the landing-screen shape, having never seen 36 books.

  Original text follows. Four layouts were prototyped as a throwaway
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
  SyncEvent (see GLOSSARY.md) — clears a Document's current-progress
  pointer, keeps its SyncEvent history intact, recorded as `LastResetAt`.

**Hardening after the initial build:** issues
[#10](https://github.com/waynen12/kosync-dotnet/issues/10)-[#13](https://github.com/waynen12/kosync-dotnet/issues/13)
were opened from code review — a merge transaction gap, duplicated tab
markup, hardcoded strings bypassing `Constants.cs`, and a couple of minor
text/naming nits. #10, #11 and #12 are closed, and
[#13](https://github.com/waynen12/kosync-dotnet/issues/13) is implemented
on `design/portal-redesign`.

#13 landed after the prototype did, so its two strings resolved slightly
differently than the issue describes: `NoDeviceDocumentsSyncedText` now
reads "No documents synced yet", and the login error — which by then had
moved from `Login.razor` into `PrototypeConstants.cs` — is
`Constants.LoginErrorText`, rendered by all three login variants. That
discharges the fold-in ordering constraint below.

Note [#1](https://github.com/waynen12/kosync-dotnet/issues/1), the
Phases 0-2 spec, is also still open despite that work having shipped.

### Phase 2.5 — Accounts, passwords and tenancy — designed, not started

Numbered 2.5 rather than 3 only to keep the existing Phase 3/4 references
stable. In scope and effort this is the largest change since Phase 0: it
touches every dashboard query.

Do the password work *before* the user-creation UI — the create-user form
can't be designed until the credential story is settled.

**Passwords and first login (ADR 0005, ADR 0006):**
- Migrate stored credentials to `bcrypt(md5(password))`. No password
  resets needed and plaintext is never required: the stored value is
  already `md5(password)`, so the migration is `bcrypt(stored)`. Same for
  users arriving via `LiteDbMigrator`.
- Configurable work factor (default 12) plus a short-lived in-memory cache
  of verified credentials so repeat pushes skip the KDF.
- `ADMIN_PASSWORD` becomes seed-only. Stop rewriting the admin's hash on
  every startup.
- Provisional Password blocks portal *and* sync. Refused sync requests
  return 401 with a message naming the portal URL (configurable — the
  server can't reliably know its own public address).
- Middleware redirects a Provisional-Password User to the change-password
  page from every authenticated route but that page and logout.
- One-shot `ADMIN_PASSWORD_RESET` recovery, returning admin to Provisional.
- `POST /users/create` disabled by default; invert the env var.
- The change-password page is built in the folded-in design language (hub
  primitives), matching `/login`. No styling decisions remain here — the
  UI fold-in lands before this phase and `/login` arrives already done,
  which is the reason for that ordering.

**Tenancy (ADR 0004):**
- `Book` gains a `UserId`.
- EF Core global query filters, tenant set explicitly per operation,
  unset = deny-all. Startup/migration code opts out explicitly.
- `/login` stops requiring `IsAdministrator`.
- `GET /manage/users/documents` loses cross-user reading.
- Note the Blazor trap: inside a live `InteractiveServer` circuit there is
  no usable `HttpContext`, so the existing `IHttpContextAccessor`-based
  `UserService` must not become the tenancy source.

**User administration (item 1):**
- Admin-only Users page: list, create, deactivate, reset password, delete.
- Promote/demote Administrators, guarded so nobody can demote or delete
  themselves and the last Administrator can't be removed.
- Logic belongs in a service the Blazor page calls directly (ADR 0003),
  with `ManagementController` delegating to the same service rather than
  the page calling `/manage/*` over HTTP.

**Testing note:** roughly 25 existing tests log in as `admin`/`admin` and
depend on `ADMIN_PASSWORD` being applied at boot. Forced password change
breaks all of them; the shared test fixture needs to seed a non-Provisional
admin.

### Phase 3 — Book metadata — designed, not started

Source is now decided (ADR 0007): KOReader's own opt-in metadata push.

- Extend `DocumentRequest` with an optional `metadata` object
  (`filename`, `title`, `authors` — `authors` is a single string, not a
  list). It is currently dropped silently at model binding. Requires
  KOReader ≥ 2026.07 with "Send document metadata" enabled; absent
  metadata must never clear previously stored metadata.
- Observed metadata on Document; curated title/author on Book; display
  resolves curated → most recent observation → `Book #N`.
- Manual title/author editing on the Book detail page.
- Observed filename replaces raw hashes as the Document label **on the
  detail pages**, hash still shown alongside. The dashboard table shows a
  Document count and labels no Documents, so nothing changes there.
- Inline hint on metadata-less Books telling the reader to enable the
  KOReader setting.
- Exact title+author merge *suggestions*, scoped within one User.
- Later, not now: fill metadata from an entered title or ISBN.

### Phase 4 — AI integration
- Summaries of in-progress books.
- Spoiler-safe "what to watch out for" recaps bounded by how far the
  reader has actually progressed.
- Needs a text/summary source, an LLM integration point, and careful
  scoping of the "no further than current progress" boundary.

## UI direction — resolved

A second prototyping session (2026-10-12) settled the layout question and
the design language; a follow-up round (2026-10-13) settled the framework
question. See [ui-tech-review.md](ui-tech-review.md) for the evidence and
the reasoning on both.

### Design language — decided

The portal adopts the **ai-hub design language**
(`/home/wayne/repos/AIResources/ai-hub`), so the two projects read as the
same product: shadcn's achromatic oklch surface ramp, Geist / Geist Mono,
the `135deg #F57C00 → #FF8F00 → #7B6FBD → #4A5BAA` gradient, `#F57C00` as
the single accent, amber-underline tabs, `--radius: 0.625rem`.

Blazor cannot consume ai-hub's actual components (Tailwind v4 + shadcn +
lucide-react + sonner are React), so parity means a hand-written
reimplementation. That cost is real and recurring — it is finding 3 of the
tech review and the strongest argument on the framework question.

### Screen layouts — decided

| Screen | Chosen | Shape |
|---|---|---|
| `/login` | **Hub parity** | ai-hub's login verbatim — full-bleed gradient, centred `rounded-2xl shadow-xl max-w-sm` card, Label+Input pairs, full-width orange submit |
| Dashboard | **Admin console** | gradient nav strip (no hero), persistent sidebar, dense table of Devices/Books |
| User management | **Admin console** | full page, dense table, slide-over detail panel — *not* ai-hub's modal |

**Why the card grid lost, for both lists.** It was prototyped faithfully
and judged at realistic density (13 Devices, 36 Books). Cards get messy
once there is a lot of data, because **a grid row is as tall as its tallest
card** — one 5-document Book forces its whole row tall and leaves voids
beside it, repeatedly, all the way down. Measured page heights at 1440px:

| | Devices (13) | Books (36) |
|---|---|---|
| card grid | 1860px | 3040px |
| table | **1050px** | **2269px** |

Height was the lesser point. The table aligns every progress bar
vertically, so a stalled Book is found by scanning one column, and the
Split Book flags form a single scannable column. Devices and Books are
*scanned* — you open this to find the one thing that went wrong — whereas
ai-hub's card grid works because a Resource card carries a title,
description and tags and is *browsed*. Same layout, different content
shape, different result.

Two sub-findings worth keeping: at 3-across a card is ~290px, which is too
narrow for median-length book titles plus an author; and for the ~20
single-document Books the card is mostly one md5 hash, the least useful
thing on it.

**Also fixed in the session, independent of any of the above:**
`wwwroot/css/app.css` has zero `@media` queries, and separately its
containers use `margin: 0 auto` on flex items — auto margins on a flex
item's cross axis make it size to content rather than stretch, so the
dashboard renders in a narrow centred column at every width. Both need
fixing whatever the framework ends up being.

### When folding in — goes first, ahead of Phase 2.5

The prototype is committed on the `design/portal-redesign` branch, so the
variants remain recoverable from history rather than from the working
tree.

**This work now leads, ahead of Phase 2.5.** The original order was
2 → 1 → 3 → 4, chosen when item 4 was an unknown quantity; it isn't any
more. Login, the dashboard, the nav and the token block have no backend
dependency and can land immediately — only the Users page is blocked on
Phase 2.5. Folding in first also removes the prototype scaffolding from
`Login.razor` and `Dashboard.razor` (which it is currently wired into, so
the working tree is not a neutral parking spot), and means Phase 2.5's
change-password screen gets built once in the final design rather than
styled twice. The reason for doing passwords before user administration
still holds *within* Phase 2.5 and is unaffected.

Everything prototype-related is under `Components/Prototype/` plus
`wwwroot/css/prototype.css` and `wwwroot/js/prototype.js`. What survives,
and where it goes:

| Prototype | Folds into | Notes |
|---|---|---|
| `LoginVariantA.razor` | `Components/Pages/Login.razor` | Strings move to `Constants.cs`, not `PrototypeConstants.cs` |
| `DashboardVariantB.razor` + `HubSidebar.razor` | `Components/Pages/Dashboard.razor` | Replaces `Components/Shared/CardGrid.razor`, which then has no caller |
| `UsersVariantB.razor` | a new Users page | **Blocked on Phase 2.5** — no account service, no tenancy |
| `HubNav.razor` | `Components/Layout/MainLayout.razor` | Replaces `.ks-shell-header` |
| `prototype.css` token block + primitives | `wwwroot/css/app.css` | **Does not delete the `--ks-*` tokens** — see "the detail pages" below |
| `HubIcon.razor` | keep as-is | Or take a real icon dependency — decide then |
| *(nothing)* | `Components/Pages/BookDetail.razor`, `DeviceDetail.razor` | **No prototype exists for these** — see below |

#### The detail pages were never prototyped

Neither session produced a variant for `/dashboard/books/{id}` or
`/dashboard/devices/{id}`, and they depend on 21 `.ks-*` classes
(`ks-detail`, `ks-doc-card`, `ks-history`, `ks-merge-form`,
`ks-reset-button`, `ks-flag-*`, …) whose rules are built on `var(--ks-*)`
tokens. `prototype.css` defines an entirely separate `.hub-*` family and
none of them. So deleting the `--ks-*` token block would strip the
variables out from under both pages and leave them unstyled — and these
are the screens carrying Split Book diagnosis, the merge form, the reset
action and the SyncEvent history timeline.

**Decision: keep the `--ks-*` tokens, redefined as aliases onto the hub
tokens.** Every `.ks-*` rule keeps working, the detail pages stay
functional and inherit the new palette, and the aliases delete themselves
when those pages are eventually prototyped. Redesigning them during the
fold-in was rejected: the fold-in's whole value is being the zero-risk
half of this work — no migrations, no auth, no tenancy — and designing
the diagnostic screens in the implementation is exactly what the
prototyping sessions existed to avoid.

The detail pages are therefore **the next UI pass** (a third prototyping
session), not part of the fold-in. They will look slightly off-brand
until then: new colours, old radii and spacing.

#### The dashboard stops listing documents

`CardGrid` rendered a hash row per Document inside every card; the
variant-B table shows a Document *count* instead, with the documents
themselves on the drill-in. This is deliberate — the second session found
that for the ~20 single-document Books the card was mostly one md5 hash,
"the least useful thing on it". Consequence for ADR 0007: observed
filename becomes a Document's label on the **detail pages**, not on the
dashboard, which no longer labels Documents at all.

Delete the rest: `PrototypeConstants.cs`, `PrototypeRoutes.cs`,
`PrototypeDemoData.cs`, `PrototypeSwitcher.razor`, `PrototypeUsers.razor`,
`PrototypeEmptyLayout.razor`, `PrototypeDashboardItem.cs`, the losing
variants, and `prototype.js`.

**Four** files in main were touched for the prototype and must be reverted
or made permanent:

- `Components/Pages/Login.razor` — `LoginModel` was made `public` and the
  page took `@layout PrototypeEmptyLayout`.
- `Components/Pages/Dashboard.razor` — also took
  `@layout PrototypeEmptyLayout`, a three-way variant switch and a
  `<PrototypeSwitcher>`, and lost its two `<CardGrid>` calls. Easy to miss:
  this one was absent from the list until a later review caught it.
- `Components/_Imports.razor` — two `@using` lines marked as throwaway.
- `tests/Kosync.Tests/DashboardAuthTests.cs` — one assertion left
  re-pointed at prototype copy (`">admin<"`); it should assert against
  whatever the folded-in nav actually renders. The login-error assertion is
  already on `Constants.LoginErrorText` — see the ordering constraint.

**Ordering constraint on the deletions — discharged by #13.**
`DashboardAuthTests.cs` used to reference `PrototypeConstants.LoginErrorText`
at *compile* time, so the test project would have stopped building the
moment `PrototypeConstants.cs` was removed. #13 moved that string to
`Constants.LoginErrorText` and re-pointed both the assertion and all three
login variants, so nothing now blocks deleting `Components/Prototype/`.

### Framework — decided (item 4)

**Blazor `InteractiveServer` stays.** ADR 0003 is amended with a rule it
was missing — *local UI state must not round-trip; data may* — rather than
superseded. The standalone Tailwind CLI middle path is rejected.

The decision turned on what "one design system" would have required in
practice. The two apps stay separate with no interaction between them —
shared look and feel only — and shadcn distributes by copying source into
a project, so moving to Next.js would have produced two independent shadcn
copies drifting against each other rather than one shared library. Parity
is wanted as branding, and branding needs the token block, which ports to
CSS custom properties verbatim. Reasoning in full:
[ui-tech-review.md](ui-tech-review.md#resolved-2026-10-13).

Still carry forward:

- Each pass over the tool is expected to refine the UI further, so avoid
  building layout primitives that the next pass invalidates.
- ADR 0004 commits the data layer to in-process EF Core with query
  filters. Keep Phase 2.5 and Phase 3 logic in services that would be
  cheap to re-expose over HTTP, and don't accrete component-level logic
  that wouldn't survive a move. This is now a hedge rather than a plan,
  but the droplet cutover is the review point and the cost of a future
  move only rises.

## Deployment target

V1 runs on this machine only, plain HTTP, no reverse proxy. The README's
Nginx Proxy Manager mention is inherited from the original repo this
project was cloned from, not a setup decision made for this fork.

**Target: the existing DigitalOcean droplet**, alongside ai-hub
(`hub.notrauto.org`) and n8n (`n8n.notrauto.org`), as
**`kosync.notrauto.org`** — its own subdomain, deliberately not a path
prefix. Path-prefixing a Blazor Server app breaks `<base href="/">`, every
constant in `AppRoutes.cs`, the static asset paths and the SignalR
endpoint; all fixable, all fiddly, and it fails only in production. A
subdomain costs one DNS record.

kosync runs **containerised** (the repo already carries a `Dockerfile`),
unlike ai-hub which is deployed directly — chosen for isolation on a
shared box.

### Before going internet-facing (droplet deployment)

Not needed for V1 — revisit as a dedicated pass before the droplet
deployment, not incrementally. Note that ADR 0004's multi-user portal
raises the stakes here: these are now other people's credentials and
other people's reading history, not just the operator's own.

- HTTPS is non-negotiable before this is reachable from the internet. The
  KOReader protocol sends `md5(password)` as a password-equivalent in a
  header, which ADR 0005 cannot fix — only transport encryption can.
- Auth cookie `SecurePolicy` → `Always` (HTTPS-only).
- Reverse proxy in front of the droplet must forward WebSocket upgrade
  headers and not aggressively time out idle connections — Blazor
  `InteractiveServer` (ADR 0003) needs a persistent connection.
- Whatever the actual reverse proxy ends up being should get its own
  README section, replacing the inherited Nginx Proxy Manager mention if
  it's no longer accurate.

## Open questions

- **Detail-page design.** `BookDetail`/`DeviceDetail` were never
  prototyped and keep the old `.ks-*` styling via token aliases after the
  fold-in. A third prototyping session is the next UI pass — and it is the
  one that should settle how the merge form, reset action and SyncEvent
  history timeline look in the hub language.
- Filling book metadata from an entered title or ISBN — wanted, shape not
  designed (ADR 0007 covers only the push and manual curation).
- Password expiry — anticipated but not designed. ADR 0006 notes that
  "must change password now" shouldn't be modelled as a one-shot flag
  that expiry can't later re-raise.
- Fuzzy Book matching, to be decided on real data gathered from the exact
  title+author rule.
- AI provider/integration shape and cost model (Phase 4).

*Resolved by the post-Phase-2 design pass:* book metadata source (ADR
0007), dashboard tenancy model (ADR 0004), password storage (ADR 0005),
first-login flow (ADR 0006).

*Resolved by the two prototyping sessions:* screen layouts and design
language (above), and the UI framework question — Blazor stays, ADR 0003
amended rather than superseded, Tailwind CLI rejected
([ui-tech-review.md](ui-tech-review.md#resolved-2026-10-13)).
