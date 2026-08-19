# Area 14: Host Extensibility (EF Tables, Migrations, Content Types)

**Namespaces:**
- `WebWayCMS` — `IWebWayCmsBuilder`, `WebWayCmsBuilder` (internal), `ServiceCollectionExtensions`
- `WebWayCMS.Data.DbContexts` — `ICmsModelExtension`, `AssemblyModelExtension`, `DelegateModelExtension`, `CmsDbContext`, `CmsExtensionDbContext<TSelf>`, `CmsModelCacheKeyFactory`, `ContentModelConfiguration`
- `WebWayCMS.Startup` — `CmsAssemblyCatalog`, `CmsMigrationsContextCatalog` (internal singletons)

**Consumed by:** a package host's `Program.cs`

---

## 1. The Problem

A host consuming WebWayCMS as a NuGet package could not define a content type with its own EF table.
It could only reuse an existing DTO's `ContentVersion.CustomFields` JSONB column — adding a real table
meant editing CMS source and re-running `./scripts/RebuildEFMigrations.sh`. The host-extensibility seam
closes that gap: a host declares a new EF-backed content type entirely in its own project, with zero
CMS source changes.

## 2. Two Independent Halves

The seam has two halves, chosen deliberately to keep the CMS's own migrations untouched:

- **Runtime model** — the host contributes `IEntityTypeConfiguration<T>` (or arbitrary
  `ICmsModelExtension` implementations) into `CmsDbContext`'s model via the builder callback.
- **Migrations** — the host owns a migrations-only `DbContext` subclassing `CmsExtensionDbContext<TSelf>`,
  which marks every CMS-owned and Identity table `ExcludeFromMigrations`. It gets its own history table
  and its own migrations in the host assembly.

## 3. The Builder

`AddWebWayCms`, `AddWebWayCmsAdmin`, and `AddWebWayCmsRendering` each have an overload taking
`Action<IWebWayCmsBuilder>? configure`. The callback runs **first**; its effects are recorded in plain
catalogs that the existing registration reads.

| Method | Effect |
|---|---|
| `AddApplicationAssembly(Assembly)` | one call → EF model scan **+** the four seeders **+** MVC `AssemblyPart`/`CompiledRazorAssemblyPart` |
| `AddModelConfiguration<TConfig>()` / `AddModelConfiguration(ICmsModelExtension)` / `ConfigureModel(Action<ModelBuilder>)` | explicit model contribution |
| `AddContentType<T>(string key)` | registers `IContentStore<T>` |
| `AddMappingProfile(Profile)` | contributes to the `IMapper` singleton |
| `AddMigrationsContext<TContext>(string historyTable)` | `AddDbContext<TContext>` over Npgsql with that history table, and enrolls it in the migration runner |
| `AddContentSeedFile(string path)` | registers a JSON content seed file applied at startup (admin mode only) |
| `AddContentSeedAssembly(Assembly)` | registers an assembly whose embedded `*.contentseed.json` resources are applied at startup (admin mode only) |
| `Services` | escape hatch |

`AddApplicationAssembly` and the three `AddModelConfiguration*` forms all register an
`ICmsModelExtension` singleton (the two shipped implementations are `AssemblyModelExtension` and
`DelegateModelExtension`). `CmsDbContext.OnModelCreating` applies the injected extension list **in
registration order** after the CMS's own `ApplyConfigurationsFromAssembly`. `CmsModelCacheKeyFactory`
folds the extension types into EF's model cache key, so two `CmsDbContext` instances with different
extension sets in one process don't share a stale model.

## 4. Target Host Experience

```csharp
// MySite/Data/FaqDTO.cs  +  FaqDTOEntityConfiguration.cs  (uses public ConfigureContentLink())

// MySite/Data/MySiteMigrationsDbContext.cs — migrations only, never injected
public sealed class MySiteMigrationsDbContext : CmsExtensionDbContext<MySiteMigrationsDbContext>
{
    public MySiteMigrationsDbContext(
        DbContextOptions<MySiteMigrationsDbContext> options,
        IEnumerable<ICmsModelExtension> modelExtensions)
        : base(options, modelExtensions) { }
}

// MySite/Program.cs
builder.Services.AddWebWayCms(builder.Configuration, cms =>
{
    cms.AddApplicationAssembly(typeof(FaqDTO).Assembly);   // EF configs + seeders + MVC parts
    cms.AddMappingProfile(new MySiteMappingProfile());
    cms.AddContentType<FaqDTO>("faqs");                    // registers IContentStore<FaqDTO>
    cms.AddMigrationsContext<MySiteMigrationsDbContext>("__EFMigrationsHistory_MySite");
});
builder.Services.AddScoped<FaqModel>();
builder.Services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<FaqModel>());
```

```bash
dotnet ef migrations add AddFaqs --context MySiteMigrationsDbContext
```

Because `CmsExtensionDbContext<TSelf>` inherits `CmsDbContext.OnModelCreating`, the host writes its
entity configuration once — it feeds both the runtime model and the migrations model.

## 5. Migration-Ordering Contract

`CmsMigrationRunner` migrates `CmsDbContext` **first**, then each context in `CmsMigrationsContextCatalog`
in registration order. CMS-first ordering is what makes a host table's FK to `ContentVersions`
resolvable. The host context's own history table keeps the CMS's `__EFMigrationsHistory` untouched, and
`RebuildEFMigrations.sh` keeps working because `CmsDbContextFactory` passes an empty extension list to
`CmsDbContext` at design time.

The FK from a host table to the excluded `ContentVersions` table (the `ConfigureContentLink` shared
PK/FK with `OnDelete(Cascade)`) **is** emitted by EF Core 10 + Npgsql in the host's migration, so
deleting a `ContentVersion` cascades to the host row.

## 6. What a Host Type Gets for Free

`AdminHandlerRegistry` consumes `IEnumerable<IAdminCrudHandler>`, so a host `AdminCrudModel<T>` is
registered and served by `AdminContentController` (`/wadmin/{contentType}`) with no controller code.
Because the MCP toolsets dispatch through the same registry, the host type is MCP-visible for free.
`ContentNode.ContentTypeKey` is a free-form string; the host picks a key that does not collide with the
built-ins (two handlers sharing a `ContentType` key throw at first registry resolution).

### Routable widgets

A host can also contribute **routable widgets**: `[ContentZoneComponent]` ViewComponents that
implement `IRoutableViewComponent` (see [widget-system.md](../widget-system.md)). The widget seeder
discovers them from the host assembly automatically; routing additionally needs a `Program.cs`
registration per widget:

```csharp
builder.Services.AddScoped<MyRoutableViewComponent>();
builder.Services.AddScoped<IRoutableViewComponent>(sp => sp.GetRequiredService<MyRoutableViewComponent>());
```

### Host-contributed form components

A host can also contribute **form components**: `[CMSFormComponent]` ViewComponents deriving
`FormFieldViewComponentBase`. Unlike routable widgets, these need **no** `Program.cs` registration —
`CmsFormComponentSeeder` scans the same assemblies passed to `AddApplicationAssembly(...)` and seeds a
`FormComponentRegistration` row automatically. A property selects the component by name:
`[FormProperty(FormComponent = "MyComponent")]` (no `EditorType` alias required). See
`WebWayCMS.TestHost.ViewComponents.Forms.FormStarRating` / `FormIconPicker` for worked examples.

A component used on a **JSON-bound** configuration form (a widget or page-type config, serialized via
`ConfigurationJson`) must emit exactly **one** element carrying `data-prop` — the client-side serializer
walks every `[data-prop]` element in the form and writes its `.value`, and the JSON deserializer on save
has no tolerance for malformed shapes: a throw there silently drops the *entire* saved configuration,
not just the offending field. A component that renders multiple inputs sharing one name (e.g. a radio
group) is therefore safe only on **model-bound** forms (a content type's own upsert form).

## 7. Known Limits (Out of Scope)

- The admin navbar is hardcoded (`WebWayCMS.Admin/Views/Shared/_AdminNavbar.cshtml`), so a host type
  has no menu entry — it is reachable by URL and over MCP only.
- No rollback/uninstall semantics for host migrations, and non-Npgsql providers are unsupported
  (`ContentVersionEntityConfiguration` uses raw Postgres partial-index filters).

> The `EntityPicker` endpoint map used to be a hardcoded-JS limit that prevented a host type from
> being an `EntityPicker` target. `form-components.js` now falls back to the generic
> `GET /wadmin/{contentType}/api/list` endpoint, so host content types work with no JS edit.
