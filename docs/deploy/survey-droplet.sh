#!/usr/bin/env bash
#
# survey-droplet.sh — read-only survey of the DigitalOcean droplet, for
# Phase 5 of kosync-dotnet (docs/ROADMAP.md, "Survey the droplet before
# designing anything").
#
# ============================== SAFE TO READ FIRST ==========================
#
# THIS SCRIPT ONLY READS. It is meant to be inspected before it is run, and
# the whole file is short enough to read in one sitting. Specifically it:
#
#   * creates no files, directories, or temporary state anywhere;
#   * writes nothing to disk — all output goes to stdout, so redirect it
#     yourself if you want a transcript (`./survey-droplet.sh > survey.txt`);
#   * starts, stops and restarts nothing — no systemctl action verbs, no
#     `docker restart`, no `docker compose up/down`, no `nginx -s reload`;
#   * installs nothing — no apt/apk/dnf/snap/pip, no package index refresh;
#   * never runs sudo. Where a probe genuinely needs root it prints a clearly
#     marked NEEDS ROOT note with the exact command, for a human to decide on.
#
# Every external command is guarded by `command -v`, so a box missing a tool
# prints a "not installed" line and the survey carries on.
#
# `set -u` is on (an unset variable is a bug worth catching) but `set -e` is
# deliberately OFF: a probe that fails must not abort the rest of the survey,
# which is the entire point of running one.
#
# Usage:   ./survey-droplet.sh            # print the report
#          ./survey-droplet.sh > survey-$(date +%F).txt 2>&1
#
# Reading `docker ps` etc. may itself require membership of the `docker`
# group. If it does, the Docker section says so rather than escalating.
# ============================================================================

set -u
set -o pipefail

# --- output helpers ---------------------------------------------------------

section() {
    printf '\n\n'
    printf '================================================================================\n'
    printf '== %s\n' "$1"
    printf '================================================================================\n'
}

subsection() {
    printf '\n--- %s ---\n' "$1"
}

note() {
    printf '    [note] %s\n' "$1"
}

missing() {
    printf '    [not installed] %s\n' "$1"
}

needs_root() {
    printf '    [NEEDS ROOT - not run] %s\n' "$1"
}

# Run a command only if it exists; otherwise say so and move on.
try() {
    local tool="$1"
    shift
    if command -v "$tool" >/dev/null 2>&1; then
        "$tool" "$@" 2>&1
    else
        missing "$tool"
    fi
}

# Show a file if it is readable, capped so a huge config does not bury the
# report. Prints the cap it applied.
show_file() {
    local path="$1"
    local lines="${2:-120}"
    if [ -r "$path" ]; then
        printf '\n>>> %s (first %s lines)\n' "$path" "$lines"
        head -n "$lines" "$path" 2>&1
    elif [ -e "$path" ]; then
        needs_root "cat $path   # exists but is not readable as $(id -un)"
    fi
}

# List a config directory's files, then show each. Read-only throughout.
show_conf_dir() {
    local dir="$1"
    local lines="${2:-80}"
    if [ -d "$dir" ]; then
        printf '\n>>> %s/\n' "$dir"
        ls -la "$dir" 2>&1
        local f
        for f in "$dir"/*; do
            [ -f "$f" ] || continue
            show_file "$f" "$lines"
        done
    fi
}

# ============================================================================

printf '================================================================================\n'
printf '== kosync-dotnet Phase 5 — droplet survey (READ-ONLY)\n'
printf '== generated: %s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
printf '== host: %s\n' "$(hostname 2>/dev/null || echo unknown)"
printf '== run as: %s\n' "$(id 2>/dev/null || echo unknown)"
printf '================================================================================\n'
note 'This script reads only. Nothing below writes, installs, or restarts.'

# ============================================================================
section '1. OS, kernel and uptime'
# ============================================================================

subsection 'distribution'
show_file /etc/os-release 40
try lsb_release -a

subsection 'kernel and architecture'
try uname -a

subsection 'uptime and load'
try uptime
show_file /proc/loadavg 5

subsection 'virtualisation / cloud hints'
try systemd-detect-virt
if [ -r /sys/class/dmi/id/product_name ]; then
    printf 'product_name: '
    cat /sys/class/dmi/id/product_name 2>&1
fi
# DigitalOcean metadata: a plain HTTP GET against the link-local service.
# Read-only, and it never leaves the droplet's own network.
if command -v curl >/dev/null 2>&1; then
    printf '\nDO metadata (region / id, 3s timeout):\n'
    curl -s --max-time 3 http://169.254.169.254/metadata/v1/region 2>&1
    printf '\n'
    curl -s --max-time 3 http://169.254.169.254/metadata/v1/id 2>&1
    printf '\n'
else
    missing 'curl (skipping DigitalOcean metadata probe)'
fi

# ============================================================================
section '2. Resources: disk, memory, CPU'
# ============================================================================

subsection 'filesystems (df -h)'
try df -h

subsection 'inodes (df -i) — a full inode table looks like a full disk'
try df -i

subsection 'memory and swap (free -h)'
try free -h
show_file /proc/meminfo 8

subsection 'CPU'
if command -v nproc >/dev/null 2>&1; then
    printf 'cores: '
    nproc 2>&1
fi
if command -v lscpu >/dev/null 2>&1; then
    lscpu 2>&1 | grep -Ei 'model name|^cpu\(s\)|core|socket|mhz' || true
fi
note 'Relevant because bcrypt work factor 12 runs per uncached sync auth,'
note 'and Blazor InteractiveServer holds per-circuit state server-side.'

subsection 'largest consumers under /var and /srv (depth 1, read-only)'
if command -v du >/dev/null 2>&1; then
    for d in /var/lib /var/log /srv /opt /home; do
        [ -d "$d" ] || continue
        printf '\n%s:\n' "$d"
        du -h --max-depth=1 "$d" 2>/dev/null | sort -h | tail -15
    done
    note 'Permission-denied subtrees are skipped silently; re-run as root for'
    note 'a complete picture if a number below looks implausibly small.'
else
    missing du
fi

# ============================================================================
section '3. Reverse proxy — what is in front of things'
# ============================================================================
note 'The README mentions Nginx Proxy Manager, but ROADMAP.md says that is'
note 'inherited from the upstream repo and may not describe this box. The'
note 'point of this section is to find out, not to confirm.'

subsection 'which proxy binaries exist'
for p in nginx caddy traefik haproxy apache2 httpd envoy; do
    if command -v "$p" >/dev/null 2>&1; then
        printf '  present: %-10s -> %s\n' "$p" "$(command -v "$p")"
    else
        printf '  absent : %s\n' "$p"
    fi
done

subsection 'proxy versions (no config reload, no test write)'
if command -v nginx >/dev/null 2>&1; then
    nginx -v 2>&1
    # -T dumps the fully resolved config to stdout. Read-only, but it can
    # need root to read included files and it can be long.
    if [ "$(id -u)" = "0" ]; then
        printf '\n>>> nginx -T (resolved config)\n'
        nginx -T 2>&1
    else
        needs_root 'nginx -T   # dumps the fully resolved nginx config'
    fi
fi
if command -v caddy >/dev/null 2>&1; then
    caddy version 2>&1
fi
if command -v traefik >/dev/null 2>&1; then
    traefik version 2>&1
fi
if command -v haproxy >/dev/null 2>&1; then
    haproxy -v 2>&1
fi

subsection 'proxy service state (status only — never start/stop/reload)'
if command -v systemctl >/dev/null 2>&1; then
    for svc in nginx caddy traefik haproxy apache2 httpd; do
        if systemctl list-unit-files "${svc}.service" >/dev/null 2>&1; then
            printf '\n%s:\n' "$svc"
            systemctl is-enabled "${svc}.service" 2>&1
            systemctl is-active "${svc}.service" 2>&1
        fi
    done
else
    missing systemctl
fi

subsection 'nginx config on disk'
show_file /etc/nginx/nginx.conf 80
show_conf_dir /etc/nginx/conf.d 100
show_conf_dir /etc/nginx/sites-enabled 100
note 'sites-enabled entries are usually symlinks; ls -la above shows targets.'

subsection 'Caddy config on disk'
show_file /etc/caddy/Caddyfile 150
show_conf_dir /etc/caddy/conf.d 80
if command -v curl >/dev/null 2>&1; then
    printf '\nCaddy admin API (localhost:2019, 2s timeout):\n'
    curl -s --max-time 2 http://127.0.0.1:2019/config/ 2>&1 | head -c 4000
    printf '\n'
fi

subsection 'Traefik config on disk'
show_file /etc/traefik/traefik.yml 120
show_file /etc/traefik/traefik.toml 120
show_conf_dir /etc/traefik/dynamic 80

subsection 'Nginx Proxy Manager (containerised — hosts live in its own DB)'
note 'NPM stores proxy hosts in SQLite/MySQL inside its container, not in a'
note 'readable text config. Presence is detectable; the host list is not,'
note 'without querying its database. Check the NPM web UI instead.'
for d in /opt/npm /opt/nginx-proxy-manager /data/nginx /srv/npm; do
    if [ -d "$d" ]; then
        printf '\npossible NPM data dir: %s\n' "$d"
        ls -la "$d" 2>&1 | head -20
    fi
done

# ============================================================================
section '4. TLS — how certificates are issued and renewed'
# ============================================================================

subsection 'ACME clients present'
for c in certbot acme.sh lego dehydrated; do
    if command -v "$c" >/dev/null 2>&1; then
        printf '  present: %-10s -> %s\n' "$c" "$(command -v "$c")"
    else
        printf '  absent : %s\n' "$c"
    fi
done
# acme.sh usually installs to ~/.acme.sh rather than onto PATH.
for home in /root /home/*; do
    [ -d "$home/.acme.sh" ] && printf '  present: acme.sh install at %s/.acme.sh\n' "$home"
done

subsection 'certbot certificates'
if command -v certbot >/dev/null 2>&1; then
    certbot --version 2>&1
    if [ "$(id -u)" = "0" ]; then
        certbot certificates 2>&1
    else
        needs_root 'certbot certificates   # lists managed certs and expiry'
    fi
fi
if [ -d /etc/letsencrypt/live ]; then
    printf '\n/etc/letsencrypt/live:\n'
    ls -la /etc/letsencrypt/live 2>&1
else
    note '/etc/letsencrypt/live absent or unreadable — either no certbot, or'
    note 'TLS is handled inside a container (Caddy/Traefik/NPM all do this).'
fi
show_conf_dir /etc/letsencrypt/renewal 60
show_conf_dir /etc/letsencrypt/renewal-hooks/deploy 40

subsection 'renewal automation (timers and cron — listed, never run)'
if command -v systemctl >/dev/null 2>&1; then
    printf '\nsystemd timers:\n'
    systemctl list-timers --all --no-pager 2>&1 | head -30
fi
show_file /etc/cron.d/certbot 20
if command -v crontab >/dev/null 2>&1; then
    printf '\ncrontab for %s:\n' "$(id -un)"
    crontab -l 2>&1
    needs_root "crontab -l -u root   # root's crontab, where acme.sh usually lives"
fi
if [ -d /etc/cron.d ]; then
    printf '\n/etc/cron.d:\n'
    ls -la /etc/cron.d 2>&1
fi

subsection 'what the live endpoints actually serve (local TLS probe)'
note 'Read-only HTTPS GETs against the known hostnames, to see the issuer and'
note 'expiry as a client sees them. 5s timeouts; no state is changed.'
if command -v openssl >/dev/null 2>&1; then
    for host in hub.notrauto.org n8n.notrauto.org kosync.notrauto.org; do
        printf '\n=== %s ===\n' "$host"
        echo | timeout 8 openssl s_client -connect "${host}:443" -servername "$host" 2>/dev/null \
            | openssl x509 -noout -issuer -subject -dates -ext subjectAltName 2>&1 \
            || printf '  no TLS answer (host may not exist yet — expected for kosync)\n'
    done
else
    missing 'openssl (skipping certificate probe)'
fi

# ============================================================================
section '5. Docker'
# ============================================================================

if ! command -v docker >/dev/null 2>&1; then
    missing 'docker — nothing else in this section can run'
else
    subsection 'versions'
    docker --version 2>&1
    docker compose version 2>&1 || note 'docker compose (v2 plugin) not available'
    try docker-compose --version

    # Everything below needs the daemon socket. If the invoking user is not in
    # the docker group this fails with a permission error; say so once rather
    # than escalating.
    if ! docker info >/dev/null 2>&1; then
        needs_root 'docker info / docker ps   # daemon socket not reachable as this user'
        note 'Either add the user to the docker group (a change — not this'
        note 'script'"'"'s business) or re-run this section as root.'
    else
        subsection 'daemon info (storage driver, root dir, resource limits)'
        docker info 2>&1 | grep -Ei \
            'server version|storage driver|logging driver|cgroup|docker root dir|total memory|cpus|live restore|registry' || true

        subsection 'disk used by Docker (docker system df)'
        docker system df -v 2>&1 | head -60

        subsection 'containers — running and stopped (docker ps -a)'
        docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' 2>&1

        subsection 'restart policies and mounts per container'
        for c in $(docker ps -a --format '{{.Names}}' 2>/dev/null); do
            printf '\n=== %s ===\n' "$c"
            docker inspect "$c" \
                --format 'image:   {{.Config.Image}}
restart: {{.HostConfig.RestartPolicy.Name}}
user:    {{.Config.User}}
network: {{range $k,$v := .NetworkSettings.Networks}}{{$k}} ({{$v.IPAddress}}) {{end}}
mounts:  {{range .Mounts}}
         {{.Type}} {{.Source}} -> {{.Destination}} (rw={{.RW}}){{end}}' 2>&1
            note 'Env vars deliberately not dumped: they hold ADMIN_PASSWORD-class secrets.'
        done

        subsection 'volumes (docker volume ls)'
        docker volume ls 2>&1
        printf '\nvolume mountpoints and which containers use them:\n'
        for v in $(docker volume ls -q 2>/dev/null); do
            printf '\n  * volume %s\n' "$v"
            docker volume inspect "$v" --format '  driver: {{.Driver}}
  mount:  {{.Mountpoint}}
  labels: {{range $k,$val := .Labels}}{{$k}}={{$val}} {{end}}' 2>&1
            users=$(docker ps -a --filter "volume=$v" --format '{{.Names}}' 2>/dev/null | tr '\n' ' ')
            printf '  used by: %s\n' "${users:-(nothing)}"
        done
        note 'A volume with no container is the "docker compose down -v took it"'
        note 'hazard from ROADMAP.md, seen from the other side.'

        subsection 'compose projects (docker compose ls)'
        docker compose ls --all 2>&1 || note 'docker compose ls unavailable'
        printf '\ncompose project labels on existing containers:\n'
        docker ps -a --format '{{.Names}}' 2>/dev/null | while read -r c; do
            proj=$(docker inspect "$c" --format '{{index .Config.Labels "com.docker.compose.project"}}' 2>/dev/null)
            file=$(docker inspect "$c" --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' 2>/dev/null)
            printf '  %-28s project=%-20s file=%s\n' "$c" "${proj:-none}" "${file:-none}"
        done
        note 'The config_files path is where the compose file kosync would join'
        note '(or deliberately not join) actually lives.'

        subsection 'networks'
        docker network ls 2>&1

        subsection 'images'
        docker images 2>&1 | head -30
    fi
fi

# ============================================================================
section '6. What else is running'
# ============================================================================
note 'ai-hub and n8n are known to be here. Confirm how each is deployed —'
note 'ROADMAP.md says ai-hub runs directly rather than containerised.'

subsection 'enabled systemd services (not docker/system units)'
if command -v systemctl >/dev/null 2>&1; then
    systemctl list-units --type=service --state=running --no-pager 2>&1 | head -60
    printf '\nenabled units matching the apps we expect:\n'
    systemctl list-unit-files --no-pager 2>&1 | grep -Ei 'hub|kosync|n8n|node|pm2|nginx|caddy|traefik|docker' || true
else
    missing systemctl
fi

subsection 'process managers'
try pm2 list
if command -v node >/dev/null 2>&1; then
    printf 'node: '
    node --version 2>&1
fi
if command -v dotnet >/dev/null 2>&1; then
    printf '\ndotnet runtimes already on the host:\n'
    dotnet --list-runtimes 2>&1
    note 'Not required — kosync is containerised (ROADMAP.md, Deployment target).'
else
    missing 'dotnet (expected: kosync runs in a container)'
fi

subsection 'top processes by memory'
if command -v ps >/dev/null 2>&1; then
    ps -eo pid,user,%cpu,%mem,rss,comm --sort=-rss 2>&1 | head -20
fi

subsection 'is sqlite3 available on the HOST?'
if command -v sqlite3 >/dev/null 2>&1; then
    printf '  present: %s\n' "$(command -v sqlite3)"
    sqlite3 --version 2>&1
    note 'Good: `sqlite3 <db> ".backup" <out>` can run on the host against a'
    note 'bind-mounted database. It is NOT in the kosync runtime image'
    note '(mcr.microsoft.com/dotnet/aspnet:10.0-alpine ships no sqlite3).'
else
    missing 'sqlite3 on the host'
    note 'This is the deciding fact for the backup mechanism. Without it the'
    note 'options are a sidecar container, adding it to the image, or doing'
    note 'VACUUM INTO from inside the app. See phase5-findings.md item 3.'
fi

# ============================================================================
section '7. Listening ports'
# ============================================================================
note 'Which port kosync can take, and whether 8080 is already claimed.'

if command -v ss >/dev/null 2>&1; then
    subsection 'ss -ltnp (TCP listeners)'
    ss -ltnp 2>&1
    note 'Process names appear only for root; the ports and addresses are'
    note 'visible either way.'
    subsection 'ss -lunp (UDP listeners)'
    ss -lunp 2>&1
elif command -v netstat >/dev/null 2>&1; then
    subsection 'netstat -ltnp'
    netstat -ltnp 2>&1
else
    missing 'ss and netstat'
fi

subsection 'docker-published ports (host side)'
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    docker ps -a --format '{{.Names}}: {{.Ports}}' 2>&1
fi

subsection 'firewall state (read-only)'
if command -v ufw >/dev/null 2>&1; then
    if [ "$(id -u)" = "0" ]; then
        ufw status verbose 2>&1
    else
        needs_root 'ufw status verbose'
    fi
fi
if command -v iptables >/dev/null 2>&1; then
    if [ "$(id -u)" = "0" ]; then
        iptables -S 2>&1 | head -40
    else
        needs_root 'iptables -S   # -S lists rules; it does not change them'
    fi
fi
if command -v firewall-cmd >/dev/null 2>&1; then
    firewall-cmd --list-all 2>&1
fi
note 'DigitalOcean cloud firewalls are configured outside the droplet and do'
note 'not appear here — check the DO control panel separately.'

# ============================================================================
section '8. DNS and hostname'
# ============================================================================

subsection 'local identity'
try hostname -f
try hostnamectl
show_file /etc/hostname 5
show_file /etc/hosts 30

subsection 'resolver'
show_file /etc/resolv.conf 20
try resolvectl status

subsection 'public addresses as seen from here'
if command -v ip >/dev/null 2>&1; then
    printf '\nlocal addresses:\n'
    ip -brief address 2>&1
    printf '\ndefault route:\n'
    ip route get 1.1.1.1 2>&1
fi
if command -v curl >/dev/null 2>&1; then
    printf '\nDO metadata public IPv4:\n'
    curl -s --max-time 3 http://169.254.169.254/metadata/v1/interfaces/public/0/ipv4/address 2>&1
    printf '\n'
fi

subsection 'does each name resolve, and to this box?'
for host in notrauto.org hub.notrauto.org n8n.notrauto.org kosync.notrauto.org; do
    printf '\n=== %s ===\n' "$host"
    if command -v dig >/dev/null 2>&1; then
        dig +short "$host" A 2>&1
        dig +short "$host" AAAA 2>&1
        dig +short "$host" CNAME 2>&1
    elif command -v host >/dev/null 2>&1; then
        host "$host" 2>&1
    elif command -v getent >/dev/null 2>&1; then
        getent hosts "$host" 2>&1
    else
        missing 'dig, host and getent — cannot resolve'
    fi
done
note 'kosync.notrauto.org returning nothing is the expected answer today;'
note 'ROADMAP.md says the subdomain costs one DNS record and has not been'
note 'created. Compare its eventual record shape to hub/n8n above.'

subsection 'nameservers for the zone (who to add the record at)'
if command -v dig >/dev/null 2>&1; then
    dig +short notrauto.org NS 2>&1
fi

# ============================================================================
section '9. Existing backup arrangements'
# ============================================================================
note 'ROADMAP.md requires "backup before migrating, automatically, not as a'
note 'thing to remember". Worth knowing whether something already exists to'
note 'hook into before designing a new mechanism.'

subsection 'backup tooling present'
for b in restic borg borgmatic duplicity rclone rsnapshot doctl; do
    if command -v "$b" >/dev/null 2>&1; then
        printf '  present: %-12s -> %s\n' "$b" "$(command -v "$b")"
    else
        printf '  absent : %s\n' "$b"
    fi
done

subsection 'backup-looking timers, cron entries and directories'
if command -v systemctl >/dev/null 2>&1; then
    systemctl list-timers --all --no-pager 2>&1 | grep -Ei 'backup|snapshot|restic|borg|dump' \
        || note 'no backup-looking systemd timers'
fi
for d in /etc/cron.daily /etc/cron.weekly; do
    [ -d "$d" ] && { printf '\n%s:\n' "$d"; ls -la "$d" 2>&1; }
done
for d in /var/backups /srv/backups /backup /opt/backups; do
    [ -d "$d" ] && { printf '\n%s:\n' "$d"; ls -la "$d" 2>&1 | head -20; }
done
note 'DigitalOcean droplet snapshots/backups are a control-panel setting and'
note 'are invisible from inside the box. Check the DO panel separately — a'
note 'weekly image snapshot is not a substitute for a pre-migration copy of'
note 'the SQLite file, but it does change how bad a mistake can get.'

# ============================================================================
section 'END OF SURVEY'
# ============================================================================
note 'Nothing above wrote, installed, restarted or escalated.'
note 'Any [NEEDS ROOT] line names a command for a human to run deliberately.'
note 'Next: phase5-findings.md, "Questions only the droplet can answer".'
printf '\n'
