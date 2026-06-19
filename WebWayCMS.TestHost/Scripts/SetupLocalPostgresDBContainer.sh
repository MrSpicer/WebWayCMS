#!/usr/bin/env bash

# Sets up a PostgreSQL Docker container for local development of the Integration-Host example.
set -euo pipefail
cd "$(dirname "$0")/.."

CONTAINER_NAME="integration-host-db"
VOLUME_NAME="integration-host-pgdata"

# Prompt for configuration (defaults match appsettings.json).
read -p "Database name [integration-host]: " DB_NAME
DB_NAME="${DB_NAME:-integration-host}"

read -p "Database user [integration-host]: " DB_USER
DB_USER="${DB_USER:-integration-host}"

read -p "Database password [integration-host]: " DB_PASS
DB_PASS="${DB_PASS:-integration-host}"

read -p "Host port [5432]: " DB_PORT
DB_PORT="${DB_PORT:-5432}"

# Check for Docker
if ! command -v docker &>/dev/null; then
    echo "Error: Docker is not installed or not in PATH."
    exit 1
fi

# Check for existing container
if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "Container '${CONTAINER_NAME}' already exists."
    echo "WARNING: Removing the container will also delete the database volume. ALL DATA WILL BE LOST."
    read -p "Remove container and all data? [y/N]: " RECREATE
    if [[ "${RECREATE:-N}" =~ ^[Yy]$ ]]; then
        echo "Removing existing container and volume..."
        docker rm -f "${CONTAINER_NAME}"
        docker volume rm "${VOLUME_NAME}" --force
    else
        echo "Aborting."
        exit 0
    fi
fi

# Start the container
echo "Starting PostgreSQL container..."
docker run -d \
    --name "${CONTAINER_NAME}" \
    --restart unless-stopped \
    -e POSTGRES_DB="${DB_NAME}" \
    -e POSTGRES_USER="${DB_USER}" \
    -e POSTGRES_PASSWORD="${DB_PASS}" \
    -p "${DB_PORT}:5432" \
    -v "${VOLUME_NAME}:/var/lib/postgresql" \
    postgres:18-alpine

# Verify the container is running
sleep 2
if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "Container '${CONTAINER_NAME}' is running."
else
    echo "Error: Container failed to start. Check 'docker logs ${CONTAINER_NAME}' for details."
    exit 1
fi

CONNECTION_STRING="Host=localhost;Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASS}"

echo ""
echo "Connection string (the default is hardcoded in appsettings.json):"
echo "  ${CONNECTION_STRING}"
