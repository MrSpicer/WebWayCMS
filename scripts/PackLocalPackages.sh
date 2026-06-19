#!/usr/bin/env bash

# Packs the WebWayCMS class libraries into the local NuGet feed (./local-nuget)
# so the host (MySite) can restore them. Run this once after cloning
# (and again after changing CMS source) before building/running the host.
#
# Note: NuGet caches a restored package by version. After re-packing the SAME
# version, clear the cached copy so the host picks up your changes:
#   dotnet nuget locals global-packages --clear   # or: rm -rf ~/.nuget/packages/webwaycms
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-Release}"
mkdir -p ./local-nuget

# Leaf-to-root so each dependency is available as it is packed.
for p in WebWayCMS.Forms WebWayCMS.Data WebWayCMS.Identity WebWayCMS.Routing \
         WebWayCMS.ContentZones WebWayCMS.Core WebWayCMS.Presentation WebWayCMS; do
  echo "Packing $p ..."
  dotnet pack "$p/$p.csproj" -c "$CONFIG" -o ./local-nuget
done

echo "Packed WebWayCMS packages into ./local-nuget"
