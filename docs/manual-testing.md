# Manual testing: the portal redesign on real devices

Phases 2.5 and 3 are built and the suite is green, but a passing suite cannot
tell you whether two KOReader devices actually sync through this. This is the
list of what that takes, what has already been confirmed, and what has not.

Sized for short sessions: each group below is roughly one sitting, and the
groups are independent apart from the order inside them.

**Running it:** `./run-local.sh` — binds the LAN so devices can reach it and
points `Portal__BaseUrl` at this machine's address, which `dotnet run` alone
does not. `./run-local.sh --fresh` starts from an empty database.

**The log** is whatever terminal that runs in. `SyncTrace__Enabled` is on, so a
request that reaches no endpoint at all still appears — that is the one thing
the per-endpoint logging cannot show you (GLOSSARY: Sync Trace).

---

## Confirmed working

Verified 2026-11-09 against an aipaper reader and an Xteink running CrossPoint.

- [x] **Fresh install** — 9 migrations onto an empty database, `admin` seeded
      with a Provisional Password and a bcrypt credential, never a bare MD5
- [x] **Forced first-login change** — both accounts went Provisional →
      UserChosen through the portal
- [x] **A Provisional Password blocks sync** — `/users/auth` and
      `PUT /syncs/progress` both 401, and the message names the portal URL
- [x] **Self-registration refused** by default (402)
- [x] **Unauthenticated portal access** redirects to `/login`
- [x] **Changing a password invalidates that User's devices** — observed, and
      exactly what ADR 0006 said would happen to every User once
- [x] **Push from both devices** lands as SyncEvents
- [x] **An unsynced document answers 200 with an empty body** (#41) — this is
      what CrossPoint needs; before it, its sync aborted on the old 502

## Not yet confirmed

### A. Sync actually syncing

The point of the tool, and still unproven: a push on one device has not yet
been shown to arrive on the other.

- [ ] Read on device 1, push. On device 2 open the **same book** and pull —
      it should offer device 1's position
- [ ] Push a **lower** percentage from device 2. The server keeps it in history
      but must not promote it; the Book's progress should not go backwards
- [ ] On that Document's detail page, **only the blocked push** carries the
      Progress Regression flag — not the ordinary pushes before it (#38)

### B. Metadata

Needs KOReader **2026.07 or newer** with **Send document metadata** switched
on, per device. The first push stored carried no metadata at all, so check the
setting before concluding the feature is broken.

- [ ] A Book shows a **real title** rather than `Book #N`
- [ ] A Book with no metadata shows the **hint** telling you to enable that
      setting — this is the thing that stops it looking broken
- [ ] Edit a Book's title and author by hand
- [ ] Push again afterwards: the edit must **survive**. Curated always beats
      observed (ADR 0007)

### C. Split Books, merging, resets

- [ ] Get the same book onto both devices with **different hashes** (convert
      one copy, or rename it if the device hashes by filename) → Split Book flag
- [ ] A **merge suggestion** appears — needs title *and* author to match
      exactly, so it depends on B
- [ ] The merge takes **two presses**, and afterwards both Documents sit on one
      Book with the furthest-along progress winning
- [ ] **Reset progress** on a Document, then push again — the push after a
      reset must **not** be flagged as a regression (#40)
- [ ] Reset **twice** on the same Document. Both resets should appear in the
      history, and neither following push should be flagged

### D. More than one reader

- [ ] A second User logs in and sees **only their own** Devices and Books
- [ ] An **Administrator** cannot see another User's Books anywhere in the
      portal — tenancy is absolute (ADR 0004)
- [ ] Users page: create, deactivate, reset a password, promote, demote
- [ ] A created User gets a **Provisional Password** and cannot sync until they
      have replaced it
- [ ] Deleting **yourself** is refused, and so is removing the **last**
      Administrator

### E. The interface

- [ ] Both detail pages at realistic density — several Documents, a long
      history, a reset and a regression in it
- [ ] **390px, 768px, 1440px**: no horizontal scrolling, and content fills the
      width rather than sitting in a narrow centred column
- [ ] **Theme toggle** survives navigation between pages
- [ ] Switching Devices/Books is a **navigation** — the back button works, and
      it is not a round trip (ADR 0003)
- [ ] Signing out — **re-check after [#43](https://github.com/waynen12/kosync-dotnet/issues/43) lands**, which removes the
      confirmation step and changes this flow

---

## Known, already raised — don't re-report

- [#42](https://github.com/waynen12/kosync-dotnet/issues/42) — a wrong password on `/users/auth` answers "User could not be
  found", which both leaks whether an account exists and misleads anyone
  debugging a typo
- [#43](https://github.com/waynen12/kosync-dotnet/issues/43) — the logout confirmation step is being removed

## If a device will not sync

In order, because each is cheaper than the next:

1. **Username and password exactly right?** A typo reads as
   `Unauthenticated ... for user [x]` in the log. A password changed in the
   portal invalidates every device holding the old one.
2. **Does the request arrive at all?** With `SyncTrace__Enabled` every request
   is logged, including ones that reach no endpoint. Nothing in the log means
   it never got here — check the address and the port.
3. **Which direction?** A client that pulls before it pushes cannot sync a book
   the server has never been told about. The first interaction with any book
   has to be a push.
