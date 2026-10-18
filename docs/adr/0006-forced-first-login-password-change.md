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
