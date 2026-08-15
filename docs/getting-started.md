# Getting Started — Build a site on WebWayCMS

WebWayCMS ships as a set of NuGet packages. A new site is a thin
`Microsoft.NET.Sdk.Web` host that references the single **`WebWayCMS`** umbrella
package (which transitively pulls `WebWayCMS.Core`, `.Data`, `.Identity`,
`.Forms`, `.Routing`, `.ContentZones`, `.Presentation`, `.Admin`, and `.Mcp`)
and supplies only its own branding.

The `MySite` host shown below is a minimal reference shape — copy it when
standing up a new site. A working example lives in a separate repo at
[WebWayCMS.TestHost](https://github.com/MrSpicer/WebWayCMS.TestHost),
which uses project references instead of the package feed.

## 1. Prerequisites

- .NET 10 SDK
- PostgreSQL (WebWayCMS is PostgreSQL-only)
- Node.js (only if your host compiles its own Sass for branding)

## 2. Configure the package feed

Add a `nuget.config` next to your host with the feed that hosts the WebWayCMS
packages. For local development against packages built from this repo
(`dotnet pack -c Release -o ./local-nuget`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="webwaycms" value="../WebWayCMS/local-nuget" />
  </packageSources>
</configuration>
```

For real distribution, point at GitHub Packages instead (requires a
`GITHUB_TOKEN` with `read:packages`):

```xml
<add key="github" value="https://nuget.pkg.github.com/MrSpicer/index.json" />
```

## 3. Create the host project

`MySite.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WebWayCMS" Version="0.0.1" />
    <!-- Optional: pick up .cshtml branding edits without a rebuild in Development -->
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation" Version="10.0.0" />
  </ItemGroup>
</Project>
```

## 4. Program.cs

```csharp
using WebWayCMS;
using WebWayCMS.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebWayCms(builder.Configuration);
builder.Host.UseCmsSerilog();

var mvc = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvc.AddRazorRuntimeCompilation();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}

app.UseWebWayCms(); // applies migrations, seeds roles/admin + the default Home page,
                    // and configures the middleware pipeline + dynamic page routing
app.Run();
```

`AddWebWayCms(IConfiguration)` registers all CMS services and the single EF Core
`CmsDbContext` (PostgreSQL — CMS tables and ASP.NET Identity tables share one
database and one `__EFMigrationsHistory`). `UseWebWayCms()` applies migrations, runs
the startup seeders, and wires up the request pipeline including routing. No
further plumbing is required.

### Rendering-only hosts

`AddWebWayCms` / `UseWebWayCms` are aliases for the full-stack pair
`AddWebWayCmsAdmin` / `UseWebWayCmsAdmin`. If a host should render published content
but never serve the admin UI — a public front-end in front of a separately
deployed editing instance — call the rendering pair instead:

```csharp
builder.Services.AddWebWayCmsRendering(builder.Configuration);
// ...
app.UseWebWayCmsRendering();
```

That skips the admin controllers, the admin handler registry, role/admin-user
seeding, and the MCP endpoint. See
[architecture/11-deployment-modes.md](architecture/11-deployment-modes.md) for
exactly what each pair registers, and for the caveat that this is a DI boundary
rather than an assembly boundary.

## 5. Configuration keys

`appsettings.json` (secrets belong in `dotnet user-secrets`, not source control):

```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=mysite;Username=mysite;Password=..." },
  "AdminUser": { "Email": "admin@example.com", "Password": "<strong-password>" },
  "CKEditor": { "LicenseKey": "" },
  "Csp": { "Enabled": true, "ReportOnly": false, "Directives": {} },
  "Mcp": { "Enabled": false, "ApiKey": null, "Path": "/mcp" }
}
```

- `ConnectionStrings:DefaultConnection` — PostgreSQL connection (**required**;
  `AddWebWayCmsRendering` throws at startup if it is missing).
- `AdminUser:Email` / `AdminUser:Password` — seeded on first run into the `Admin`
  role. Password must satisfy the Identity policy (≥12 chars, upper/lower/digit/symbol).
- `CKEditor:LicenseKey` — for the admin rich-text editor. Empty ⇒ evaluation mode.
- `Csp` — tunes the Content-Security-Policy header. Directives you don't list keep
  the CMS default; set one to an empty string to drop it. See
  [architecture/13-security.md](architecture/13-security.md).
- `Mcp` — off by default. Setting `Enabled: true` **requires** a non-empty
  `ApiKey` or startup throws. Only mapped by the admin bootstrap path. See
  [architecture/12-mcp-server.md](architecture/12-mcp-server.md).

Set secrets for local dev:

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=mysite;Username=mysite;Password=..."
dotnet user-secrets set "AdminUser:Email" "admin@example.com"
dotnet user-secrets set "AdminUser:Password" "<strong-password>"
```

## 6. Branding (the only per-site code)

WebWayCMS provides a minimal fallback `_Layout.cshtml` so the host boots with no
branding at all. Because the host's own views are resolved **before** the
packaged (compiled) library views, you override any CMS view by adding a file of
the same name. To brand the public site, supply:

- `Views/_ViewStart.cshtml` → `@{ Layout = "_Layout"; }`
- `Views/Shared/_Layout.cshtml` — your public layout (overrides the fallback)
- Public navigation/footer partials and any `ContentZone` placeholders
- `wwwroot/` — your CSS/JS, fonts, `favicon`, etc.

The admin UI (`/wadmin`), Identity pages, content-zone editors, and their CSS/JS
come from the packages and need no host files. Admin assets are served under
`_content/WebWayCMS.Admin/...`; public assets under
`_content/WebWayCMS.Presentation/...`.

## 7. Optional startup toggles (environment variables)

All comparisons are case-insensitive against the literal string `true`.

- `WEBWAYCMS_SKIP_MIGRATIONS` — skip applying EF migrations on startup.
- `WEBWAYCMS_SKIP_ROLESEED` — skip seeding roles + the admin user.
- `WEBWAYCMS_SKIP_DEFAULTPAGE` — skip seeding the default Home/Admin pages.
- `WEBWAYCMS_SKIP_DEFAULTWIDGETS` — skip seeding widget registrations from
  `[ContentZoneComponent]`-decorated ViewComponents.
- `WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS` — skip seeding page-type registrations
  from `[PageController]`-decorated controllers.
- `WEBWAYCMS_SKIP_CODEBASEDROUTES` — skip seeding routes declared with `[CmsRoute]`.

One more variable applies only to the EF design-time tooling, not the running app:

- `WEBWAYCMS_DESIGNTIME_CONNECTION` — connection string used by
  `CmsDbContextFactory` when scaffolding migrations (default
  `Host=localhost;Database=webwaycms_designtime;Username=postgres;Password=postgres`).

## 8. Dev-loop note when iterating on the CMS itself

NuGet caches a restored package by version. If you change CMS source and re-run
`dotnet pack` **without bumping the version**, consumers keep the cached copy.
While actively developing the CMS, either bump `<Version>` in
`Directory.Build.props` per pack, or clear the cached package:

```bash
dotnet nuget locals global-packages --clear   # or: rm -rf ~/.nuget/packages/webwaycms
```
