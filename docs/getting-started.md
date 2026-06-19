# Getting Started — Build a site on WebWayCMS

WebWayCMS ships as a set of NuGet packages. A new site is a thin
`Microsoft.NET.Sdk.Web` host that references the single **`WebWayCMS`** umbrella
package (which transitively pulls `WebWayCMS.Core`, `.Data`, `.Identity`,
`.Forms`, `.Routing`, `.ContentZones`, and `.Presentation`) and supplies only its
own branding.

The `MySite` host shown below is a minimal reference shape — copy it when
standing up a new site.

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
builder.Host.UseCmsSerilog(builder.Configuration);

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

app.EnsureCMS();   // applies migrations, seeds roles/admin + the default Home page,
                   // and configures the middleware pipeline + dynamic page routing
app.Run();
```

`AddWebWayCms(IConfiguration)` registers all CMS services and the five EF Core
DbContexts (PostgreSQL). `EnsureCMS()` runs migrations and seeding and wires up
the request pipeline. No further plumbing is required.

## 5. Configuration keys

`appsettings.json` (secrets belong in `dotnet user-secrets`, not source control):

```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=mysite;Username=mysite;Password=..." },
  "AdminUser": { "Email": "admin@example.com", "Password": "<strong-password>" },
  "CKEditor": { "LicenseKey": "" }
}
```

- `ConnectionStrings:DefaultConnection` — PostgreSQL connection (required).
- `AdminUser:Email` / `AdminUser:Password` — seeded on first run into the `Admin`
  role. Password must satisfy the Identity policy (≥12 chars, upper/lower/digit/symbol).
- `CKEditor:LicenseKey` — for the admin rich-text editor.

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

The admin UI (`/admin`), Identity pages, content-zone editors, and their CSS/JS
come from the package (served under `_content/WebWayCMS.Presentation/...`) and
need no host files.

## 7. Optional startup toggles (environment variables)

- `WEBWAYCMS_SKIP_MIGRATIONS=true` — skip applying EF migrations on startup.
- `WEBWAYCMS_SKIP_ROLESEED=true` — skip seeding roles + the admin user.
- `WEBWAYCMS_SKIP_DEFAULTPAGE=true` — skip seeding the default Home/Admin pages.

## 8. Dev-loop note when iterating on the CMS itself

NuGet caches a restored package by version. If you change CMS source and re-run
`dotnet pack` **without bumping the version**, consumers keep the cached copy.
While actively developing the CMS, either bump `<Version>` in
`Directory.Build.props` per pack, or clear the cached package:

```bash
dotnet nuget locals global-packages --clear   # or: rm -rf ~/.nuget/packages/webwaycms
```
