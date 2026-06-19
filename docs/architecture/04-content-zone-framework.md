# Area 4: Content Zone Component Framework

**Namespaces:**
- `WebWayCMS.ContentZones` — `ContentZoneComponentRegistry`, `IContentZoneComponentRegistry`, `ContentZoneComponentInfo`
- `WebWayCMS.Attributes` — `[ContentZoneComponent]`
- `WebWayCMS.ViewComponents` — `ContentZoneViewComponent`
- `WebWayCMS.Models.ContentZone` — `ContentZoneViewModel`, `ContentZoneObject`, `IContentZoneObject`, `ContentZoneUpsertViewModel`

**Depends on:** Data Tier (`IContentZoneService`), Form Generation Metadata (`FormPropertyBuilder`), Page Routing Subsystem (`CMS:PageData` from `HttpContext`)
**Consumed by:** Admin CRUD Framework (zone controller + inline API), any Razor view invoking `ContentZone` component

---

## 1. System Overview

Content zones are named database-backed slots that appear in Razor views. Each zone holds an ordered list of *widget instances* — rows in `ContentZoneItems` that reference a ViewComponent by name and store a JSON configuration blob.

Zones can be:
- **Page-scoped** — tied to a specific page via `ContentZoneAssignments (ParentPageMasterId, SlotName)`
- **Nested** — tied to a parent zone via `ContentZoneAssignments (ParentZoneId, SlotName)`
- **Global** — looked up by name only, shared across all pages

Widgets are ViewComponents decorated with `[ContentZoneComponent]`. They receive their stored JSON configuration deserialized into a typed object.

---

## 2. `ContentZoneViewComponent.InvokeAsync` — Parameters

```csharp
await Component.InvokeAsync("ContentZone", new
{
    zoneName = "Main",    // slot name
    IsGlobal = false,     // bypass page/zone context
    editMode = false,     // force edit UI (admin inline editing)
    zoneId = (Guid?)null  // skip name/page resolution; fetch by ID
})
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `zoneName` | `string?` | `null` | Named slot to render; required unless `zoneId` is provided |
| `IsGlobal` | `bool` | `false` | When `true`, ignores `CMS:PageData` and resolves by name only |
| `editMode` | `bool` | `false` | When `true`, renders the edit UI with add/remove/reorder controls |
| `zoneId` | `Guid?` | `null` | Direct zone ID lookup, bypassing name/page resolution entirely |

**Read mode** (default): Renders each widget ViewComponent in order. Returns `Content(string.Empty)` for empty zones.

**Edit mode**: Renders the `Edit` view instead of the default view. Populates `ViewData["ComponentsByCategory"]` with the registry's component list for the add-widget dropdown.

---

## 3. Zone Resolution Algorithm

`ContentZoneViewComponent.InvokeAsync` resolves zones in this order:

1. **Direct ID lookup** — if `zoneId` is provided, call `_model.GetViewModelByIdAsync(zoneId)` and skip all other steps
2. **Nested zone** — if `ViewData["ContentZone:ParentZoneId"]` is set (a parent zone is rendering), call `_model.GetOrCreateViewModelByZoneSlotAsync(parentZoneId, zoneName)`
3. **Page-scoped zone** — if `HttpContext.Items["CMS:PageData"]` is a `PageDTO` and `IsGlobal = false`, call `_model.GetOrCreateViewModelByPageSlotAsync(pageMasterId, zoneName)`
4. **Global zone** — otherwise, call `_model.GetOrCreateViewModelAsync(zoneName)`

If the resolved `ContentZoneViewModel` is `null`, an empty view model is constructed (zone exists conceptually but has no DB record yet).

---

## 4. Lazy Zone Creation

Zones are created on demand. The first time a page is rendered in admin edit mode, `GetOrCreateByPageSlotAsync` runs inside a database transaction:

1. Check if an assignment exists for `(pageMasterId, slotName)`
2. If yes, return the existing zone
3. If no, begin transaction → re-check (double-checked locking) → create `ContentZoneDTO` + `ContentZoneAssignmentDTO` atomically → commit

This means zones do not need to be seeded or pre-created. They appear in the database only when an admin first visits a page in edit mode. The same pattern applies to global zones (`GetOrCreateByNameAsync`) and nested zones (`GetOrCreateByZoneSlotAsync`).

---

## 5. `ContentZoneComponentRegistry`

`ContentZoneComponentRegistry` is a **singleton**. It scans:
- `typeof(ContentZoneComponentRegistry).Assembly` — the CMS library
- `Assembly.GetEntryAssembly()` — the host Web project

Any non-abstract class inheriting from `ViewComponent` with `[ContentZoneComponent]` is registered. The component name is derived by stripping the `"ViewComponent"` suffix from the class name.

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

`ValidateConfiguration` checks required fields, numeric range, max length, and regex pattern against the resolved `FormPropertyInfo` list. It accepts either a typed config object or a JSON string.

---

## 6. `[ContentZoneComponent]` Attribute Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Component name with spaces | Shown in the add-widget dropdown |
| `Description` | `string` | `""` | Help text |
| `Category` | `string` | `"General"` | Groups related widgets (e.g. "Content", "Layout", "Media") |
| `ConfigurationType` | `Type?` | `null` | Config class; properties become the widget's config form |
| `IconClass` | `string` | `""` | CSS icon class for admin UI display |
| `Order` | `int` | `0` | Sort order within category |

---

## 7. `ContentZoneObject` / `IContentZoneObject`

`ContentZoneObject` is the render-time wrapper passed to each widget invocation. It is built from a `ContentZoneItemDTO` + registry lookup:

```csharp
public class ContentZoneObject : IContentZoneObject
{
    public Guid Id { get; set; }              // ContentZoneItemDTO.Id
    public string ComponentName { get; set; } // e.g. "ContentBlock"
    public string ComponentPropertiesJson { get; set; }  // raw JSON stored in DB
    public object? Configuration { get; set; }  // deserialized config object
    public int Ordinal { get; set; }
    public bool IsActive { get; set; }
}
```

The `ContentZoneViewComponent` renders each item by invoking:
```razor
@await Component.InvokeAsync(item.ComponentName, new { configuration = item.Configuration })
```

Widget ViewComponents receive the deserialized configuration as a parameter named `configuration` with the type they declared in `ConfigurationType`.

---

## 8. Nested Zones

Zones can contain other zones by having a widget render its own `ContentZone` component invocations. The parent zone ID is threaded through `ViewData`:

1. `ContentZoneViewComponent` stores `ViewData["ContentZone:ParentZoneId"] = vm.Id` after resolving the zone
2. When a widget renders `@await Component.InvokeAsync("ContentZone", new { zoneName = "Inner" })`, the `ViewData` is in scope
3. The inner invocation detects `ContentZone:ParentZoneId` and calls `GetOrCreateViewModelByZoneSlotAsync(parentZoneId, "Inner")`

Nesting depth is unlimited, but each level adds a database query. Avoid deep nesting for performance-sensitive pages.

---

## 9. Component Configuration Contract

**Storage:** `ContentZoneItemDTO.ComponentPropertiesJson` — a JSON string written when the admin saves the widget's config form.

**Admin form generation:** `ContentZoneComponentRegistry.GetByName(componentName).Properties` — built by `FormPropertyBuilder.BuildPropertyInfos(ConfigurationType)` at startup. The `FormFieldsTagHelper` renders this into an HTML form.

**Runtime deserialization:** `ContentZoneModel` deserializes the JSON into the `ConfigurationType` when building `ContentZoneViewModel`. The result is stored in `ContentZoneObject.Configuration`.

**Widget receives:** The typed configuration object as a parameter to `InvokeAsync`:
```csharp
public class MyWidgetViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(MyWidgetConfiguration? configuration)
    {
        configuration ??= new MyWidgetConfiguration();
        return View(configuration);
    }
}
```

If `ConfigurationType` is `null`, the widget receives no configuration parameter.

---

*See also:* [docs/widget-system.md](../widget-system.md) for the step-by-step guide to creating a custom widget.
