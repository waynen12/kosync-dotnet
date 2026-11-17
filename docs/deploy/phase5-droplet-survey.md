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

1. **There is no firewall, and ai-hub's port 3000 is exposed to the
   internet.** `ufw` is inactive and the `iptables` INPUT policy is `ACCEPT`.
   Verified from outside the droplet: `http://104.248.168.202:3000/` answers
   HTTP 307. ai-hub is therefore reachable over plain HTTP, bypassing Caddy
   and TLS. This shapes kosync's own deployment: on this box *any*
   host-published port is public by default. DigitalOcean cloud firewalls are
   configured outside the droplet and are invisible from inside it, so the DO
   panel should be checked before concluding nothing filters traffic.
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

## The real constraint is the single CPU

Disk is a non-issue (31 GB free for a database measured in megabytes). Memory
is adequate but not generous: ~1.19 GiB available, against a .NET 10 Blazor
Server container wanting roughly 100-250 MB alongside Next.js, n8n, Postgres
and dockerd.

**What should be measured on this box before deploying is bcrypt work factor
12 on one shared core.** Per ADR 0005 that cost is paid on every uncached
sync auth, and here it contends with ai-hub and n8n for the only vCPU. The
work factor is configurable and the verified-credential cache exists for
exactly this reason — but the number should come from this hardware rather
than from a development machine.

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
