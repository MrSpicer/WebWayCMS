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
    └── ContentZoneDTO

IContentService<T where T : class, IContent>
    └── ContentService<T>  (single generic implementation)

VersionedModel<TDto>  (abstract)
    └── AdminCrudModel<TDto>  (abstract, also implements IAdminCrudHandler)
            ├── ContentBlockModel
            ├── ArticleListModel
            ├── PageModel
            └── ContentZoneModel
        ArticleModel  (extends VersionedModel<ArticleDTO> directly — child resource, no standalone admin handler)

IAdminCrudHandler  (interface)
    └── implemented by each AdminCrudModel subclass
    └── resolved via AdminHandlerRegistry
    └── driven by the generic Blazor admin pages (AdminUpsert / list / version)
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

    public List<CustomField> CustomFields { get; set; } = new();
}
```

Each content type table uses a **shared primary key 1:1** relationship: its `ContentId` column is
both its PK and the FK into the single `Content` table, and `ContentId == ContentMeta.Id`. Shared
fields are read/written through `dto.ContentMeta.X` (e.g. `article.ContentMeta.Title`).

**Single shared table across contexts.** All content `DbContext`s map `ContentDTO` to one `Content`
table. Exactly one context (`ArticleContext`) *owns* it and emits its DDL; every other context maps
it with `ToTable("Content", t => t.ExcludeFromMigrations())` so it can declare the FK without
re-creating the table. Because of this, `ArticleContext` must migrate before the other content
contexts (see `CMSExtensions.ApplyCmsPendingMigrations`). The reusable
`ContentModelConfiguration.ConfigureContent(modelBuilder, ownsTable)` and
`entity.ConfigureContentLink()` helpers encapsulate this wiring.


## Built-in Content Types

| Content Type | ContentType | DTO | Model |
|---|---|---|---|
| Content Block | `contentblocks` | `ContentBlockDTO` | `ContentBlockModel` |
| Article List | `articlelists` | `ArticleListDTO` | `ArticleListModel` |
| Article (child) | child of `articlelists` | `ArticleDTO` | `ArticleModel` |
| Page | `pages` | `PageDTO` | `PageModel` |
| Content Zone | `contentzones` | `ContentZoneDTO` | `ContentZoneModel` |

### ContentBlock

Adds `string Content` (max 10,000 chars). Managed via a rich-text editor. Referenced elsewhere in views by MasterId.

### Article / ArticleList

`ArticleListDTO` is the parent container (its own versioned content type). `ArticleDTO` is a child and holds `ArticleListMasterId` as a FK. `ArticleListModel` exposes an inner `ArticleChildHandler` that implements `IAdminCrudChildHandler`.

### Page

Adds `string Route` (unique, must begin with `/`), `string ControllerName`, and `string ConfigurationJson` for per-page controller config. See `PageRouteTransformer` for how routes are resolved at request time.

### ContentZone

A named zone (`string Name`, `string Description`) that owns an ordered list of `ContentZoneItemDTO`. Each item stores `ComponentName` (a view component) and `ComponentPropertiesJson`. The `ContentZoneService` extends beyond `IContentService<T>` with zone-item management methods (`AddItemAsync`, `RemoveItemAsync`, `ReorderItemsAsync`).

---

## Adding a New Content Type

New content types belong in the **Web project** (`MySite`), not the CMS library. This keeps the CMS library stable while allowing the host application to define its own content.

Follow these steps to wire in a new content type that gets full versioning and admin CRUD for free.

### 1. Create the DTO

`MySite/Data/Models/MyContentDTO.cs`

```csharp
using WebWayCMS.Data.Models;

namespace MySite.Data.Models;

public record MyContentDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Body { get; set; } = string.Empty;
}
```

### 2. Create the DbContext

`MySite/Data/DbContexts/MyContentContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using MySite.Data.Models;
using WebWayCMS.Data.DbContexts; // ContentModelConfiguration helpers

namespace MySite.Data.DbContexts;

public class MyContentContext : DbContext
{
    public MyContentContext(DbContextOptions<MyContentContext> options) : base(options) { }

    public DbSet<MyContentDTO> MyContents { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // The shared "Content" table is owned/migrated by the CMS's ArticleContext, so map it here
        // with ownsTable: false (ExcludeFromMigrations) — this context only declares the FK into it.
        mb.ConfigureContent(ownsTable: false);

        mb.Entity<MyContentDTO>(e =>
        {
            e.ConfigureContentLink();           // shared-key 1:1 to ContentDTO + AutoInclude
            e.Property(e => e.Body).IsRequired();
            e.ToTable("MyContents");
        });
    }
}
```

> Shared fields (`Title`, `Slug`, `CustomFields`, versioning, …) live on the `Content` table, so
> configure only your type-specific columns here.

### 3. Create a migration

```bash
dotnet ef migrations add AddMyContent \
  -s MySite/MySite.csproj \
  -p MySite/MySite.csproj \
  -c MyContentContext \
  -o Migrations/MyContent
```

Then apply:

```bash
dotnet ef database update \
  -s MySite/MySite.csproj \
  -c MyContentContext
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

### 7. No admin views to author

The admin UI is **Blazor SSR**, so there are **no per-type Razor views**. The generic `AdminUpsert`
component renders the create/edit form directly from the `[FormProperty]` attributes on your ViewModel
(via `InteractiveFormFields`), driven by the `IAdminCrudHandler` you registered.

To expose your content type at admin routes, add small **routable Blazor wrapper pages** (mirroring the
built-ins `AdminContentBlocksPage` / `AdminContentBlockUpsertPage`):

```razor
@* MySite/Components/Admin/AdminMyContentsPage.razor *@
@page "/admin/mycontents"
@attribute [Authorize(Roles = "Admin")]
@* ...render the list from GetIndexViewModelAsync... *@
```

```razor
@* MySite/Components/Admin/AdminMyContentUpsertPage.razor *@
@page "/admin/mycontents/create"
@page "/admin/mycontents/edit/{Id:guid}"
@attribute [Authorize(Roles = "Admin")]
<AdminUpsert ContentType="mycontents" Id="Id" ListUrl="/admin/mycontents" />
@code { [Parameter] public Guid? Id { get; set; } }
```

See [architecture/06-admin-crud-framework §6](architecture/06-admin-crud-framework.md) for the route
conventions.

### 8. Register services

In `MySite/Program.cs`, before `builder.Services.AddWebWayCms(...)`:

```csharp
// DbContext
builder.Services.AddDbContext<MyContentContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_MyContent")));

// Generic content service
builder.Services.AddScoped<IContentService<MyContentDTO>>(sp =>
    new ContentService<MyContentDTO>(sp.GetRequiredService<MyContentContext>()));

// Model / handler
builder.Services.AddScoped<MyContentModel>();
builder.Services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<MyContentModel>());
```

`AdminHandlerRegistry` picks up any `IAdminCrudHandler` registered in DI regardless of which project it originates from. The generic Blazor admin components (`AdminUpsert` etc.) handle the create/edit/version flows for `mycontents` once you add its routable wrapper pages (Step 7) — no MVC controller code needed.

---

*For architectural reference — `ContentDTO`/`IContent` field semantics, versioning internals, DbContext catalog, service method reference, `AdminCrudModel<T>` dual-role pattern, and mapping conventions — see [docs/architecture/01-data-tier.md](architecture/01-data-tier.md) and [docs/architecture/05-content-domain-models.md](architecture/05-content-domain-models.md).*
