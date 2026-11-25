# Deploying to the droplet

The artefacts for the hand-run first deploy (issue #54,
[phase5-spec.md](phase5-spec.md) task 8). Read
[phase5-droplet-survey.md](phase5-droplet-survey.md) first if you want the
reasoning behind any of this — this file only says what to do.

## The compose file

[docker-compose.yml](docker-compose.yml) is written for `/opt/kosync` on the
droplet — a separate compose project from `/opt/n8n`, joined to its
`n8n_demo` network as external, with no published port (Caddy reaches it by
container name; the DO firewall allows only 22, 443 and 3000 inbound).

Two values are placeholders because nothing in this repo can know them
ahead of time:

- `ADMIN_PASSWORD` — set to a strong password before the first
  `docker compose up`. It is seed-only (ADR 0006): it applies once, when the
  admin row is first created, and editing it afterwards does nothing.
- `ForwardedHeaders__KnownNetworks` — the `n8n_demo` subnet, read off the
  droplet with `docker network inspect n8n_demo` (the `Subnet` field under
  `IPAM`). Trusting the subnet rather than a single container's address is
  deliberate: a container on a user-defined bridge network can be
  re-assigned its address on restart.

Validate before copying it over:

```
docker compose -f docker-compose.yml config
```

## The Caddy block

Three lines, added directly to the box's existing `/opt/n8n/Caddyfile` — not
a file this repo owns, since that Caddyfile is already the box's shared edge
config (it already routes `hub.notrauto.org`), not n8n's private one:

```
kosync.notrauto.org {
    reverse_proxy kosync:8080
}
```

Apply it without restarting anything:

```
docker exec caddy caddy reload
```

Graceful — no downtime for n8n or ai-hub, because the Caddyfile is
bind-mounted into the `caddy` container rather than baked into its image.

## First deploy order

Order matters — see [phase5-spec.md](phase5-spec.md#the-first-deploy-by-hand)
for the full sequence and why. In short: DNS record (grey cloud) → image tag
→ `docker compose up -d` and confirm healthy → Caddy block + reload → verify.

**Nothing here is verified until that first deploy runs.** `docker compose
config` validating and a reviewer being able to follow this file are as far
as a repo change can go.
