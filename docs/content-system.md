# Content System

The content system provides a generic, versioned approach to managing all CMS content types. Every content type **composes** a shared `ContentVersion` (the universal fields), is served by a single generic store (`IContentStore<T>`), and plugs into a unified admin CRUD framework.

> **Model change (Node/Version split):** the older `ContentDTO`/`IContent` (identity and version in one
> row) has been replaced by `ContentNode` (stable identity) + `ContentVersion` (per-version data).
> Content types implement `IVersionedContent`; `ContentService<T>`/`PageService` are gone, replaced by
> `IContentStore<T>`. The authoritative reference is [architecture/01-data-tier.md](architecture/01-data-tier.md).

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Built-in Content Types](#built-in-content-types)
- [Adding a New Content Type](#adding-a-new-content-type)

---

## Architecture Overview

```
ContentNode (stable identity) ── 1:N ── ContentVersion (per-version data)
    └── every cross-entity FK points at ContentNode.Id

IVersionedContent { Guid VersionId; ContentVersion Version; }  (1:1 shared PK/FK)
    ├── ContentBlockDTO
    ├── ArticleDTO
    ├── ArticleListDTO
    ├── PageDTO
    ├── ContentZoneDTO
    ├── ContentZoneItemDTO
    ├── WidgetRegistrationDTO
    └── PageControllerRegistrationDTO
    (CMSRouteDTO and FormComponentRegistrationDTO are NOT versioned — plain rows)

IContentStore<T where T : class, IVersionedContent>   (single generic engine)
    └── ContentStore<T>

VersionedModel<TDto>  (abstract)
    └── AdminCrudModel<TDto>  (abstract, also implements IAdminCrudHandler)
            ├── ContentBlockModel
            ├── ArticleListModel
            ├── PageModel
            ├── ContentZoneModel
            ├── WidgetRegistrationModel
            └── PageControllerRegistrationModel
        ArticleModel  (extends VersionedModel<ArticleDTO> directly — child resource, no standalone admin handler)

IAdminCrudHandler  (interface)
    ├── implemented by each AdminCrudModel subclass
    └── implemented directly by CMSRouteModel and FormComponentRegistrationModel (unversioned)
    └── resolved via AdminHandlerRegistry
    └── driven by AdminContentController (single controller, all content types)
```

**Top-level vs child model types:**
- **Top-level** types extend `AdminCrudModel<TDto>`. They get their own admin list/edit UI and are registered as `IAdminCrudHandler` so `AdminHandlerRegistry` picks them up automatically.
- **Child** types (like `ArticleModel`) extend `VersionedModel<TDto>` directly and are managed through a parent model's inner child handler (`IAdminCrudChildHandler`). They do not register as `IAdminCrudHandler` and have no standalone admin UI.

When adding a new standalone content type, extend `AdminCrudModel<TDto>`.

---

## Built-in Content Types

Eight top-level types are registered as `IAdminCrudHandler`s. The `ContentType` string is both the
admin URL segment (`/wadmin/{ContentType}`) and the key MCP tools use.

| Content Type | ContentType | DTO | Model |
|---|---|---|---|
| Page | `pages` | `PageDTO` | `PageModel` |
| Content Block | `contentblocks` | `ContentBlockDTO` | `ContentBlockModel` |
| Article List | `articles` | `ArticleListDTO` | `ArticleListModel` |
| Article (child) | child type `articles` under `articles` | `ArticleDTO` | `ArticleModel` |
| Content Zone | `contentzones` | `ContentZoneDTO` | `ContentZoneModel` |
| Zone Item (child) | child type `items` under `contentzones` | `ContentZoneItemDTO` | *(handled by `ContentZoneModel`)* |
| Widget Registration | `widgets` | `WidgetRegistrationDTO` | `WidgetRegistrationModel` |
| Page Type Registration | `pagetypes` | `PageControllerRegistrationDTO` | `PageControllerRegistrationModel` |
| CMS Route | `cmsroutes` | `CMSRouteDTO` | `CMSRouteModel` |
| Form Component Registration | `formcomponents` | `FormComponentRegistrationDTO` | `FormComponentRegistrationModel` |

### ContentBlock

Adds `string Content` (max 10,000 chars). Managed via a rich-text editor. Referenced elsewhere in views by NodeId.

### Article / ArticleList

`ArticleListDTO` is the parent container (its own versioned content type). `ArticleDTO` is a child and holds `ArticleListNodeId` as a FK, alongside `Body`, `AuthorName`, and `Summary`. `ArticleListModel` exposes an inner `ArticleChildHandler` that implements `IAdminCrudChildHandler`. Note the parent's `ContentType` is `articles`, so article URLs look like `/wadmin/articles/{listSlug}/articles`.

### Page

Adds `string? ViewName` (optional view override) and `string ConfigurationJson` (per-page controller config). A page has **no route or controller column** — its URL is a `CMSRouteDTO` row derived from `Version.Slug` plus `ContentNode.ParentNodeId`, written when the page is **published** (never saved). See [`docs/page-system.md`](page-system.md).

### ContentZone

A named zone (`string Name`, `string Description`) that owns an ordered list of `ContentZoneItemDTO`. Each item stores `ComponentName` (a view component) and `ComponentPropertiesJson`. The `ContentZoneService` extends beyond `IContentStore<T>` with zone-item management methods (`AddItemAsync`, `RemoveItemAsync`, `ReorderItemsAsync`) and assignment-based slot resolution.

### Registry types: Widget, Page Type, CMS Route

These three are infrastructure exposed as ordinary content types so admins can manage them:

- **`widgets`** — one row per available widget, seeded from `[ContentZoneComponent]` at startup and served at runtime by `IWidgetRegistry`. See [`docs/widget-system.md`](widget-system.md).
- **`pagetypes`** — one row per available page type, seeded from `[PageController]` and served by `IPageControllerRegistry`.
- **`cmsroutes`** — one row per URL. Written by the routing layer when pages and routable widgets are saved, and by the `[CmsRoute]` seeder. `CMSRouteModel` sets `SupportsVersionHistory => false`, because `ICMSRouteService.UpsertAsync` replaces rows rather than versioning them.

---

## Adding a New Content Type

The DTO, its EF configuration, the migrations-only `DbContext`, the model class, ViewModels,
mappings, and views for a new content type all live in the **host project** (`MySite`). No CMS
source change is required: the host contributes its own table and migration through the
`AddWebWayCms(config, cms => …)` builder callback (see [14-host-extensibility](architecture/14-host-extensibility.md)).

Follow these steps to wire in a new content type that gets full versioning and admin CRUD for free.

### 1. Create the DTO

`MySite/Data/MyContentDTO.cs`

```csharp
using WebWayCMS.Data.Models;

namespace MySite.Data;

public record MyContentDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string Body { get; set; } = string.Empty;
}
```

### 2. Add an entity configuration

The configuration uses the public `ConfigureContentLink()` helper, so the host's DTO gets the same
shared PK/FK into `ContentVersions` (with cascade delete) as the built-in types:

`MySite/Data/MyContentDTOEntityConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebWayCMS.Data.DbContexts;

namespace MySite.Data;

public sealed class MyContentDTOEntityConfiguration : IEntityTypeConfiguration<MyContentDTO>
{
    public void Configure(EntityTypeBuilder<MyContentDTO> entity)
    {
        entity.ConfigureContentLink();          // shared PK/FK into ContentVersions
        entity.Property(e => e.Body).IsRequired();
        entity.ToTable("MyContents");
    }
}
```

> Shared fields (`Title`, `Slug`, `CustomFields`, versioning, …) live on the `ContentVersion`, so
> configure only your type-specific columns here.

### 3. Add a migrations-only DbContext

The host owns its migrations via a context that subclasses `CmsExtensionDbContext<TSelf>`. It
inherits `CmsDbContext.OnModelCreating` (so the host's entity configuration feeds both the runtime
model and this migrations model) and then marks every CMS-owned and Identity table as excluded from
migrations — this context only ever emits the host's own tables:

`MySite/Data/MySiteMigrationsDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using WebWayCMS.Data.DbContexts;

namespace MySite.Data;

public sealed class MySiteMigrationsDbContext : CmsExtensionDbContext<MySiteMigrationsDbContext>
{
    public MySiteMigrationsDbContext(
        DbContextOptions<MySiteMigrationsDbContext> options,
        IEnumerable<ICmsModelExtension> modelExtensions)
        : base(options, modelExtensions) { }
}
```

### 4. Create a migration

```bash
dotnet ef migrations add AddMyContent --context MySiteMigrationsDbContext
```

The generated migration creates only `MyContents` (and its FK to `ContentVersions`); the CMS-owned
tables are excluded. `MySiteMigrationsDbContext` gets its own history table
(`__EFMigrationsHistory_MySite` — see step 9), leaving the CMS's `__EFMigrationsHistory` untouched.

### 5. Create ViewModels

`MySite/Models/MyContent/MyContentViewModel.cs`

```csharp
using WebWayCMS.Models;

namespace MySite.Models.MyContent;

public class MyContentViewModel : BaseContentViewModel
{
    public string Body { get; set; } = string.Empty;
}
```

`MySite/Models/MyContent/MyContentUpsertViewModel.cs`

```csharp
using WebWayCMS.Attributes;
using WebWayCMS.Models;

namespace MySite.Models.MyContent;

public class MyContentUpsertViewModel : BaseContentViewModel
{
    [FormProperty(EditorType.RichText)]
    public string Body { get; set; } = string.Empty;
}
```

### 6. Add mappings

In `MySite/MappingProfile.cs` (a class deriving `WebWayCMS.Mapping.Profile`), add inside the
constructor. Each `CreateMap` takes a converter lambda that builds the destination — fields you
don't set are simply omitted (there is no separate `Ignore`):

```csharp
// MyContent — read shared fields via Version, write them into a ContentVersion.
CreateMap<MyContentDTO, MyContentViewModel>(s => new MyContentViewModel
{
    NodeId = s.Version.Node.Id,
    Title = s.Version.Title,
    Body = s.Body,
    Slug = s.Version.Slug ?? string.Empty,
});
CreateMap<MyContentDTO, MyContentUpsertViewModel>(s => new MyContentUpsertViewModel
{
    NodeId = s.Version.Node.Id,
    ExpectedVersionNumber = s.Version.VersionNumber,
    Title = s.Version.Title,
    Body = s.Body ?? string.Empty,
    Slug = s.Version.Slug ?? string.Empty,
});
CreateMap<MyContentUpsertViewModel, MyContentDTO>(s =>
{
    var version = new ContentVersion
    {
        Node = new ContentNode { Id = s.NodeId ?? Guid.Empty, IsHidden = s.IsHidden },
        Title = s.Title ?? string.Empty,
        Slug = s.Slug ?? string.Empty,
        PublishStartUtc = s.PublicationDate,
        PublishEndUtc = s.PublicationEndDate
    };
    return new MyContentDTO
    {
        Version = version,
        Body = s.Body ?? string.Empty,
    };
});
```

> `ContentStore<T>.SaveDraftAsync` fills in the `Node.Id`/`VersionNumber`/`Slug` defaults on create —
> the mapper only needs to carry the editor-supplied fields.

### 7. Create the Model class

`MySite/Models/MyContent/MyContentModel.cs`

```csharp
using Microsoft.AspNetCore.Http;
using WebWayCMS.Mapping;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using MySite.Data.Models;

namespace MySite.Models.MyContent;

public sealed class MyContentModel : AdminCrudModel<MyContentDTO>
{
    private readonly IContentStore<MyContentDTO> _store;
    private readonly IMapper _mapper;

    protected override IContentStore<MyContentDTO> Store => _store;
    protected override string VersionHistoryContentType => "mycontents";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/mycontents";
    protected override Task<List<MyContentDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct)
        => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "mycontents";
    public override string DisplayName => "My Content";
    public override string IndexViewPath => "~/Views/AdminMyContent/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminMyContent/Upsert.cshtml";

    public MyContentModel(IContentStore<MyContentDTO> store, IMapper mapper, IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return dtos.Select(d => _mapper.Map<MyContentViewModel>(d)).ToList();
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new MyContentUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
        return dto == null ? null : _mapper.Map<MyContentUpsertViewModel>(dto);
    }

    public override object CreateEmptyUpsertViewModel() => new MyContentUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (MyContentUpsertViewModel)model;
        var dto = _mapper.Map<MyContentDTO>(vm);
        var result = await _store.SaveDraftAsync(dto, vm.ExpectedVersionNumber, ct);
        return result.Success
            ? new AdminSaveResult(true, NodeId: result.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage ?? "Save failed.");
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await _store.DeleteAsync(id, softDelete: false, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return dtos.Select(d => (object)new { id = d.Version.Node.Id, title = d.Version.Title });
    }
}
```

### 8. Create Razor views

`MySite/Views/AdminMyContent/Index.cshtml` — list all items using the standard admin table partial.

`MySite/Views/AdminMyContent/Upsert.cshtml` — the create/edit form. Use `@Html.EditorForModel()` or bind individual fields; the `[FormProperty]` attributes on the ViewModel drive dynamic form generation.

### 9. Register services

In `MySite/Program.cs`, wire everything through the builder callback:

```csharp
builder.Services.AddWebWayCms(builder.Configuration, cms =>
{
    cms.AddApplicationAssembly(typeof(MyContentDTO).Assembly);   // EF configs + seeders + MVC parts
    cms.AddMappingProfile(new MySiteMappingProfile());           // host mappings reach IMapper
    cms.AddContentType<MyContentDTO>("mycontents");              // registers IContentStore<MyContentDTO>
    cms.AddMigrationsContext<MySiteMigrationsDbContext>("__EFMigrationsHistory_MySite");
});

// Model / handler
builder.Services.AddScoped<MyContentModel>();
builder.Services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyContentModel>());
```

`AdminHandlerRegistry` picks up any `IAdminCrudHandler` registered in DI regardless of which project it originates from. `AdminContentController` handles all routes for `mycontents` with no additional controller code needed — and because the MCP toolsets dispatch through the same registry, the new type is also immediately available over MCP with no extra work.

> Registering an `IAdminCrudHandler` only has an effect in a host that called `AddWebWayCmsAdmin`
> (or its alias `AddWebWayCms`). A rendering-only host has no `AdminHandlerRegistry`.

---

*For architectural reference — `ContentNode`/`ContentVersion` field semantics, versioning internals, DbContext catalog, service method reference, `AdminCrudModel<T>` dual-role pattern, and mapping conventions — see [docs/architecture/01-data-tier.md](architecture/01-data-tier.md) and [docs/architecture/05-content-domain-models.md](architecture/05-content-domain-models.md).*
