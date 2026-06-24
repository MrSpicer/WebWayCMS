# WebWayCMS

This software is in pre-alpha development. do not use it

This repository contains **WebWayCMS** — a modular, reusable ASP.NET Core MVC CMS
distributed as NuGet packages. To build a site on WebWayCMS, reference the
`WebWayCMS` package and supply your own branding; see
[docs/getting-started.md](docs/getting-started.md).
The architecture is documented in [docs/architecture](docs/architecture/README.md).

## License
Apache-2.0 

## Built With
* [dotnet 10.0](https://dotnet.microsoft.com)
* [ASP.Net Core MVC](https://dotnet.microsoft.com/en-us/apps/aspnet)
* [PostgreSQL](https://www.postgresql.org/)
* [Serilog](https://serilog.net/)
* [NUnit](https://nunit.org/)
* [NSubstitute](https://nsubstitute.github.io/)
* [coverlet](https://github.com/coverlet-coverage/coverlet)

### Dependencies
* [dotnet sdk](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
* [PostgreSQL](https://www.postgresql.org/)
* [dotnet-ef](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) (optional) - ```dotnet tool install --global dotnet-ef --version 10.0.0```
* [docker](https://docs.docker.com/desktop/setup/install/linux/) (optional)

This software is uses code from [ComJustinSpicer](https://github.com/MrSpicer/comjustinspicer)

## Setup

### Make Scripts Executable
```
chmod +x ./Scripts/*
```

### Setup Local Database ###
```
./Scripts/SetupLocalPostgresDBContainer.sh
```

this will create a postgres 18-alpine docker container

### Pack the CMS packages (first run)
Your host project (`MySite`) consumes the CMS as the `WebWayCMS` NuGet package
from a local feed. Pack the libraries once after cloning (and after changing CMS
source) before building or running the host:
```
./Scripts/PackLocalPackages.sh
```

### Development - Hot Reload
```
./Scripts/HotReloadRun.sh
```

The watch system monitors source files and automatically rebuilds when you save changes.

## Testing

Tests live under `tests/`, one project per source project (NUnit + NSubstitute), each isolated to
its own assembly. Coverlet enforces **100% line + branch** coverage per project on every run.

```
./Scripts/RunTests.sh                                              # run everything (with the coverage gate)
dotnet test tests/WebWayCMS.Core.Tests/WebWayCMS.Core.Tests.csproj # run a single project
```

Generated EF migrations, the Blazor Identity components (`Components/Account/*`), and the
database/seeding orchestration in `CMSExtensions` are excluded from coverage (validated by running
the app).

### Integration host (end-to-end)

The `WebWayCMS.TestHost` example boots the full CMS against a real Postgres in
Docker. It references the WebWayCMS libraries directly (project references), so the
image builds the CMS from source — no packing step. Being a throwaway test stack, all
config is hardcoded (see `WebWayCMS.TestHost/docker-compose.yml`); the only value taken
from the environment is the optional `CKEDITOR_LICENSE_KEY`. One script runs the whole
flow non-interactively and exits with a meaningful return code: it builds and starts the
compose stack, then polls `http://localhost:45847` until it answers `200`.

```
./scripts/StartIntegrationHost.sh
```

On success the stack is left running and the admin credentials
(`admin@example.com` / `ChangeMe!Strong12`) are printed; on a startup error or timeout
the script dumps the compose logs, tears the stack down, and exits non-zero.

Tear the running stack down with (pass `-v` to also delete the Postgres data volume):

```
./scripts/TearDownIntegrationhost.sh
```

## Docker

### Build Image
```
./Scripts/DockerBuild.sh
```

### Run with Docker Compose
```
./Scripts/DockerRun.sh
```