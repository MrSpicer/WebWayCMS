#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

DATA=WebWayCMS.Data/WebWayCMS.Data.csproj

echo "Restoring local dotnet tools (dotnet-ef)..."
dotnet tool restore

echo "Removing existing migrations..."
rm -rf WebWayCMS.Data/Migrations/*

echo "Creating new migration..."
dotnet ef migrations add InitialCreate -p "$DATA" -s "$DATA" -c CmsDbContext -o Migrations

echo "Migrations rebuilt successfully."
