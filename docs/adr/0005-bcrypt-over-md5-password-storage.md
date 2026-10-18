# Passwords are stored as bcrypt over the MD5 the KOReader protocol sends

KOReader sends `x-auth-key: MD5(password)` and the server has to be able
to verify that, so the MD5 cannot be eliminated without breaking protocol
fidelity. Storing it bare was defensible when one administrator used the
tool on localhost; it is not, now that ordinary readers log into a web
portal and an internet-facing deployment is planned.

Stored credentials are therefore `bcrypt(md5(password))`. The sync API
verifies the MD5 the client already sent against it. The portal hashes
the typed password with MD5 first and verifies the same value. Both paths
hand the verifier identical input, so one column serves both with no risk
of two credential columns drifting apart.

## Consequences

- Existing credentials migrate without any password reset and without
  ever needing plaintext: the stored value is already `md5(password)`, so
  the migration is `bcrypt(stored)`. The same applies to users imported
  by the LiteDB migrator.
- An MD5 still crosses the wire as a password-equivalent. That is
  inherent to the KOReader protocol and is HTTPS's job — already on the
  pre-deployment checklist.
- Verification is no longer a string comparison. Every authenticated sync
  request now runs a deliberately slow KDF, which makes the bcrypt work
  factor a throughput decision on the progress-push path, not just a
  security one. The work factor stays at the standard 12 and is
  configurable, but verified credentials are held in a short-lived
  in-memory cache so repeat pushes from an already-authenticated device
  skip the KDF. Lowering the work factor instead was rejected: it weakens
  first contact and password changes, which is where the strength actually
  matters, to speed up the case the cache already handles. Sync latency is
  what makes a reader stop trusting a sync tool, so paying 250ms on every
  page-turn batch was not acceptable either.
- A future reader will wonder why there is an MD5 nested inside a bcrypt.
  This ADR is the answer: protocol fidelity on the outside, real password
  storage on the inside.
