# Area 4: Content Zone Component Framework

**Namespaces:**
- `WebWayCMS.ContentZones` — `ContentZoneComponentRegistry`, `IContentZoneComponentRegistry`, `ContentZoneComponentInfo`
- `WebWayCMS.Attributes` — `[ContentZoneComponent]`, `ContentZoneComponentNaming`
- `WebWayCMS.Presentation.Components` — `ContentZone.razor` (rendering), `ContentZoneEditor.razor` (admin editing)
- `WebWayCMS.Presentation.Rendering` — `IContentZoneResolver`/`ContentZoneResolver`, `IContentZoneWidgetRegistry`/`ContentZoneWidgetRegistry`
- `WebWayCMS.Models.ContentZone` — `ContentZoneViewModel`, `ContentZoneObject`, `IContentZoneObject`, `ContentZoneUpsertViewModel`

**Depends on:** Data Tier (`IContentZoneService`), Form Generation Metadata (`FormPropertyBuilder`), Page Routing Subsystem (`PageDTO` from the `CmsRenderContext` cascade)
**Consumed by:** Admin CRUD Framework (the content-zone editor), any Razor component placing a `<ContentZone />`

---

## 1. System Overview

Content zones are named database-backed slots that appear in Razor components. Each zone holds an
ordered list of *widget instances* — rows in `ContentZoneItems` that reference a widget component by
name and store a JSON configuration blob.

Zones can be:
- **Page-scoped** — tied to a specific page via `ContentZoneAssignments (ParentPageMasterId, SlotName)`
- **Nested** — tied to a parent zone via `ContentZoneAssignments (ParentZoneId, SlotName)`
- **Global** — looked up by name only, shared across all pages

Widgets are **Blazor components** in `WebWayCMS.Presentation/Components/Widgets/` decorated with
`[ContentZoneComponent]`. Each receives its stored JSON configuration deserialized into a typed
object through its `[Parameter] Config` property.

---

## 2. `ContentZone.razor` — Parameters

`ContentZone` is the Blazor port of the former `ContentZoneViewComponent` (and its `Default.cshtml`).
Place it in any Razor component:

```razor
<ContentZone ZoneName="Main" />
<ContentZone ZoneName="Sidebar" IsGlobal="true" />
<ContentZone ZoneId="@someZoneId" />
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ZoneName` | `string?` | `null` | Named slot to render; required unless `ZoneId` is provided |
| `IsGlobal` | `bool` | `false` | When `true`, ignores the current page and resolves by name only |
| `ZoneId` | `Guid?` | `null` | Direct zone ID lookup, bypassing name/page resolution entirely |

Two cascading values feed it implicitly:
- `[CascadingParameter] CmsRenderContext? Context` — supplies the current `PageDTO` (established by
  `CmsPageHost`), replacing the old `HttpContext.Items["CMS:PageData"]` read inside the component.
- `[CascadingParameter(Name = "ParentZoneId")] Guid? ParentZoneId` — set by an enclosing zone so child
  zones resolve as nested slots (replacing the old `ViewData["ContentZone:ParentZoneId"]` side-channel).

`ContentZone` delegates the actual zone lookup to `IContentZoneResolver` and renders each resulting
item with `<DynamicComponent>`. An empty zone renders nothing. There is no `editMode` parameter — admin
inline editing is a separate component, `ContentZoneEditor.razor` (see §10).

---

## 3. Zone Resolution Algorithm

`ContentZone.razor` calls `IContentZoneResolver.ResolveAsync(zoneName, isGlobal, zoneId, page, parentZoneId)`.
`ContentZoneResolver` resolves zones in this order:

1. **Direct ID lookup** — if `zoneId` is provided, call `_model.GetViewModelByIdAsync(zoneId)` and skip all other steps
2. **Empty name guard** — if `zoneName` is blank (and no `zoneId`), return an empty view model
3. **Page-scoped zone** — if `IsGlobal = false` and a `PageDTO` is present:
   - **nested**, when `parentZoneId` is set (a parent zone is rendering) → `GetOrCreateViewModelByZoneSlotAsync(parentZoneId, zoneName)`
   - **top-level page slot**, otherwise → `GetOrCreateViewModelByPageSlotAsync(pageMasterId, zoneName)`
4. **Global zone** — otherwise, call `_model.GetOrCreateViewModelAsync(zoneName)`

If the resolved `ContentZoneViewModel` is `null`, an empty view model is returned (the zone exists
conceptually but has no DB record yet).

---

## 4. Lazy Zone Creation

Zones are created on demand. The first time a zone is resolved, `GetOrCreateViewModelByPageSlotAsync`
runs inside a database transaction:

1. Check if an assignment exists for `(pageMasterId, slotName)`
2. If yes, return the existing zone
3. If no, begin transaction → re-check (double-checked locking) → create `ContentZoneDTO` + `ContentZoneAssignmentDTO` atomically → commit

This means zones do not need to be seeded or pre-created. They appear in the database the first time a
page slot is rendered. The same pattern applies to global zones (`GetOrCreateViewModelAsync`) and
nested zones (`GetOrCreateViewModelByZoneSlotAsync`).

---

## 5. `ContentZoneComponentRegistry`

`ContentZoneComponentRegistry` is a **singleton** that supplies widget *metadata* (for the admin
component picker and config-form generation). It scans:
- `typeof(ContentZoneComponentRegistry).Assembly` — the CMS library
- `Assembly.GetEntryAssembly()` — the host Web project

**Any non-abstract class** carrying `[ContentZoneComponent]` is registered (no `ViewComponent` base
requirement — widgets are Blazor components). The component name is derived by
`ContentZoneComponentNaming.ResolveName`: the attribute's explicit `Name` if set, otherwise the class
name with a `"Widget"` or `"ViewComponent"` suffix stripped (e.g. `ContentBlockWidget` → `ContentBlock`,
`PageNavigationWidget` sets `Name = "Page"`).

**Interface:**
```csharp
IReadOnlyList<ContentZoneComponentInfo> GetAllComponents()
ContentZoneComponentInfo? GetByName(string componentName)
IReadOnlyList<string> GetCategories()
IReadOnlyList<ContentZoneComponentInfo> GetByCategory(string category)
IReadOnlyDictionary<string, IReadOnlyList<ContentZoneComponentInfo>> GetComponentsByCategory()
object? CreateDefaultConfiguration(string componentName)
IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration)
```

Components are sorted within each category by `Order` ascending, then `DisplayName` alphabetically.

`ValidateConfiguration` checks required fields, numeric range, max length, and regex pattern against the
resolved `FormPropertyInfo` list. It accepts either a typed config object or a JSON string.

> **Runtime dispatch** (name → widget `Type`) is a separate concern, handled by
> `IContentZoneWidgetRegistry` (see §7), not by this metadata registry.

---

## 6. `[ContentZoneComponent]` Attribute Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string?` | `null` | Stored component name; when unset, derived by stripping `Widget`/`ViewComponent` from the class name |
| `DisplayName` | `string` | Component name with spaces | Shown in the add-widget dropdown |
| `Description` | `string` | `""` | Help text |
| `Category` | `string` | `"General"` | Groups related widgets (e.g. "Content", "Layout", "Media") |
| `ConfigurationType` | `Type?` | `null` | Config class; properties become the widget's config form |
| `IconClass` | `string` | `""` | CSS icon class for admin UI display |
| `Order` | `int` | `0` | Sort order within category |

---

## 7. Render-time dispatch: `ContentZoneObject` + `IContentZoneWidgetRegistry`

`ContentZoneObject` is the render-time wrapper for one zone item, built from a `ContentZoneItemDTO` +
registry lookup:

```csharp
public class ContentZoneObject : IContentZoneObject
{
    public Guid Id { get; set; }                  // ContentZoneItemDTO.Id
    public int Ordinal { get; set; }
    public Guid ZoneId { get; set; }
    public string ComponentName { get; set; }     // e.g. "ContentBlock"
    public string ViewName { get; set; }          // optional host [ContentZoneView] selection
    public object ComponentProperties { get; set; } // deserialized config object
}
```

`ContentZone.razor` maps each item's `ComponentName` to a concrete component `Type` and renders it:

```razor
@foreach (var obj in _vm.ZoneObjects.OrderBy(o => o.Ordinal))
{
    var renderType = ResolveRenderType(obj);            // IContentZoneViewRegistry then IContentZoneWidgetRegistry
    <DynamicComponent Type="renderType" Parameters="BuildParameters(obj)" />  // Parameters: { ["Config"] = obj.ComponentProperties }
}
```

`IContentZoneWidgetRegistry.Resolve(componentName)` returns the widget `Type` (its name→type map is
built by `ContentZoneWidgetRegistry.FromAssemblies(...)`, scanning the CMS Presentation assembly + the
host entry assembly — registered in `WebWayCMS/ServiceCollectionExtensions.cs`). When the item carries
a `ViewName`, `IContentZoneViewRegistry.Resolve(componentName, viewName)` is consulted first so a host
`[ContentZoneView]` component can override the default widget while sharing the same `Config`. The
widget receives the deserialized configuration through a `[Parameter] Config` property typed to its
declared `ConfigurationType`.

---

## 8. Nested Zones

Zones can contain other zones: a widget simply places its own `<ContentZone ZoneName="Inner" />`. The
parent zone ID is threaded through a cascading value instead of `ViewData`:

1. After resolving its zone, `ContentZone.razor` cascades `ParentZoneId = vm.Id` (`IsFixed`) to its children
2. An inner `<ContentZone ZoneName="Inner" />` reads the cascaded `ParentZoneId`
3. The resolver detects `parentZoneId` and calls `GetOrCreateViewModelByZoneSlotAsync(parentZoneId, "Inner")`

An unsaved (empty) zone passes the inherited parent through unchanged. Nesting depth is unlimited, but
each level adds a database query — avoid deep nesting on performance-sensitive pages.

---

## 9. Component Configuration Contract

**Storage:** `ContentZoneItemDTO.ComponentPropertiesJson` — a JSON string written when the admin saves the widget's config form.

**Admin form generation:** `ContentZoneComponentRegistry.GetByName(componentName).Properties` — built by
`FormPropertyBuilder.BuildPropertyInfos(ConfigurationType)` at startup. `InteractiveFormFields.razor`
renders this list into the editor's config form.

**Runtime deserialization:** `ContentZoneModel` deserializes the JSON into the `ConfigurationType` when
building `ContentZoneViewModel`. The result is stored in `ContentZoneObject.ComponentProperties`.

**Widget receives:** the typed configuration object through its `Config` parameter:
```razor
@attribute [ContentZoneComponent(DisplayName = "My Widget", ConfigurationType = typeof(MyWidgetConfiguration))]

@* markup using Config *@

@code {
    [Parameter] public MyWidgetConfiguration? Config { get; set; }
}
```

If `ConfigurationType` is `null`, the widget can omit the `Config` parameter.

---

## 10. Inline Editing — `ContentZoneEditor.razor`

Admin inline editing is an **Interactive Server** Blazor component, `ContentZoneEditor.razor` (hosted at
`/admin/zone-editor/{ZoneId:guid}` by `AdminZoneEditorPage`). It replaces the former `Edit.cshtml` view,
`content-zone-edit.js`, and `ContentZoneApiController`: add / reorder / delete and the dynamic config
form (`InteractiveFormFields.razor`) are plain C# event handlers running in the circuit, and it uses the
component registry to populate its add-widget picker and `IContentZoneWidgetRegistry` to render live
previews.

---

*See also:* [docs/widget-system.md](../widget-system.md) for the step-by-step guide to creating a custom widget.
