# CLAUDE.md

## Dev Server

- **URL:** `https://localhost:7046/`

## Commands

- **Build:** `dotnet build`
- **Run dev (hot reload + Sass watch):** `./Scripts/HotReloadRun.sh`
- **Rebuild Ef Migrations (destructive):** `./Scripts/RebuildEFMigrations.sh`
- **Docker build:** `./Scripts/DockerBuild.sh`
- **Run all tests + coverage gate:** `./Scripts/RunTests.sh` (or `dotnet test WebWayCMS.sln`)
- **Run one project's tests:** `dotnet test tests/WebWayCMS.Core.Tests/WebWayCMS.Core.Tests.csproj`
- **Run integration host end-to-end (dev secrets + docker compose; builds the libraries from source via project references, polls `http://localhost:45847`):** `./scripts/StartIntegrationHost.sh`
- **Tear down integration host (add `-v` to also drop the DB volume):** `./scripts/TearDownIntegrationhost.sh`

## Testing

- Test projects live under `tests/`, one per source project (NUnit + NSubstitute). Each references
  only its own source project and mocks cross-project dependencies, so it runs independently.
- Coverlet enforces **100% line + branch** coverage per project on every `dotnet test` run
  (configured in `tests/Directory.Build.props`); the build fails if a project drops below 100%.
- EF-backed data services are tested against the EF Core InMemory provider; everything else uses
  NSubstitute mocks. Blazor component markup is covered with the in-framework `HtmlRenderer`
  (`BlazorRenderHarness`) plus bUnit for interactive admin components. Coverage excludes generated
  EF migrations, the Blazor Identity components (`WebWayCMS.Presentation.Components.Account.*`, the
  parity replacement for the former scaffolded Identity Razor Pages), logicless Blazor host/shell
  components (marked `[ExcludeFromCodeCoverage]`), and the database/Identity-seeding + migration
  orchestration in `WebWayCMS/CMSExtensions.cs` (validated by running the app).

## View Layer

- The view layer is **Blazor SSR** — no MVC `.cshtml` views remain. Public pages render **Static
  SSR**; the admin UI (`/admin/*`) and the content-zone editor render **Interactive Server**;
  Identity flows are Blazor components at `/Account/*`.
- MVC routing is retained: the DB-driven page pipeline (`PageRouteTransformer` →
  `PageControllerRegistry` → `GenericPageController` / `GenericAdminPageController`) renders a Blazor
  root component via `ICmsPageRenderer` (`CmsPageHost` for public pages, `AdminPageHost` for admin
  pages) instead of returning a view.
- Content-zone widgets are Blazor components in `WebWayCMS.Presentation/Components/Widgets/`
  decorated with `[ContentZoneComponent]`. `ContentZoneComponentRegistry` scans them to populate the
  admin component picker; `IContentZoneWidgetRegistry` maps component names to widget types for
  `<DynamicComponent>` dispatch.
- The RichText editor wraps CKEditor 5 via JS interop (`wwwroot/js/richtext.js`). For the licensed
  cloud-CDN build, set the `CKEDITOR_LICENSE_KEY` environment variable (read via the
  `CKEditor:LicenseKey` config key); when empty it falls back to the GPL/esm.sh build. The license
  key must not be committed.

## rules
 - after finishing work check to see if documentation needs to be updated to reflect the changes
 - Do not Remove todo notes from the code unless the todo not has been completed. If you are unsure. ask
 - If Tests fail that were previously passing, do not modify those tests without permission from a human
 - When multiple good options exist ask the user which they would prefer
 - always ask clarifying questions when planning if you have any uncertainty.
 - Do not use JQuery.
 - Get confirmation from a human before using any external library or code.
 - Do not commit work
 - The very last thing you should do before existing work is reread the plan and ensure that all steps have been completed and all verification prescribed by the plan was actually done.

## Code Conventions

- File-scoped namespaces, nullable reference types enabled
- Private fields: `_camelCase`; async methods: suffix `Async`
- ViewModels: `{Name}ViewModel.cs`; DTOs: `{Name}DTO.cs` (in `Data/Models/`)
- Constructor injection with `?? throw new ArgumentNullException(nameof(...))`
- Fallible operations return `(bool Success, string? ErrorMessage)` tuples
- Async methods include `CancellationToken ct = default`
- Logging: `Serilog.Log.ForContext<ClassName>()`
- Controller routing: attribute-based with `[Authorize]`, `[ValidateAntiForgeryToken]`
- Test naming: `MethodName_Scenario_ExpectedBehavior`, NUnit constraint model (`Assert.That(...)`)
- Import order: System > Microsoft > Third-party > Project
- Configuration form fields use `[FormProperty]` attribute with `EditorType` enum

