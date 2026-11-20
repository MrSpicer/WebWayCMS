#!/usr/bin/env bash

# Runs the Integration-Host example end-to-end as a scriptable smoke test:
#   1. builds and starts the self-contained docker compose stack (web + postgres).
#      All config is hardcoded (see docker-compose.yml); the only value taken from the
#      environment is the optional CKEDITOR_LICENSE_KEY. The image builds the WebWayCMS
#      libraries from source (project references) — no packing step is required.
#   2. polls http://localhost:45847 until it answers 200, then exits 0
#
# By default every run starts from a clean database: any existing stack is torn down
# together with its postgres data volume first. Pass -p/--persist to keep the volume
# (a running stack is then updated in place and the existing data reused).
#
# On failure (startup error or timeout) the script dumps the compose logs, tears
# the stack down (dropping volumes unless -p), and exits non-zero. On success the
# stack is left running so you can browse to http://localhost:45847 and log in with
# the printed credentials.
#
# Usage:
#   ./scripts/StartIntegrationHost.sh        # tear down any existing stack + DB volume, start fresh
#   ./scripts/StartIntegrationHost.sh -p     # (--persist) keep the existing DB volume
set -euo pipefail
cd "$(dirname "$0")/.."

usage() {
    echo "Usage: $0 [-p|--persist]"
    echo "  -p, --persist   keep the existing postgres data volume (default: start from a clean database)"
}

PERSIST=false
for arg in "$@"; do
    case "$arg" in
        -p|--persist) PERSIST=true ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

REPO_ROOT="$(pwd)"
HOST_DIR="$REPO_ROOT/WebWayCMS.TestHost"
TEST_URL="http://localhost:45847"
TIMEOUT_SECONDS=300
POLL_INTERVAL=5

# --- Step 0: start from a clean database ---------------------------------------
# Unless persisting, tear down any existing stack (running or stopped) and drop its
# postgres data volume, so this run seeds a fresh database. A no-op when nothing exists.
if [[ "$PERSIST" == false ]]; then
    echo "==> Tearing down any existing stack and its volumes ..."
    "$REPO_ROOT/scripts/TearDownIntegrationhost.sh"
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
    if [[ "$PERSIST" == true ]]; then
        docker compose down || true
    else
        docker compose down --volumes || true
    fi
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
            echo "  ./scripts/TearDownIntegrationhost.sh      (add -p to keep the DB volume)"
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
