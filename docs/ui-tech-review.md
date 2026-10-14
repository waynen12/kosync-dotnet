# UI tech review: does ADR 0003 survive?

Written during the UI prototyping session that
[ROADMAP.md](ROADMAP.md)'s "UI direction — open, prototype-gated" section
parked this question behind. Read that section first.

The question: the variant-D layout is right, but the dashboard "isn't
responsive" and "doesn't feel interactive enough". Does
[ADR 0003](adr/0003-blazor-interactive-server-render-mode.md) (Blazor
`InteractiveServer` throughout) survive, or does the dashboard move to a
client-side framework?

A new constraint arrived mid-session and changes the shape of the answer:
the login and user-management screens should match
`/home/wayne/repos/AIResources/ai-hub` — a **Next.js 16 / React 19 /
Tailwind v4 / shadcn / lucide-react / sonner** app that already exists and
is already maintained.

## What the prototype established

### 1. The responsiveness complaint is not a framework question

`wwwroot/css/app.css` had zero `@media` queries in ~390 lines. All three
prototype variants are responsive — sidebar collapsing to a bottom tab bar,
single-column grids, scrolling tables — and nothing about that involved a
framework. It is one stylesheet.

The prototype also turned up the actual bug behind the "not responsive"
feeling, and it is not a missing breakpoint: `.hub-main`/`.hub-toolbar`/
`.hub-stream` are flex items with `margin: 0 auto`, and **auto margins on a
flex item's cross axis make it size to its content instead of stretching**.
The dashboard was silently rendering in a narrow centred column at every
width. One `width: 100%` per container fixed it. Same class of bug exists
to be found in `app.css`.

**Verdict: irrelevant to the framework choice. Fix it either way.**

### 2. The interactivity complaint is mostly self-inflicted

Variant C is a Blazor `InteractiveServer` page on which **no interaction
sends a SignalR message**. Filter chips are radio inputs the stylesheet
reacts to with `:has()`; expanding a row is native `<details>`; the text
filter is ~20 lines in `wwwroot/js/prototype.js`. It is the same render
mode as variants A and B.

So the round-trip is not something `InteractiveServer` imposes — it is
something `@onclick` opts into. The current `Dashboard.razor` sends a
WebSocket message to the server, re-renders a component tree and ships a
DOM diff back **in order to change a local string from `"devices"` to
`"books"`**. That is a code choice, and it has a fix that does not require
leaving .NET.

**Verdict: "move to React so it feels instant" is not the only lever.
"Stop round-tripping trivial local state" is available today.**

### 3. Matching ai-hub's look is achievable, but it forks the design system

`wwwroot/css/prototype.css` reproduces ai-hub's design language closely
enough that the screens read as the same product: the shadcn achromatic
oklch ramp from its `globals.css`, Geist/Geist Mono, the
`135deg #F57C00 → #FF8F00 → #7B6FBD → #4A5BAA` gradient, `#F57C00` as the
single accent, amber-underline tabs, `--radius: 0.625rem`, the
`rounded-2xl shadow-xl max-w-sm` login card, the `max-w-xl` "Manage
Accounts" dialog with its Active/Inactive groups and inline reset form.

The cost is that none of it is **shared** — it is a hand-written
reimplementation. To get there this session I hand-wrote:

| ai-hub has | kosync prototype needed |
|---|---|
| `components/ui/button.tsx` (shadcn/base-ui, 6 variants × 8 sizes) | `.hub-btn` + 5 variant classes, by hand |
| `input`, `label`, `card`, `badge`, `select`, `dialog` | `.hub-input`, `.hub-label`, `.hub-card`, `.hub-badge`, `.hub-select`, `.hub-dialog`, by hand |
| `lucide-react` | `HubIcon.razor`, 20 inlined Lucide paths |
| `sonner` toasts | `.hub-toast`, by hand |
| `npx shadcn add <anything>` | write it again |

Tailwind itself is **not** the blocker — the standalone Tailwind CLI works
fine against `.razor` files with no Node at runtime, and that is the
obvious middle path if the token drift becomes annoying. shadcn components
are the blocker: they are React, and there is no port of them to Blazor
that tracks upstream.

**Verdict: parity is reachable and the prototype proves it. Parity is a
fork, and it has to be re-paid every time ai-hub gains a component.**

### 4. The droplet is where the latency argument gets real

Every measurement you can take locally is over loopback. The deployment
plan is a DigitalOcean droplet, and at that point each `@onclick` costs a
real network round-trip — tens to low hundreds of milliseconds from a phone
on mobile data, on every tab click, every modal open, every row select.
Plus the constraints ADR 0003 already lists: the reverse proxy has to
forward WebSocket upgrades and not time out idle circuits, and the
dashboard is unusable when the connection drops.

This is the one complaint that gets structurally worse rather than better,
and CSS cannot touch it. Note the mitigation is the same as finding 2:
interactions that never go to the server don't care about the RTT.

### 5. Switching is getting more expensive, but it hasn't got expensive yet

ADR 0003 and [ADR 0004](adr/0004-multi-tenant-portal.md) commit the data
layer to in-process EF Core with global query filters. A React client needs
a real HTTP API with tenancy enforced at that boundary instead — ADR 0004's
"unset tenant denies everything" rule would have to be re-established per
request rather than per operation.

That work gets harder after Phase 2.5 (tenancy, bcrypt, forced first-login
change) and Phase 3 (metadata) add surface area. **Phase 2.5 has not
started.** So if a move is ever going to happen, now is the cheapest
remaining moment — which is a genuinely awkward thing to be true, because
it means "decide later" is itself a costly choice.

## Measuring it yourself

The prototype ships a perceived-latency probe rather than asking you to
judge by feel. Every instrumented control carries `data-pt-probe`, and the
switcher pill shows **click→paint** — the time from the click to the first
DOM mutation inside the rendered tree.

- Variant A (dashboard tabs, modal open, reset-password toggle) → measured
  milliseconds; this is the round-trip.
- Variant B (row select, slide-over) → measured milliseconds, plus real
  `<a href>` navigation for section changes, which costs nothing.
- Variant C (chips, row expand) → reports `client-only`; there is no DOM
  mutation because nothing went to the server.

Read A against C on the machine, then imagine A over the droplet link. I
built the instrument but have not driven a browser with it, so the numbers
in this doc are deliberately absent rather than guessed.

## Recommendation

**Stay on Blazor `InteractiveServer`, adopt variant C's discipline, and set
the droplet cutover as the review point.** Concretely:

1. Keep ADR 0003. Amend it with a rule it is currently missing: *local UI
   state that the server does not need (tab selection, filters, disclosure,
   search-as-you-type) must not round-trip.* Variant C shows the whole
   dashboard can obey that.
2. Fix the responsive and flex-auto-margin bugs in `app.css` regardless.
3. Do Phase 2.5 with its logic in services that would be cheap to re-expose
   over HTTP — which ROADMAP.md already says, and which is now the hedge
   that keeps option 4 open.
4. Re-decide at the droplet pass, when the latency is real and measurable
   rather than hypothetical.

**The one thing that would change this recommendation** is not latency — it
is whether you want *one design system across ai-hub and kosync*. If the
answer is yes, the right move is Next.js + shadcn now, before Phase 2.5,
and the justification is "stop hand-reimplementing shadcn", not "SignalR is
slow". Finding 3 is the real argument for switching; findings 1 and 2 are
arguments against treating the UI complaints as framework complaints at
all.

That is a question about how you want to spend your time across projects,
which is yours and not the prototype's to answer.

## Decided since (2026-10-12)

Layouts are chosen — `/login` takes variant A (hub parity), the dashboard
and user management take variant B (admin console). The reasoning, the
measured page heights behind it, and the "cards go ragged at realistic
density" finding are recorded in
[ROADMAP.md](ROADMAP.md#ui-direction--layouts-resolved-framework-still-open).

Note what that does **not** settle. Variant B is the same design language
as A — same gradient, Geist, `#F57C00`, badges; it changes the dashboard's
*content area*, not the look. So choosing B is not a vote against ai-hub
parity, and finding 3 above stands undiminished: parity is still a fork of
the design system that has to be re-paid per component.

## Not decided here

- **The framework question itself.** The recommendation above stands
  unanswered: keep ADR 0003 with a no-round-trip rule, or move to Next.js
  before Phase 2.5 to stop reimplementing shadcn.
- Whether to adopt the standalone Tailwind CLI against `.razor` files as a
  middle path, if the answer to the design-system question is "keep them
  close but separate".
- ADR 0003 needs amending — with the no-round-trip rule if it survives, or
  superseding if it doesn't. Left alone deliberately; the decision is not
  made yet.
