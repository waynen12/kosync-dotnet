# Every User must replace their initial password before the account does anything

A User's first password is always one somebody else chose — `admin`/`admin`
on a fresh install, or whatever an Administrator handed over when creating
the account. Until the User replaces it through the portal, the account is
inert: both the portal and the sync API refuse it.

Blocking sync as well as the portal is the deliberate part. It means a
fresh install cannot sync a single page of progress until someone has
logged in and replaced the default credentials, and it means a password an
Administrator typed cannot quietly become a device's long-lived credential.
The cost is that a new User must visit the portal before their device will
sync at all, which is a real inconvenience accepted on purpose.

`ADMIN_PASSWORD` is consequently **seed-only** — it sets the admin password
when the account is first created and never again. It previously rewrote
the admin's password on every startup, which would have silently reverted
any password change on the next restart and made this whole decision
meaningless.

Because an Administrator provisioning accounts is now the way Users come
into existence, KOReader's self-registration endpoint (`POST /users/create`)
is **disabled by default** — the env var that used to opt out of it becomes
an env var that opts into it. Leaving it open would mean a second
account-creation path whose passwords were never Provisional, bypassing
this decision entirely, on a server intended to face the internet.

## Consequences

- Breaking change to documented README behaviour: `ADMIN_PASSWORD` no
  longer resets an existing admin password on restart. Recovery from a
  genuinely lost admin password is a one-shot `ADMIN_PASSWORD_RESET` env
  var that returns the admin to a *Provisional* Password on next start —
  deliberately not to a working password, so the recovery path cannot
  itself leave a lingering usable credential behind.
- Self-registration defaulting to off is also a breaking change to a
  documented default, and needs saying plainly in the README.
- An Administrator can promote and demote other Users and create new
  Administrators directly, guarded so that nobody can demote or delete
  themselves and the last remaining Administrator cannot be removed. The
  existing blanket "cannot update admin user" refusals in `/manage/*` were
  reaching for this protection but applied it far too broadly — they also
  prevented an Administrator from ever changing their own password.
- In the portal, a User holding a Provisional Password is redirected to the
  change-password page from every authenticated route except that page and
  logout, enforced in middleware rather than in a layout: a layout check
  runs per component render and a future page can sidestep it, while
  middleware sees every request including non-Blazor ones.
- Because the portal password and the sync password are the same stored
  credential, completing the forced change invalidates that User's
  password on every KOReader device they own. This happens to every User
  exactly once, by construction, so the change screen must say so
  prominently. Decoupling the two credentials, or per-device app
  passwords, were considered and rejected for now: one credential doing
  both jobs is the core appeal of the portal design.
- A refused sync is indistinguishable from a wrong password to stock
  KOReader unless the server's response says otherwise, so the refusal
  needs a message that names the portal.
- Password expiry is anticipated but not built. Whatever represents
  "must change password now" should not be a one-shot flag that expiry
  cannot later re-raise.

## Notes from building it

- The state is `User.PasswordState`, an enum of `Provisional` and
  `UserChosen` rather than a bool, so expiry can arrive as a third value.
  `Provisional` is zero, which makes it what a caller that forgot to say
  ends up with — the safe way round, since it is the state that grants
  nothing. Two routes opt out explicitly and say why: KOReader
  self-registration (the person typed the password into their own device)
  and the LiteDB import (the User has been syncing with it for however long
  the legacy server ran). `User.MustChangePassword` is derived from the
  enum rather than stored, and is written as "anything that is not the
  User's own choice" rather than as a list of blocking states — so a state
  added later blocks until somebody deliberately exempts it, rather than
  being let through by an enumeration nobody remembered to extend.
- "One-shot" needs something that outlives a restart, because the env var
  does too: `ADMIN_PASSWORD_RESET` stays in the compose file that triggered
  it, so honouring it whenever it is merely present would reinstate exactly
  the reset-on-every-boot behaviour this ADR removed from `ADMIN_PASSWORD`.
  It is therefore acted on once per *distinct value*, with the value last
  acted on held in a new `ServerSetting` table (GLOSSARY: Server Setting).
  Changing the value is how an operator recovers a second time.
- Merge-order hazard worth naming: the state landed before anything
  enforces it. Until the sync refusal and the portal gate are in, a
  Provisional Password is a fully working credential, so `ADMIN_PASSWORD`
  and the reset both still leave a usable password derived from an env var.
  Nothing is worse than it was — that is simply the old behaviour — but the
  recovery path is only as safe as this ADR claims once the enforcement
  ships. Both halves are now in — the sync refusal and the portal gate —
  so the recovery path is as safe as described above.
- The portal gate is `PasswordChangeGateMiddleware`, and it only ever looks
  at cookie-authenticated requests. The protocol and `/manage/*` endpoints
  authenticate from headers per request, so they arrive at the gate as
  anonymous requests and pass straight through to the refusals they
  already carry. That is deliberate rather than incidental: redirecting
  them would answer a KOReader client with an HTML page, and it would take
  `PUT /manage/users/password` — the route a User with no working portal
  replaces their password through — down with it.
- Three paths are let through besides the change-password page itself:
  logout, `/login`, and the framework's own endpoints (`/_blazor`,
  `/_framework`, `/_content`). The last group is not a route anybody
  navigates to; redirecting `blazor.web.js` would answer a script request
  with the HTML of the page the gate is trying to serve.
- The gate's one blind spot is the mirror image of the reason it is not in
  `MainLayout`: it sees requests, so it cannot see a circuit that is
  already open. An Administrator who resets a User's password to
  Provisional while that User is sat on the dashboard does not interrupt
  them — the next page load is the gate's first opportunity. A layout
  check would have caught that one case and missed every non-Blazor
  request, which is the trade this decision already made.
- Completing the change does not re-issue the session cookie, because the
  cookie carries only the username: it is not derived from the credential
  and so does not go stale when the credential does.
- Re-typing the same password is refused rather than accepted as a
  change — otherwise the state flips to `UserChosen` while the password
  somebody else chose stays in place, which is the one outcome this whole
  decision exists to prevent. That check runs ahead of the length rule, so
  a User re-typing `admin` is told to choose a different password rather
  than invited to pad this one to eight characters.
- The minimum length lives in `PasswordPolicy` rather than in the page,
  against two callers that are still to come: the Users administration
  page (issue #28) and `PUT /manage/users/password`, which today enforces
  only "not blank". It deliberately does not constrain the Provisional
  Passwords an Administrator hands over, which exist to be replaced.
- Logout is a confirm screen with a form post, not a link that signs you
  out on GET. A GET sign-out is triggered by anything that merely fetches
  the URL, and the antiforgery token a Blazor named form carries is the
  standard answer to that. It costs one click on an action taken rarely.
- The sync refusal sits on all three authenticated protocol endpoints
  rather than on a filter or middleware. The protocol surface is four
  endpoints that each already hand-roll their own auth and active checks
  from `UserService`, so a third check beside them reads the way the
  existing ones do; a filter would have been the better shape only if
  there were a `[Authorize]`-style attribute for these endpoints to share,
  and there isn't. What makes this safe is that forgetting the call on a
  future endpoint is a leak rather than an empty result — the opposite of
  the tenant scoping right beside it — so each endpoint has its own test
  asserting the refusal, not a shared one asserting the helper.
- `/manage/*` is deliberately *not* refused, so "the account is inert" is
  not yet literally true: a Provisional Password can still read its own
  library over the management API. Blocking that wholesale would take
  `PUT /manage/users/password` with it, which is the route a User without a
  working portal replaces their password through — so the refusal would
  have to carve out its own escape hatch. Still left alone now that the
  portal page (#24) exists, and for a reason that has not gone away: the
  portal still requires `IsAdministrator` to log in at all until issue
  #26, so for every other User the management API *is* the route, not a
  redundant second one. Whoever closes #26 inherits this decision.
- The portal URL in the message is configuration (`Portal__BaseUrl`),
  deliberately not derived from the request. The `Host` header on a sync
  request is whatever the device was pointed at, which is often an
  internal address, a bare IP, or the wrong scheme behind a proxy — and the
  one reader who needs this message is the one whose setup is least likely
  to make that guess right. The shipped default is `http://localhost:8080`,
  which is honest about being a guess: correct only for somebody running
  the server on the machine they are sitting at.
- The reset runs after the admin is seeded, not before. The other order
  leaves a fresh install's token unrecorded and therefore still pending, so
  it would fire on the next restart against a password the admin had
  meanwhile chosen.
- Users that already exist when the migration runs are taken as
  `UserChosen`. Their passwords work today and upgrading the server is not
  a reason to lock anybody out of their own account; this ADR governs
  accounts created from here on.
- `/manage/users/password` decides between the two states by comparing the
  target's id to the authenticated caller's, not by comparing usernames: a
  password you set for yourself is your own choice, one set for somebody
  else is Provisional. The blanket "cannot update admin user" refusals on
  `/manage/users/active` and `DELETE /manage/users` are untouched and still
  too broad — they are the self-demotion and last-admin guards' job, and
  they belong to the Users administration work.
- That work is now in (issue #28). The blanket refusals are gone, replaced
  by three guards in `UserAdminService`, which both the portal's Users page
  and `/manage/*` go through: nobody may demote, deactivate or delete
  themselves, and no operation may leave the server without an active
  Administrator. The last-Administrator rule is checked *first*, because on
  a server with one Administrator every way of removing them is that person
  acting on themselves — so the self-rules alone would answer "ask another
  Administrator" when there is nobody to ask.
- Deactivation is guarded alongside demotion and deletion, which the ADR
  above does not list: an Administrator who cannot log in is not one the
  server can fall back on, so "the last remaining Administrator" is read as
  the last *active* one throughout.
- Self-deletion is refused for everybody, not only for an Administrator, so
  `DELETE /manage/users` lost the carve-out that let a User delete their own
  account. That is a breaking change to a documented endpoint and is said
  plainly in the README.
