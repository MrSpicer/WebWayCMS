#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

DATA=WebWayCMS.Data/WebWayCMS.Data.csproj

echo "Restoring local dotnet tools (dotnet-ef)..."
dotnet tool restore

echo "Removing existing migrations..."
rm -rf WebWayCMS.Data/Migrations/*

echo "Creating new migrations..."
dotnet ef migrations add InitialIdentity     -p "$DATA" -s "$DATA" -c ApplicationDbContext  -o Migrations/Identity
dotnet ef migrations add InitialArticle      -p "$DATA" -s "$DATA" -c ArticleContext        -o Migrations/Article
dotnet ef migrations add InitialContentBlock -p "$DATA" -s "$DATA" -c ContentBlockContext   -o Migrations/ContentBlock
dotnet ef migrations add InitialContentZone  -p "$DATA" -s "$DATA" -c ContentZoneContext    -o Migrations/ContentZone
dotnet ef migrations add InitialPage         -p "$DATA" -s "$DATA" -c PageContext           -o Migrations/Page

echo "Migrations rebuilt successfully."
