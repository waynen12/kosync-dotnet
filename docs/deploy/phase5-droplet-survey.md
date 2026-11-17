# Phase 5, step 1 — droplet survey (findings)

Surveyed 2026-11-17 over SSH as `root@104.248.168.202`. **Read-only: nothing
on the box was installed, started, stopped, reloaded or edited.**

This is the droplet-side half of the investigation. The repo-side half is
[phase5-findings.md](phase5-findings.md) and the brief that scoped both is
[../phase5-deployment-task.md](../phase5-deployment-task.md). Facts here were
read off the box; where something could only be answered from outside it (DO
control panel), that is said rather than guessed.

## The box

`Notrons1-n8n` — Ubuntu 24.04.5 LTS, kernel 6.8.0-147-generic, x86_64.

| | |
|---|---|
| CPU | **1 vCPU** |
| RAM | 1.9 GiB total, ~1.19 GiB available |
| Swap | 2 GiB (`/swapfile`), unused |
| Disk | 48 GB on `/dev/vda1`, 17 GB used, **31 GB free** (36%); inodes 7% |
| Docker | 29.1.3, Compose v2.40.3, overlay2, cgroup v2 |

DigitalOcean's `do-agent` and `droplet-agent` both run. `unattended-upgrades`
is active with no `Automatic-Reboot` setting, so it patches but does not
reboot on its own.

## What runs on it

| What | How | Listens |
|---|---|---|
| Caddy 2 | container `caddy`, compose project `n8n` | `0.0.0.0:80`, `0.0.0.0:443` |
| n8n | container `n8n` (`n8nio/n8n:latest`) | `5678`, container-internal |
| Postgres 16 | container `n8n-postgres-1` (healthy) | `5432`, container-internal |
| ai-hub | Next.js 16.2.6 under **PM2** (`pm2-deploy.service`, user `deploy`) | `*:3000` |

One compose project, `n8n`, at `/opt/n8n/docker-compose.yml`, on network
`n8n_demo`, with volumes `n8n_caddy_data`, `n8n_n8n_storage` and
`n8n_postgres_storage`. ai-hub lives at `/opt/aihub` — a git clone of
`waynen12/AIResources` (branch `master`), deployed by pulling on the box and
run by PM2. There is **no self-hosted GitHub Actions runner**, so its
"Updating deploy for actions" commit implies Actions reaches in over SSH.
That is the existing deploy precedent roadmap step 3 would either follow or
deliberately depart from.

**A `ps` artefact worth not re-discovering.** A `node /usr/local/bin/n8n`
process at ~275 MB appears to run as user `deploy`. It is not a second,
host-installed n8n: `deploy` is uid 1000, the same uid as `node` inside the
container, so that is the containerised n8n seen from the host. PM2 manages
exactly one process, `aihub`.

## The reverse proxy — Caddy, and the README is wrong

**`README.md`'s Nginx Proxy Manager mention does not describe this box**,
confirming what `ROADMAP.md` suspected: it is inherited from the upstream
repo. There is no nginx, caddy, traefik, haproxy or apache *binary* on the
host at all — ports 80/443 are held by `docker-proxy`. The proxy is
containerised Caddy, and `/opt/n8n/Caddyfile` is six lines in full:

```
n8n.notrauto.org {
    reverse_proxy n8n:5678
}

hub.notrauto.org {
    reverse_proxy host-gateway:3000
}
```

Three consequences, all favourable, and the reason this phase got easier
rather than harder:

- **Adding kosync is a three-line block plus a DNS record.** The Caddyfile is
  bind-mounted (`./Caddyfile:/etc/caddy/Caddyfile`), so
  `docker exec caddy caddy reload` applies it gracefully — no container
  restart and no downtime for n8n or ai-hub.
- **Caddy proxies WebSocket upgrades transparently**, with no explicit
  `Upgrade`/`Connection` configuration and no default idle read/write timeout
  on proxied connections. This satisfies ROADMAP's Blazor `InteractiveServer`
  requirement (ADR 0003) essentially for free — it is the thing that would
  have needed careful hand-configuration under nginx.
- **Caddy already sets `X-Forwarded-Proto`/`-For`/`-Host` by default.** That
  turns the no-op `UseForwardedHeaders()` at `Program.cs:102`
  ([phase5-findings.md](phase5-findings.md)) into a bounded fix: configure
  `ForwardedHeadersOptions`, with the Caddy container on `n8n_demo` as the
  trusted proxy.

## TLS

Let's Encrypt via **Caddy's built-in ACME**, fully automatic — no certbot,
no acme.sh, no renewal cron or timer to inherit or maintain. Certificates
live in the `n8n_caddy_data` volume:

| Host | Issuer | Expires |
|---|---|---|
| `hub.notrauto.org` | Let's Encrypt `YE1` | 2026-12-30 |
| `n8n.notrauto.org` | Let's Encrypt `YE1` | 2027-01-21 |

An `acme-staging-v02` account directory is also present, so staging has been
used here before. That is the right way to rehearse a fourth hostname without
spending production rate limits.

## DNS — Cloudflare

Public DNS for `notrauto.org` is **Cloudflare** (`sonny.ns.cloudflare.com`,
`hazel.ns.cloudflare.com`). `hub` and `n8n` both resolve straight to
`104.248.168.202`, so those records are **DNS-only (grey cloud), not
proxied**. `kosync.notrauto.org` does not resolve yet. The apex has no A
record.

**Decision for the new record: grey cloud, matching the other two.** Caddy's
automatic ACME then keeps working exactly as it does today, nothing sits
between the Blazor circuit's long-lived WebSocket and the origin, and Caddy
remains the only proxy — which keeps the `X-Forwarded-*` story single-hop and
leaves `TRUSTED_PROXIES` (`Services/ProxyService.cs:80`) with one hop to
trust.

Orange cloud (proxied) was considered and rejected for now: it would hide the
origin IP, but it puts a second TLS terminator in front of the ACME challenge
and in the path of both the Blazor circuit and KOReader's `x-auth-*` headers,
and it moves client-IP attribution to `CF-Connecting-IP`, which
`ProxyService.cs` does not know about. It also would not mitigate the
exposed-port finding below, since the origin IP is already public in DNS.

## Findings that are not kosync's to fix

Recorded rather than acted on, per the brief's read-only scope.

1. **~~There is no firewall~~ — corrected 2026-11-17: there is a DO cloud
   firewall.** The operator supplied its inbound rules; see "The firewall"
   below, which supersedes this item. What was read off the box was accurate
   (`ufw` inactive, `iptables` INPUT policy `ACCEPT`, port 3000 answering
   HTTP 307 from outside) and the conclusion drawn from it was wrong. A DO
   cloud firewall is enforced upstream of the droplet, so it is invisible to
   both `ufw` and `iptables` from inside — and, unlike `ufw`, it is not
   bypassed by Docker's own iptables rules. The exposure of port 3000 is
   real but deliberate: it is an explicit allow rule.
2. **Docker has no log rotation.** There is no `/etc/docker/daemon.json`, so
   `json-file` runs with no `max-size`. Current logs are small (4 MB worst
   case) on a 48 GB disk, but this is a standing trap for an accidental
   `SyncTrace__Enabled=true` (GLOSSARY: Sync Trace), which logs reading
   positions and document hashes verbatim.
3. **`Live Restore` is disabled.** A Docker daemon restart — including an
   unattended `apt` upgrade of Docker itself — bounces every container. This
   bears directly on ROADMAP's "what does a reader with the portal open see
   mid-deploy" question: the answer is that something *other than a deploy*
   can also kill their circuit, so the decision cannot be scoped to deploys.

## The firewall — and why port 80 being shut matters

Supplied by the operator 2026-11-17, not read off the box (a DO cloud
firewall cannot be). Inbound, all sources, IPv4 and IPv6:

| Port | What |
|---|---|
| 22 | SSH |
| 443 | HTTPS |
| 3000 | ai-hub, directly — plain HTTP, bypassing Caddy |

**Port 80 is not open.** That is the fact with consequences, and it is good
news twice before it is a constraint.

- **TLS still works, via a different challenge.** Caddy has *smart challenge
  selection*: "if port 80 is blocked, it will learn to prefer the TLS-ALPN
  challenge which does not use port 80." HTTP-01 requires port 80 to be
  externally reachable and cannot succeed here. So `hub` and `n8n` hold valid
  certificates because they were issued over **TLS-ALPN-01 on 443**, and
  `kosync.notrauto.org` will be issued the same way — **no firewall change is
  needed to add it.**
- **It means there is no plaintext path to the portal at all.** Caddy listens
  on `0.0.0.0:80` inside the box, but nothing outside can reach it.

The constraint, and it is a real one:

- **`http://` does not redirect to `https://` — it times out.** Caddy's
  automatic HTTP-to-HTTPS redirect lives on port 80, which is filtered, so a
  client using an `http://` URL gets no answer rather than a redirect. For
  kosync this is the sharp edge: **`Portal__BaseUrl` must be
  `https://kosync.notrauto.org`**, and devices must be pointed at `https://`
  too. ADR 0006's refusal message names that URL to a reader whose password
  is Provisional, so an `http://` value sends them somewhere that hangs — the
  exact silent-misconfiguration failure `run-local.sh` exists to prevent
  locally.
- **The grey-cloud DNS decision above is now load-bearing, not a
  preference.** TLS-ALPN-01 requires that Caddy be the thing terminating TLS
  for the hostname. Switching `kosync` to Cloudflare's orange cloud would put
  Cloudflare's TLS terminator in front of it, breaking TLS-ALPN-01 — and
  HTTP-01 is unavailable as a fallback because port 80 is shut. That would
  break renewal, silently, up to 60 days after it was changed. The escape
  hatch, if proxying is ever wanted, is the DNS-01 challenge, which needs no
  open ports but does need a Caddy built with the Cloudflare DNS plugin
  (`xcaddy` + `caddy-dns/cloudflare`) and an API token — a different
  deployment than the stock image running there today.

Worth verifying once from the Caddy logs rather than inferring: that the
issued certificates name the `tls-alpn-01` challenge. The reasoning above is
sound but it was not read off the box.

## The backup picture is empty

The sharpest finding against roadmap step 2, and it compounds what
[phase5-findings.md](phase5-findings.md) already established about the image.

- **`sqlite3` is not installed on the host.** It is not in the runtime image
  either. So `.backup` and `VACUUM INTO` — the two correct hot-copy
  mechanisms for a WAL-mode database — are today available in **neither**
  place.
- **No backup tooling of any kind**: no restic, borg, borgmatic, duplicity,
  rclone, and **no `doctl`**.
- **No backup-shaped cron entry or systemd timer.** `/etc/cron.d` holds only
  `e2scrub_all` and `sysstat`; root has no crontab; the timers are all
  distro defaults (`logrotate`, `apt-daily`, `fwupd-refresh`,
  `droplet-agent-update`, `dpkg-db-backup`, `man-db`, `motd-news`, `sysstat`).
  Nothing exists to hook a pre-migration backup into.
- **DigitalOcean snapshots/backups cannot be seen from inside the box**, and
  without `doctl` the API cannot be queried either. **This needs checking in
  the DO control panel** — it is currently the only thing on this droplet
  that even resembles a backup.

**Cheapest correct fix is a repo change, not a droplet change:** add
`sqlite3` to the runtime stage of the `Dockerfile` (`apk add --no-cache
sqlite`). That makes `.backup`/`VACUUM INTO` available both to the app and to
a `docker exec`, requires no package installed on a shared production host,
and keeps "backup before migrating, automatically" solvable without a sidecar
container.

## Recommended shape for kosync

**A separate compose project at `/opt/kosync`, attached to the existing
`n8n_demo` network as external, publishing no host port.** Caddy then reaches
it by container name — `reverse_proxy kosync:8080` — exactly as it already
does for n8n.

Why this over the alternatives:

- *Joining `/opt/n8n/docker-compose.yml`* would couple kosync's lifecycle to
  n8n's (one `docker compose down` takes all of it) and would edit a file
  inside a clone of the upstream n8n starter kit, which conflicts on its next
  pull. It also contradicts the already-decided isolation (ROADMAP,
  "Deployment target").
- *Publishing a host port and routing via `host-gateway`*, the way ai-hub is
  reached, would expose kosync publicly on this firewall-less box — finding 1
  above. Binding `127.0.0.1` does not rescue it, because `host-gateway`
  resolves to the bridge gateway address, not loopback.

The decided own-subdomain routing and containerisation are unchanged by any
of this; both are confirmed workable as specified.

## Capacity — a non-issue at this traffic, memory the one to watch

Disk is nowhere near a constraint: 31 GB free for a database measured in
megabytes.

**The single-vCPU concern is retired.** It was raised against unknown
traffic; the operator has since confirmed the box serves **one user and
little to no traffic**. bcrypt work factor 12 (ADR 0005) is paid per
*uncached* sync auth, and `VerifiedCredentialCacheSeconds` is 300
(`appsettings.json`), so a reader syncing a book costs one KDF every five
minutes at worst. One hash on one shared core is tens to a few hundred
milliseconds, against a device that syncs on opening and closing a book.
There is nothing here to tune and no reason to lower the work factor — which
ADR 0005 rejected on its own merits anyway.

Keep it in mind only if this ever stops being a single-reader server: the
cost is per authenticating device, not per user, and it is the one thing in
this system that deliberately burns CPU.

**Memory is the tighter resource, and still adequate.** ~1.19 GiB available
against a .NET 10 Blazor Server container wanting roughly 100-250 MB,
alongside Next.js, n8n, Postgres and dockerd. The 2 GiB swapfile is
currently unused, so there is headroom behind the headroom. A memory limit
on the kosync container is worth setting — not because it is expected to
need one, but because on a 1.9 GiB box the failure mode of *not* having one
is the OOM killer choosing between kosync and Postgres.

## Still open after this survey

Step 1 is answered. These are step 2's, and the first is a question for the
operator rather than a thing to derive:

- Whether a destructive migration may **ever** run unattended. Four of nine
  migrations touch rows; `Migrate()` runs on every startup
  ([phase5-findings.md](phase5-findings.md)).
- The rollback story when a migration is one-way — one of nine `Down` methods
  is genuinely safe.
- What a reader with the portal open should experience mid-deploy, now
  widened by finding 3: a Docker daemon upgrade can do the same thing.
- Whether DigitalOcean snapshots are enabled (DO panel — not visible from the
  droplet).
