# Content System

The content system provides a generic, versioned approach to managing all CMS content types. Every content type **composes** a shared `ContentDTO` (the universal fields), is served by a single generic service, and plugs into a unified admin CRUD framework.

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [ContentDTO and IContent](#contentdto-and-icontent)
- [Built-in Content Types](#built-in-content-types)
- [Adding a New Content Type](#adding-a-new-content-type)

---

## Architecture Overview

```
ContentDTO (shared fields, own "Content" table)
    ▲ 1:1 (ContentId shared primary key / FK)
    │
IContent { Guid ContentId; ContentDTO ContentMeta; }
    ├── ContentBlockDTO
    ├── ArticleDTO
    ├── ArticleListDTO
    ├── PageDTO
    ├── ContentZoneDTO
    ├── ContentZoneItemDTO
    ├── CMSRouteDTO
    ├── WidgetRegistrationDTO
    └── PageControllerRegistrationDTO

IContentService<T where T : class, IContent>
    └── ContentService<T>  (single generic implementation)

VersionedModel<TDto>  (abstract)
    └── AdminCrudModel<TDto>  (abstract, also implements IAdminCrudHandler)
            ├── ContentBlockModel
            ├── ArticleListModel
            ├── PageModel
            ├── ContentZoneModel
            ├── WidgetRegistrationModel
            ├── PageControllerRegistrationModel
            └── CMSRouteModel
        ArticleModel  (extends VersionedModel<ArticleDTO> directly — child resource, no standalone admin handler)

IAdminCrudHandler  (interface)
    └── implemented by each AdminCrudModel subclass
    └── resolved via AdminHandlerRegistry
    └── driven by AdminContentController (single controller, all content types)
```

**Top-level vs child model types:**
- **Top-level** types extend `AdminCrudModel<TDto>`. They get their own admin list/edit UI and are registered as `IAdminCrudHandler` so `AdminHandlerRegistry` picks them up automatically.
- **Child** types (like `ArticleModel`) extend `VersionedModel<TDto>` directly and are managed through a parent model's inner child handler (`IAdminCrudChildHandler`). They do not register as `IAdminCrudHandler` and have no standalone admin UI.

When adding a new standalone content type, extend `AdminCrudModel<TDto>`.

---

## ContentDTO and IContent

**Files:** `WebWayCMS.Data/Data/Models/ContentDTO.cs`, `WebWayCMS.Data/Data/Models/IContent.cs`

The shared fields live in their own concrete record, `ContentDTO`, persisted to a single shared
`Content` table. Content types do **not** inherit it — they **compose** it via the `IContent`
interface (has-a, not is-a):

```csharp
public interface IContent
{
    Guid ContentId { get; set; }       // shared primary key / FK to Content
    ContentDTO ContentMeta { get; set; }
}

public record ContentDTO
{
    public Guid Id { get; set; }          // Primary key; new Guid per version
    public Guid MasterId { get; set; }    // Constant across all versions of one item
    public int Version { get; set; }      // Monotonically increasing; 0 on first save

    public string Slug { get; set; }      // URL segment; auto-derived from Title if blank
    public string Title { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid LastModifiedBy { get; set; }
    public DateTime CreationDate { get; set; }
    public DateTime ModificationDate { get; set; }

    public DateTime PublicationDate { get; set; }
    public DateTime? PublicationEndDate { get; set; }

    public bool IsPublished { get; set; }
    public bool IsArchived { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }

    public Guid? ParentMasterId { get; set; }   // FK to a parent's MasterId (child resources)

    public List<CustomField> CustomFields { get; set; } = new();
}
```

Each content type table uses a **shared primary key 1:1** relationship: its `ContentId` column is
both its PK and the FK into the single `Content` table, and `ContentId == ContentMeta.Id`. Shared
fields are read/written through `dto.ContentMeta.X` (e.g. `article.ContentMeta.Title`).

**Single shared table in the unified context.** All content types map to the same `Content` table
via the shared `CmsDbContext`. The context declares no `DbSet`s: it calls
`ApplyConfigurationsFromAssembly`, and each entity has a sealed `IEntityTypeConfiguration<T>` class
in `WebWayCMS.Data/Data/EntityConfiguration/`. Those configurations call the shared
`entity.ConfigureContentLink()` helper, which wires the 1:1 shared-primary-key relationship.


## Built-in Content Types

Seven top-level types are registered as `IAdminCrudHandler`s. The `ContentType` string is both the
admin URL segment (`/admin/{ContentType}`) and the key MCP tools use.

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

### ContentBlock

Adds `string Content` (max 10,000 chars). Managed via a rich-text editor. Referenced elsewhere in views by MasterId.

### Article / ArticleList

`ArticleListDTO` is the parent container (its own versioned content type). `ArticleDTO` is a child and holds `ArticleListMasterId` as a FK, alongside `Body`, `AuthorName`, and `Summary`. `ArticleListModel` exposes an inner `ArticleChildHandler` that implements `IAdminCrudChildHandler`. Note the parent's `ContentType` is `articles`, so article URLs look like `/admin/articles/{listSlug}/articles`.

### Page

Adds `string? ViewName` (optional view override) and `string ConfigurationJson` (per-page controller config). A page has **no route or controller column** — its URL is a `CMSRouteDTO` row derived from `ContentMeta.Slug` when the page is saved. See [`docs/page-system.md`](page-system.md).

### ContentZone

A named zone (`string Name`, `string Description`) that owns an ordered list of `ContentZoneItemDTO`. Each item stores `ComponentName` (a view component) and `ComponentPropertiesJson`. The `ContentZoneService` extends beyond `IContentService<T>` with zone-item management methods (`AddItemAsync`, `RemoveItemAsync`, `ReorderItemsAsync`) and assignment-based slot resolution.

### Registry types: Widget, Page Type, CMS Route

These three are infrastructure exposed as ordinary content types so admins can manage them:

- **`widgets`** — one row per available widget, seeded from `[ContentZoneComponent]` at startup and served at runtime by `IWidgetRegistry`. See [`docs/widget-system.md`](widget-system.md).
- **`pagetypes`** — one row per available page type, seeded from `[PageController]` and served by `IPageControllerRegistry`.
- **`cmsroutes`** — one row per URL. Written by the routing layer when pages and routable widgets are saved, and by the `[CmsRoute]` seeder. `CMSRouteModel` sets `SupportsVersionHistory => false`, because `ICMSRouteService.UpsertAsync` replaces rows rather than versioning them.

---

## Adding a New Content Type

The model class, ViewModels, mappings, and views for a new content type all live in the **Web
project** (`MySite`). The DTO and its EF configuration are the exception — see the note in step 2.

Follow these steps to wire in a new content type that gets full versioning and admin CRUD for free.

### 1. Create the DTO

`WebWayCMS.Data/Data/Models/MyContentDTO.cs`

```csharp
namespace WebWayCMS.Data.Models;

public record MyContentDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Body { get; set; } = string.Empty;
}
```

### 2. Add an entity configuration

`CmsDbContext` declares no `DbSet`s. Instead it calls
`ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`, so you add a configuration
class and nothing else:

`WebWayCMS.Data/Data/EntityConfiguration/MyContentDTOEntityConfiguration.cs`

```csharp
public sealed class MyContentDTOEntityConfiguration : IEntityTypeConfiguration<MyContentDTO>
{
    public void Configure(EntityTypeBuilder<MyContentDTO> entity)
    {
        entity.ConfigureContentLink();          // shared PK/FK into the Content table
        entity.Property(e => e.Body).IsRequired();
        entity.ToTable("MyContents");
    }
}
```

> Shared fields (`Title`, `Slug`, `CustomFields`, versioning, …) live on the `Content` table, so
> configure only your type-specific columns here.

> **Why this part cannot live in the host.** `ApplyConfigurationsFromAssembly` scans only
> `WebWayCMS.Data`, so a configuration class in `MySite` is never discovered and the table is never
> created. A content type that needs its own table must have its DTO and configuration in
> `WebWayCMS.Data`. If you only need a few extra fields on an existing type, use
> `ContentDTO.CustomFields` (JSONB) instead — no schema change, no CMS-library edit.

### 3. Create a migration

```bash
dotnet ef migrations add AddMyContent \
  --project WebWayCMS.Data --startup-project WebWayCMS.Data \
  --context CmsDbContext --output-dir Migrations
```

### 4. Create ViewModels

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

### 5. Add mappings

In `MySite/MappingProfile.cs`, add inside the constructor. Each `CreateMap` takes a converter lambda
that builds the destination — fields you don't set are simply omitted (there is no separate `Ignore`):

```csharp
// MyContent — read shared fields via ContentMeta, write them into a ContentDTO.
CreateMap<MyContentDTO, MyContentViewModel>(s => new MyContentViewModel
{
    Id = s.ContentMeta.Id,
    Title = s.ContentMeta.Title,
    Body = s.Body,
    Slug = s.ContentMeta.Slug ?? string.Empty,
});
CreateMap<MyContentDTO, MyContentUpsertViewModel>(s => new MyContentUpsertViewModel
{
    Id = s.ContentMeta.Id,
    Title = s.ContentMeta.Title,
    Body = s.Body ?? string.Empty,
    Slug = s.ContentMeta.Slug ?? string.Empty,
});
CreateMap<MyContentUpsertViewModel, MyContentDTO>(s =>
{
    var id = s.Id is { } existing && existing != Guid.Empty ? existing : Guid.NewGuid();
    return new MyContentDTO
    {
        ContentId = id,
        Body = s.Body ?? string.Empty,
        ContentMeta = new ContentDTO
        {
            Id = id,
            Title = s.Title ?? string.Empty,
            Slug = string.IsNullOrWhiteSpace(s.Slug) ? Uri.EscapeDataString(s.Title ?? string.Empty) : s.Slug,
        }
    };
});
```

> Keep `ContentId` and `ContentMeta.Id` equal when constructing a DTO; the services keep them in
> sync on create/update.

### 6. Create the Model class

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
    private readonly IContentService<MyContentDTO> _service;
    private readonly IMapper _mapper;

    protected override string VersionHistoryContentType => "mycontents";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/mycontents";
    protected override Task<List<MyContentDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _service.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "mycontents";
    public override string DisplayName => "My Content";
    public override string IndexViewPath => "~/Views/AdminMyContent/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminMyContent/Upsert.cshtml";

    public MyContentModel(IContentService<MyContentDTO> service, IMapper mapper)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return dtos.Select(d => _mapper.Map<MyContentViewModel>(d)).ToList();
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new MyContentUpsertViewModel();

        var dto = await _service.GetByIdAsync(id.Value, ct);
        return dto == null ? null : _mapper.Map<MyContentUpsertViewModel>(dto);
    }

    public override object CreateEmptyUpsertViewModel() => new MyContentUpsertViewModel();

    public override async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (MyContentUpsertViewModel)model;
        var dto = _mapper.Map<MyContentDTO>(vm);
        var ok = await _service.UpsertAsync(dto, ct);
        return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Save failed.");
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await _service.DeleteAsync(id, softDelete: false, deleteHistory: true, ct: ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return dtos.Select(d => (object)new { id = d.ContentMeta.Id, title = d.ContentMeta.Title });
    }
}
```

### 7. Create Razor views

`MySite/Views/AdminMyContent/Index.cshtml` — list all items using the standard admin table partial.

`MySite/Views/AdminMyContent/Upsert.cshtml` — the create/edit form. Use `@Html.EditorForModel()` or bind individual fields; the `[FormProperty]` attributes on the ViewModel drive dynamic form generation.

### 8. Register services

In `MySite/Program.cs`, before `builder.Services.AddWebWayCms(...)`:

```csharp
// Generic content service — reuses the unified CmsDbContext
builder.Services.AddScoped<IContentService<MyContentDTO>>(sp =>
    new ContentService<MyContentDTO>(sp.GetRequiredService<CmsDbContext>()));

// Model / handler
builder.Services.AddScoped<MyContentModel>();
builder.Services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyContentModel>());
```

`AdminHandlerRegistry` picks up any `IAdminCrudHandler` registered in DI regardless of which project it originates from. `AdminContentController` handles all routes for `mycontents` with no additional controller code needed — and because the MCP toolsets dispatch through the same registry, the new type is also immediately available over MCP with no extra work.

> Registering an `IAdminCrudHandler` only has an effect in a host that called `AddWebWayCmsAdmin`
> (or its alias `AddWebWayCms`). A rendering-only host has no `AdminHandlerRegistry`.

---

*For architectural reference — `ContentDTO`/`IContent` field semantics, versioning internals, DbContext catalog, service method reference, `AdminCrudModel<T>` dual-role pattern, and mapping conventions — see [docs/architecture/01-data-tier.md](architecture/01-data-tier.md) and [docs/architecture/05-content-domain-models.md](architecture/05-content-domain-models.md).*
