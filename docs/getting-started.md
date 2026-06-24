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
  </ItemGroup>
</Project>
```

> The view layer is Blazor SSR, so there is no `.cshtml` runtime-compilation package to add — branding
> is supplied by Blazor components (see §6), picked up by the normal .NET hot-reload loop in Development.

## 4. Program.cs

```csharp
using WebWayCMS;
using WebWayCMS.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebWayCms(builder.Configuration);
builder.Host.UseCmsSerilog(builder.Configuration);

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

## 6. Branding and custom views (the per-site code)

The view layer is **Blazor SSR**. The CMS owns the public document shell
(`CmsLayout` — `<html>`/`<head>`/`<HeadOutlet>` and the `blazor.web.js` runtime
script), and a host brands and customizes the public site by dropping **Blazor
components marked with attributes** into the host project. They are discovered by
convention (the CMS scans the host's entry assembly at startup — the same pattern as
`[PageController]` / `[ContentZoneComponent]`); no registration call is required.

> The old `Views/Shared/_Layout.cshtml` + `Views/_ViewStart.cshtml` override
> mechanism is **gone** — there are no MVC views in the render path anymore. Create
> the components below instead.

Three extension points, all optional (the CMS works with none of them):

- **Site chrome** — header / navigation / footer wrapped around every public page.
  Inherit `CmsChromeBase` and mark the component `[CmsChrome]`; render your chrome
  around `@ChildContent` (the page body). Inject host `<head>` assets (stylesheets,
  fonts) with Blazor's `<HeadContent>`, which flows into the CMS document shell's
  `<HeadOutlet>`:

  ```razor
  @attribute [CmsChrome]
  @inherits CmsChromeBase

  <HeadContent><link rel="stylesheet" href="/css/site.css" /></HeadContent>
  <header>…your nav…</header>
  <main>@ChildContent</main>
  <footer>…</footer>
  ```

- **Page views** — an alternate body for a page type. Mark the component
  `[CmsPageView(ForController = "GenericPage", Name = "Wide")]` (where `ForController`
  is the page controller's class name without the `Controller` suffix — the built-in
  `GenericPageController` is `"GenericPage"`); it appears in the admin page editor's
  **View Name** dropdown and renders in place of the default content zone when
  selected. It can include its own `<ContentZone ZoneName="…" />`.

- **Content-zone widget views ("sub-views")** — an alternate rendering of an existing
  widget that shares the widget's configuration model. Mark the component
  `[ContentZoneView(ForComponent = "ContentBlock", Name = "Card")]` and accept the
  same `[Parameter] … Config`; it appears in the widget editor's **View** dropdown.

- **`wwwroot/`** — your CSS/JS, fonts, `favicon`, referenced from your `[CmsChrome]`
  component via `<HeadContent>`.

Working examples of all three live in `WebWayCMS.TestHost/Components/`. The admin UI
(`/admin`), Identity pages, content-zone editors, and their CSS/JS come from the
package (served under `_content/WebWayCMS.Presentation/...`) and need no host files.

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
