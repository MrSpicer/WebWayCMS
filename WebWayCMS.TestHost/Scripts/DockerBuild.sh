#!/usr/bin/env bash

# Builds the self-contained Docker image for the Integration-Host example.
# The build context is the repo root so the image can build the WebWayCMS
# libraries from source (project references) — no packing required.
set -euo pipefail
cd "$(dirname "$0")/../.."

DOCKER_BUILDKIT=1 docker build -f WebWayCMS.TestHost/Dockerfile -t integration-host-webwaycms-example .

echo "Built Docker image integration-host-webwaycms-example"
