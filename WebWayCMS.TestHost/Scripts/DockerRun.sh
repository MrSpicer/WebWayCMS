#!/usr/bin/env bash
# Runs docker compose up for the self-contained Integration-Host example. All config
# is hardcoded in docker-compose.yml; the only value taken from the environment is the
# optional CKEditor license key, read from dotnet user-secrets when present.

set -euo pipefail
cd "$(dirname "$0")/.."

PROJ="Integration-Host.csproj"

SECRETS=$(dotnet user-secrets list --project "$PROJ" 2>/dev/null || true)

# The CKEditor license key is optional; default to empty when unset.
CKEDITOR_LICENSE_KEY=$(echo "$SECRETS" | grep "^CKEditor:LicenseKey = " | sed 's/^CKEditor:LicenseKey = //' || true)

export CKEDITOR_LICENSE_KEY

exec docker compose up
