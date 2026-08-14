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

## Features

WebWayCMS is a code-first, attribute-driven CMS: you declare page types, widgets, content types,
and routes as C# classes and attributes rather than configuring them through an admin UI, then
consume the whole thing as a NuGet package.

- **Code-first extensibility** — custom page types (`[PageController]`), widgets
  (`[ContentZoneComponent]`), content types (`AdminCrudModel<T>`), and code-based routes
  (`[CmsRoute]`) are declared in C#, with no per-type admin plumbing to write.
- **Generic admin CRUD framework** — a single controller drives list/edit/version-history/
  drag-reorder for every content type automatically, including types you add yourself.
- **Database-backed content zones** — named slots in views hold ordered widget instances with
  inline add/remove/reorder editing.
- **Built-in content versioning** — every content type gets version history and a draft/publish workflow for free (identity split from version; edits are drafts until published).
- **Two deployment modes from one codebase** — full admin, or a rendering-only mode for a public
  front end with no admin surface.
- **MCP server** — exposes the entire admin CRUD surface to AI agents generically (no per-type tool
  code), so any content type you add is automatically usable by an AI agent.
- **Security defaults baked in** — configurable Content-Security-Policy, server-side rich-text
  sanitization at the single save choke point, Identity account lockout and hardened auth cookies,
  and per-IP rate limiting on auth endpoints.
- **Open source, self-hosted** — Apache-2.0, PostgreSQL-only, .NET 10 / ASP.NET Core MVC.

### How this compares

> verify against each vendor's current docs.

| | WebWayCMS | Sitefinity | Sitecore | Umbraco | Kentico |
|---|---|---|---|---|---|
| **License / cost** | Open source, free (Apache-2.0) | Commercial license | Commercial license | OSS core, free; paid Cloud/enterprise tiers | Commercial license |
| **Database support** | PostgreSQL | SQL Server, Oracle | SQL Server (content); MongoDB used historically for xDB/analytics | SQL Server, SQLite | SQL Server |
| **ORM / data layer** | Entity Framework Core | Telerik Data Access ORM (formerly OpenAccess ORM) — in-house | No default ORM — native item-based Sitecore API; Third party| NPoco | Proprietary in-house ORM |
| **Runtime** | .Net Core (.NET 10) | .NET Framework, with ASP.NET Core support added incrementally (hybrid two-tier architecture) | .NET Framework 4.8 (XP and XM Cloud backend); ASP.NET Core SDK available for headless front-end rendering | ASP.NET Core (.NET 8/9 depending on version) | Hybrid — Xperience 13 admin is ASP.NET Web Forms (.NET Framework), live site .NET Framework or .NET 6; newer "Xperience by Kentico" is ASP.NET Core |
| **Split Admin Rendering** | Yes, configurable (full admin vs. rendering-only mode) | (Framework) No | Yes  | No | Yes — (split projects) |
| **Content versioning/history** | Yes | Yes | Yes | Yes | Yes |
| **AI Ready** | Yes — built-in MCP server, generic across all content types | No | Yes | Yes | No |
| **Output caching** | Coming Soon!* | Yes | Yes | Yes | Yes |
| **Visual/drag-and-drop page builder** | Coming Soon!* | Yes | Yes | Yes | Yes |
| **Headless** | Coming Soon!*  | Yes | Yes (native for XM Cloud) | Yes (Content Delivery API) | Yes |
| **Multi-site / multi-tenant** | Coming Soon!* | Yes | Yes | Yes | Yes |
| **Localization / multilingual content** | Coming Soon!* | Yes | Yes | Yes | Yes |
| **Personalization / A-B testing** | Coming Soon!* | Yes, first-class | Yes, first-class | Limited, via marketplace packages | Yes, first-class |
| **Content approval/workflow engine** | Coming Soon!* | Yes, configurable | Yes, configurable | Yes, configurable | Yes, configurable |

*maybe

## Setup

Repo-level scripts live in `./scripts/`.

### Initialize submodule
The integration host lives in a [separate repository](https://github.com/MrSpicer/WebWayCMS.TestHost)
and is linked as a git submodule:
```
git submodule update --init --recursive
```

### Content-Security-Policy configuration

The CMS emits a `Content-Security-Policy` header with secure defaults that keep the admin UI working
out of the box. A host can tune it from the `"Csp"` section of its `appsettings.json` without touching
CMS code. Directives you don't list keep the CMS default; set one to an empty string to drop it.

```jsonc
"Csp": {
  "Enabled": true,        // set false to disable the header entirely
  "ReportOnly": false,    // true emits Content-Security-Policy-Report-Only (monitor without enforcing)
  "Directives": {
    "script-src": "'self' https://cdn.ckeditor.com https://my-cdn.example",
    "img-src": "'self' data: https://my-cdn.example"
  }
}
```

### MCP server (optional)

The CMS can expose its admin feature set to AI agents over the Model Context Protocol. It is
**off by default**; enable it from the `"Mcp"` section and supply an API key (user-secrets or
environment — never source). The bearer token is the security boundary, since the endpoint runs
with effective admin authority.

```jsonc
"Mcp": {
  "Enabled": true,
  "ApiKey": "<generated-key>",
  "Path": "/mcp"          // default
}
```

The endpoint is only mapped by the admin bootstrap path; a rendering-only host never exposes it.
See [docs/architecture/12-mcp-server.md](docs/architecture/12-mcp-server.md).

### CKEditor license key (optional)

The admin rich-text editor loads CKEditor 5 from `cdn.ckeditor.com`. Supply your license key via
`CKEditor:LicenseKey`; leave it empty to run CKEditor in evaluation mode.

```json
"CKEditor": { "LicenseKey": "" }
```

## Testing

Tests live under `tests/`, one project per source project (NUnit + NSubstitute), each isolated to
its own assembly. Coverlet enforces **100% line + branch** coverage per project on every run.

```
./scripts/RunTests.sh                                              # run everything (with the coverage gate)
dotnet test tests/WebWayCMS.Core.Tests/WebWayCMS.Core.Tests.csproj # run a single project
```

Generated EF migrations, the scaffolded ASP.NET Identity Razor Pages, and the
database/seeding orchestration in `WebWayCmsApplicationBuilderExtensions` (migrations, role seeding, widget/page-controller/
code-based-route assembly scanning and registration) are excluded from coverage (validated by
running the app).

### Integration host (end-to-end)

The integration host lives at **[WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost)** —
a separate repository linked as a git submodule. It boots the full CMS against a real
Postgres in Docker, referencing the WebWayCMS libraries directly (project references) so the
image builds the CMS from source — no packing step. Being a throwaway test stack, all config
is hardcoded (see `WebWayCMS.TestHost/docker-compose.yml`); the only value taken from the
environment is the optional `CKEDITOR_LICENSE_KEY`. One script runs the whole flow
non-interactively and exits with a meaningful return code: it builds and starts the compose
stack, then polls `http://localhost:45847` until it answers `200`.

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
