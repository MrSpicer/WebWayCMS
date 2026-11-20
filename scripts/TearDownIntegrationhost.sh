#!/usr/bin/env bash

# Tears down the Integration-Host docker compose stack started by
# scripts/StartIntegrationHost.sh (stops and removes the web + postgres containers,
# the compose network and, by default, the postgres data volume).
#
# Usage:
#   ./scripts/TearDownIntegrationhost.sh        # stop + remove containers/network and the DB volume
#   ./scripts/TearDownIntegrationhost.sh -p     # (--persist) keep the postgres data volume
set -euo pipefail
cd "$(dirname "$0")/.."

usage() {
    echo "Usage: $0 [-p|--persist]"
    echo "  -p, --persist   keep the postgres data volume (default: delete it)"
}

PERSIST=false
for arg in "$@"; do
    case "$arg" in
        -p|--persist) PERSIST=true ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

HOST_DIR="$(pwd)/WebWayCMS.TestHost"

DOWN_ARGS=()
if [[ "$PERSIST" == true ]]; then
    echo "==> Tearing down the Integration-Host compose stack (keeping volumes) ..."
else
    DOWN_ARGS+=(--volumes)
    echo "==> Tearing down the Integration-Host compose stack (dropping volumes) ..."
fi

# The compose file references CKEDITOR_LICENSE_KEY for `up`; define an empty value so
# `down` doesn't emit a substitution warning.
export CKEDITOR_LICENSE_KEY=""

cd "$HOST_DIR"

docker compose down "${DOWN_ARGS[@]}"

echo "Done."
