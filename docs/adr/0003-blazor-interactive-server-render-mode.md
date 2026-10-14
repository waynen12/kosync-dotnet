# Dashboard runs entirely as Blazor Interactive Server; no WebAssembly

The dashboard is used by exactly one operator, self-hosted, on infrastructure
the operator controls. `InteractiveServer` render mode (a persistent
SignalR circuit, full C# execution on the server) is used throughout the
authenticated dashboard, with no `InteractiveWebAssembly` or
`InteractiveAuto` anywhere. `/login` alone is `Static SSR` — a plain form
POST — since there's no reason to hold open a live circuit before the user
is even authenticated.

This is a deliberate departure from the instinct a lot of developers have
that "modern Blazor" means WebAssembly. WASM buys offline capability and
moves rendering cost to the client — neither matters when there's one user
and one server. Choosing `InteractiveServer` also settles the data-access
architecture: components call domain/EF Core services directly, in-process,
with no HTTP/JSON round trip. The existing `/manage/*` REST API is not
reused as the dashboard's data layer and is not extended to serve it — it
stays a separate, stable surface for Postman/scripts.

## Local UI state must not round-trip

Added after the second prototyping session. `InteractiveServer` does not
*impose* a server round-trip on every interaction — `@onclick` opts into
one. The prototype's variant C was a complete dashboard on this same
render mode where no interaction reached the server at all: filter chips
were radio inputs styled with `:has()`, row expansion was native
`<details>`, and text filtering was a few lines of plain JS.

So the rule is: **UI state never round-trips; data may.** Tab selection,
filters, sorting, disclosure and search-as-you-type are client-side.
Fetching a detail panel's contents may go to the server, because that is
data rather than UI state.

The code this rule was written against is the original `Dashboard.razor`,
which sent a WebSocket message, re-rendered a component tree and shipped a
DOM diff back in order to change a local string from `"devices"` to
`"books"`.

## Reaffirmed, 2026-10-13

This decision was genuinely reopened — the dashboard "didn't feel
interactive enough", and the portal had meanwhile adopted the ai-hub
design language (Next.js 16 / React 19 / Tailwind v4 / shadcn), raising
the question of whether to move wholesale. See
[ui-tech-review.md](../ui-tech-review.md) for the full evidence. It was
reaffirmed because:

- The strongest argument for moving was to stop hand-reimplementing
  shadcn. But the two apps are staying separate, and shadcn distributes by
  copying source into a project — so moving would produce two independent
  shadcn copies to keep in step, not one shared library. The problem would
  have been relocated rather than solved.
- Parity with ai-hub is wanted as *branding*, not as shared components.
  That needs the token block — the oklch ramp, gradient, Geist,
  `--radius` — which ports to CSS custom properties verbatim. The
  expensive half of parity is the half branding does not need.
- The responsiveness complaint was never a framework matter: `app.css`
  had no `@media` queries, and auto margins on flex items were making
  containers size to content instead of stretching.
- The latency complaint is addressed by the no-round-trip rule above,
  which variant C demonstrated is achievable without leaving .NET.

Rejected alongside it: the standalone Tailwind CLI against `.razor` files.
It adds a build step to a .NET project to buy utility class names, when
the thing that actually defines the branding is a one-time token copy.

Review point: the droplet cutover, where latency stops being hypothetical.

## Consequences

- Requires a persistent WebSocket (or SSE/long-polling fallback) connection
  between browser and server. Any reverse proxy placed in front of this app
  later must be configured to forward WebSocket upgrade headers and not
  aggressively time out idle connections, or the dashboard's circuit will
  keep dropping.
- Moving any part of the dashboard to WebAssembly later would mean
  introducing a real API layer for it to call, since WASM components can't
  reach server-side services in-process. Not a cheap change.
- No offline support — the dashboard is unusable if the connection to the
  server drops, by design.
