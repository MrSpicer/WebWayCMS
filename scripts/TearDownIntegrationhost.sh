#!/usr/bin/env bash

# Tears down the Integration-Host docker compose stack started by
# scripts/StartIntegrationHost.sh (stops and removes the web + postgres containers
# and the compose network).
#
# Usage:
#   ./scripts/TearDownIntegrationhost.sh        # stop + remove containers/network
#   ./scripts/TearDownIntegrationhost.sh -v     # also delete the postgres data volume
set -euo pipefail
cd "$(dirname "$0")/.."

HOST_DIR="$(pwd)/WebWayCMS.TestHost"

DOWN_ARGS=()
if [[ "${1:-}" == "-v" || "${1:-}" == "--volumes" ]]; then
    DOWN_ARGS+=(--volumes)
fi

# The compose file references CKEDITOR_LICENSE_KEY for `up`; define an empty value so
# `down` doesn't emit a substitution warning.
export CKEDITOR_LICENSE_KEY=""

cd "$HOST_DIR"

echo "==> Tearing down the Integration-Host compose stack ..."
docker compose down "${DOWN_ARGS[@]}"

echo "Done."
