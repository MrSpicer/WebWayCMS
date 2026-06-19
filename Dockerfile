# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install Node.js 20 for SCSS compilation (MSBuild targets use npx sass)
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# The host (MySite) consumes WebWayCMS as a NuGet package, so the
# build first packs the CMS libraries into an in-image local feed, then publishes
# the host against that feed.
COPY . .

# A build-only NuGet config: the local feed plus nuget.org (no auth feeds).
RUN printf '%s\n' \
    '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration><packageSources><clear />' \
    '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />' \
    '<add key="local-nuget" value="/src/local-nuget" />' \
    '</packageSources></configuration>' > /src/nuget.config

# Pack the CMS libraries (leaf-to-root) into the local feed.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    for p in WebWayCMS.Forms WebWayCMS.Data WebWayCMS.Identity WebWayCMS.Routing \
             WebWayCMS.ContentZones WebWayCMS.Core WebWayCMS.Presentation WebWayCMS; do \
      dotnet pack "$p/$p.csproj" -c Release -o /src/local-nuget || exit 1; \
    done

# Publish the host (restores the WebWayCMS package from the local feed).
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish MySite/MySite.csproj -c Release -o /app/publish

# Runtime stage — Alpine for smaller image size
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# ICU libraries for .NET globalization (dates, strings, etc.)
RUN apk add --no-cache icu-libs icu-data-full

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "MySite.dll"]
