#!/usr/bin/env bash

# Runs the Integration-Host example end-to-end as a scriptable smoke test:
#   1. builds and starts the self-contained docker compose stack (web + postgres).
#      All config is hardcoded (see docker-compose.yml); the only value taken from the
#      environment is the optional CKEDITOR_LICENSE_KEY. The image builds the WebWayCMS
#      libraries from source (project references) — no packing step is required.
#   2. polls http://localhost:45847 until it answers 200, then exits 0
#
# On failure (startup error or timeout) the script dumps the compose logs, tears
# the stack down, and exits non-zero. On success the stack is left running so you
# can browse to http://localhost:45847 and log in with the printed credentials.
#
# Usage:
#   ./scripts/StartIntegrationHost.sh        # prompt to tear down any running stack first
#   ./scripts/StartIntegrationHost.sh -y     # tear down a running stack (with -v) without prompting
set -euo pipefail
cd "$(dirname "$0")/.."

REPO_ROOT="$(pwd)"
HOST_DIR="$REPO_ROOT/WebWayCMS.TestHost"
TEST_URL="http://localhost:45847"
TIMEOUT_SECONDS=300
POLL_INTERVAL=5

# Optional first argument: skip the prompt and tear down a running stack automatically.
AUTO_TEARDOWN=false
if [[ "${1:-}" == "-y" || "${1:-}" == "--yes" ]]; then
    AUTO_TEARDOWN=true
fi

# --- Step 0: handle an already-running stack ----------------------------------
# If a previous Integration-Host stack is still up, the postgres data volume from
# that run will be reused. Offer to tear it down (with -v, dropping the volume)
# so this run starts from a clean database.
if [[ -n "$(docker compose -f "$HOST_DIR/docker-compose.yml" ps -q 2>/dev/null)" ]]; then
    teardown_existing=false
    if [[ "$AUTO_TEARDOWN" == true ]]; then
        echo "==> An Integration-Host stack is already running; tearing it down (-v) ..."
        teardown_existing=true
    else
        read -r -p "An Integration-Host stack is already running. Tear it down (including the DB volume) before continuing? [y/N] " reply
        [[ "$reply" =~ ^[Yy]$ ]] && teardown_existing=true
    fi
    if [[ "$teardown_existing" == true ]]; then
        "$REPO_ROOT/scripts/TearDownIntegrationhost.sh" -v
    fi
fi


# --- Step 1: bring up the compose stack ---------------------------------------
# All config is hardcoded in docker-compose.yml (these are the matching values, used
# only for the success summary below). The CKEditor license key is the one value taken
# from the environment; export it (empty unless the operator set it) so compose
# substitution stays quiet.
export CKEDITOR_LICENSE_KEY="${CKEDITOR_LICENSE_KEY:-}"

cd "$HOST_DIR"

# Dump logs and tear the stack down; used on every failure path.
teardown_on_failure() {
    echo "==> Startup failed - dumping compose logs ..." >&2
    docker compose logs || true
    echo "==> Tearing down the compose stack ..." >&2
    docker compose down || true
}

echo "==> Building and starting the compose stack ..."
if ! docker compose up --build -d; then
    teardown_on_failure
    exit 1
fi

# --- Step 2: poll the test URL ------------------------------------------------
echo "==> Polling $TEST_URL (up to ${TIMEOUT_SECONDS}s) ..."
deadline=$(( SECONDS + TIMEOUT_SECONDS ))
while (( SECONDS < deadline )); do
    code="$(curl -s -L -o /dev/null -w '%{http_code}' "$TEST_URL" || true)"
    case "$code" in
        200)
            echo ""
            echo "Integration host is up at $TEST_URL"
            echo ""
            echo "Stack left running. Stop it with:"
            echo "  script/TearDownIntegrationhost.sh (-v)"
            exit 0
            ;;
        000)
            # Not accepting connections yet - keep waiting.
            echo "  ... not ready yet (no response), retrying in ${POLL_INTERVAL}s"
            ;;
        *)
            # The app responded but with an error status - treat as a failure.
            echo "ERROR: $TEST_URL returned HTTP $code" >&2
            teardown_on_failure
            exit 1
            ;;
    esac
    sleep "$POLL_INTERVAL"
done

echo "ERROR: timed out after ${TIMEOUT_SECONDS}s waiting for $TEST_URL to return 200" >&2
teardown_on_failure
exit 1
