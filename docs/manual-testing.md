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

Verified 2026-11-09 and 2026-11-16 against an aipaper reader, an Xteink 3 and
an Xteink 4 Pro, the last two running CrossPoint.

- [x] **A push on one device arrives on another** — the point of the whole
      tool. Pushed from the Xteink 3 and pulled it down on both others;
      pushed from the aipaper and pulled it onto the Xteink 4 Pro
- [x] **Regression protection** — `1924aa71…` went to 0.9409 from CrossPoint,
      and two later aipaper pushes at 0.9283 and 0.9315 were recorded without
      being promoted. The Book's progress did not go backwards
- [x] **Observed metadata arrives** — Documents now carry real titles
      ("They return at evening", "OLD MAN'S BEARD"), so at least one device is
      on KOReader 2026.07+ with the setting on
- [x] **Curated metadata outranks observed** — Book 2 carries a hand-typed
      "They Return at evening" over an observed "They return at evening"

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

Cross-device sync and regression protection are both confirmed above. What is
left is how the regression *reads* in the portal, which only an eye can judge.

- [ ] Open Book `1924aa71…` in the portal. It already holds a real blocked
      regression from testing — the two aipaper pushes behind CrossPoint's
      0.9409. **Only those two** should carry the Progress Regression flag,
      and the pushes that were promoted at the time should not (#38)

### B. Metadata

Arriving and curation works (above). What is left is the absent case and
whether a later push can trample a hand-typed title.

- [ ] Book 1 still has **no observed title**, so it should be showing the
      **hint** telling you to switch the setting on for that device — the one
      thing stopping the feature looking broken rather than off
- [ ] Push again to a Book you have **curated**, from a device that sends
      metadata. The hand-typed title must survive it (ADR 0007)

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
- [ ] **Signing out** is one click in the nav with no page in between
      ([#43](https://github.com/waynen12/kosync-dotnet/issues/43)), and lands on `/login`. Hovering the button says what
      signing out does and does not touch — the one thing no test can check is
      whether that tooltip is where a worried User actually finds it
- [ ] Signing out from `/change-password` works the same way, which is the
      only copy of the control a Provisional-Password User can reach
- [ ] Mistype the password in a device's **sync settings**. KOReader should
      show "Invalid credentials" and nothing suggesting the User does not
      exist ([#42](https://github.com/waynen12/kosync-dotnet/issues/42)) — the
      test pins the sentence, but only a device can show you where KOReader
      puts it

---

## Known limitation: two CrossPoint readers are one Device

Observed 2026-11-16 with an Xteink 3 and an Xteink 4 Pro both running
CrossPoint. The portal shows **one** Device for the two of them.

CrossPoint sends the literal string `crosspoint-reader` as its `device_id`,
the same on every installation, where stock KOReader sends a per-install GUID
(the aipaper's is `FC4C8C61…`). A Device *is* its `device_id` (GLOSSARY:
Device), so two readers reporting the same one are the same Device here.

Nothing the server can do about it: both units also report the display name
`CrossPoint`, so they are indistinguishable on the wire. Worth knowing because
"last synced" for that Device means whichever of the two synced most recently,
and its per-Device progress conflates both.

If CrossPoint exposes an editable device name or id in its sync settings,
setting them differently per reader would separate them. Tracked as
[#44](https://github.com/waynen12/kosync-dotnet/issues/44).

## Known, already raised — don't re-report

Nothing outstanding. #42 — a wrong password answered "User could not be
found" — is fixed; the check for it is in group E above.

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
