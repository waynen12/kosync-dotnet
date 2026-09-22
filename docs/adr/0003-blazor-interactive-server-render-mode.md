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
