# The portal is multi-tenant; every User logs in with their sync credentials

The dashboard is not an operator's monitoring tool. It is a portal each
reader logs into with the same username and password their KOReader
devices sync with, to see their own Books, Devices, and sync issues —
that is the primary purpose of the project. An Administrator is a User
who can additionally manage other Users; it is a capability, not a
separate kind of account.

Tenancy is absolute: an Administrator manages accounts but never reads
another User's library. There is no "view as user" mode and no admin
cross-user view, because every bypass path is a place a leak can hide,
and the people whose sync issues need diagnosing can see their own
Split Book flags and report them.

Scoping is enforced by **EF Core global query filters** on
`KosyncDbContext`, not by threading a `userId` parameter through every
service method. Queries are then scoped by default and a future query
cannot silently forget to scope itself. It also fixes authorization on
mutations for free: a filtered lookup of another User's Book returns
null, so the existing "not found" paths already handle it.

`Book` gains a `UserId`. It previously had no owner at all, reachable
only through its Documents, which left `MergeBooksAsync` free to merge
two Users' Books together and physically move one User's Documents into
the other's Book — data corruption, not merely a disclosure. A global
filter also needs an owning column to filter on.

The filters read the current User from state **set explicitly at the start
of each operation**, and an unset tenant **denies everything** rather than
allowing everything. Resolving the User ambiently was rejected: the four
callers know "who" by incompatible means — controllers from the
`x-auth-user` header, `/login` from a cookie claim, interactive Blazor
circuits from `AuthenticationStateProvider` (they have no usable
`HttpContext` after the first render), and startup code from nothing at
all — so any ambient chain has a branch that resolves to "no user", and in
an allow-by-default design that branch returns everybody's data. Setting
the tenant explicitly is a step that can be forgotten, but failing closed
means forgetting it yields an empty dashboard, which is noticed in
seconds, instead of a silent cross-tenant leak.

`GET /manage/users/documents` loses its ability to read another User's
documents. The rest of `/manage/*` keeps its Administrator powers —
creating, deleting, activating Users and resetting their passwords — since
those are account operations rather than reading someone's library. That
endpoint's cross-user reach was never a privacy decision anybody made; it
was written when the server had one user.

## Consequences

- Supersedes the "single-operator tool, no separate user system for the
  dashboard" premise in ADR 0002 and the roadmap. ADR 0002's hand-rolled
  cookie mechanism stands; only its single-operator assumption is void.
- `/login` must stop requiring `IsAdministrator`.
- Code paths that legitimately operate outside any User — migrations,
  startup seeding, the LiteDB import — need a deliberate, explicit way to
  bypass the filters, and that bypass is the thing to review carefully.
