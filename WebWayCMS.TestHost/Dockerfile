# syntax=docker/dockerfile:1
# Self-contained image for the Integration-Host example.
#
# The build context is the repo root (see docker-compose.yml), so the whole CMS
# source tree is available. Integration-Host references the WebWayCMS libraries as
# project references, so this image builds the libraries from source — no packing.

# WebWayCMS.Presentation compiles its .scss via `npx sass` during Build, so the SDK
# image needs Node + Dart Sass (the packaged feed shipped pre-compiled CSS; building
# from source does not). Debian's apt Node is too old for current sass, so bring a
# matching Node from the official image. node:24-bookworm-slim shares the SDK image's
# Debian base for binary compatibility.
FROM node:24-bookworm-slim AS node
RUN npm install -g sass

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node + the global Dart Sass install (puts node/npm/npx/sass on PATH).
COPY --from=node /usr/local/bin/ /usr/local/bin/
COPY --from=node /usr/local/lib/node_modules/ /usr/local/lib/node_modules/

COPY . .

# Build-only NuGet config: nuget.org only. Overrides the repo-root nuget.config so
# restore does not reach for the GitHub auth feed (no token in the build).
RUN printf '%s\n' \
    '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration><packageSources><clear />' \
    '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />' \
    '</packageSources></configuration>' > /src/nuget.config

# Publish the host (builds the referenced CMS projects from source). Package versions
# come from the repo-root Directory.Packages.props copied in above.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish WebWayCMS.TestHost/Integration-Host.csproj -c Release -o /app/publish

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
ENTRYPOINT ["dotnet", "Integration-Host.dll"]
