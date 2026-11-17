#!/usr/bin/env bash
# Run the server for testing against real KOReader devices on the LAN.
#
# `dotnet run` on its own binds to localhost, which a device cannot reach, and
# leaves Portal__BaseUrl at its localhost default - so a device refused for
# holding a Provisional Password is told to go somewhere it cannot resolve
# (ADR 0006). Both are easy to get wrong and neither fails loudly.
#
#   ./run-local.sh            # bind the LAN, trace on
#   ./run-local.sh --fresh    # ... after deleting the database first
#   PORT=8080 ./run-local.sh  # a different port
#   HOST_IP=10.0.0.5 ./run-local.sh   # if the address is guessed wrong
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

PORT="${PORT:-5181}"

# The address a device on the same network reaches this machine at. Guessed
# from the default route rather than written down: it changes with the network
# and a stale one is the failure this script exists to prevent.
if [[ -z "${HOST_IP:-}" ]]; then
  HOST_IP="$(ip route get 1.1.1.1 2>/dev/null | grep -oP 'src \K[\d.]+' | head -1 || true)"
fi

if [[ -z "$HOST_IP" ]]; then
  echo "Could not work out this machine's LAN address. Set it explicitly:" >&2
  echo "  HOST_IP=192.168.0.248 ./run-local.sh" >&2
  exit 1
fi

if [[ "${1:-}" == "--fresh" ]]; then
  # Deliberately interactive: the whole point of --fresh is that it destroys
  # whatever you were testing against.
  read -rp "Delete the database and start empty? [y/N] " reply
  [[ "$reply" == [yY] ]] || { echo "Left alone."; exit 1; }
  # SQLite runs in WAL mode, so the -wal and -shm files are part of the
  # database. Removing only the .sqlite3 leaves the other two behind.
  rm -f data/kosync.sqlite3 data/kosync.sqlite3-wal data/kosync.sqlite3-shm
  echo "Database deleted. It will be recreated with admin / admin."
fi

if ss -ltn 2>/dev/null | grep -q ":${PORT}\b"; then
  echo "Port ${PORT} is already in use - something is still running." >&2
  exit 1
fi

echo "Portal      http://${HOST_IP}:${PORT}"
echo "Devices     point KOReader at http://${HOST_IP}:${PORT}"
echo "Sync trace  on (SyncTrace__Enabled=true)"
echo

ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
ASPNETCORE_URLS="http://0.0.0.0:${PORT}" \
Portal__BaseUrl="http://${HOST_IP}:${PORT}" \
SyncTrace__Enabled="${SyncTrace__Enabled:-true}" \
Logging__LogLevel__Microsoft_EntityFrameworkCore=Warning \
SINGLE_LINE_LOGGING=true \
exec dotnet run --no-launch-profile
