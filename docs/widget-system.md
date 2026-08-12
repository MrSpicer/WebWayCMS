# Widget System (Content Zones)

Content Zones are named, database-backed regions in a view that an admin can populate with **widgets** at runtime through the CMS admin UI — no code deploys required.

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Placing a Zone in a View](#placing-a-zone-in-a-view)
- [How to Add a New Widget](#how-to-add-a-new-widget)

---

## System Overview

- A **Content Zone** is a named slot in a Razor view. Each zone stores an ordered list of widget instances in the database.
- The `ContentZone` view component renders all widgets assigned to a zone path. When an admin is viewing the page it also renders an inline "Add Widget" button and edit controls.
- **Widgets** are ViewComponent classes decorated with `[ContentZoneComponent]`. At startup the CMS reflects over the `WebWayCMS.Presentation` assembly and the entry assembly (`MySite`) and **seeds a row per widget** into the `WidgetRegistrations` table. From then on the runtime registry, `IWidgetRegistry`, serves widget metadata from the database — so widgets can be renamed, re-categorised, or deactivated from the admin UI at `/wadmin/widgets` with no code change.
- Each widget can declare a typed **configuration class**. Properties on that class decorated with `[FormProperty]` are rendered as form fields in the admin "Add Widget" modal.

---

## Core Components

| Class | File | Role |
|---|---|---|
| `ContentZoneViewComponent` | `WebWayCMS.Presentation/ViewComponents/ContentZoneViewComponent.cs` | Renders a zone by name; switches to edit view for admins |
| `IWidgetRegistry` / `WidgetRegistry` | `WebWayCMS.ContentZones/ContentZones/WidgetRegistry.cs` | Runtime widget metadata, loaded from the database with a 5-minute cache |
| `IWidgetRegistrationService` | `WebWayCMS.Data/Data/Services/WidgetRegistrationService.cs` | Queries the `WidgetRegistrations` table |
| `WidgetRegistrationModel` | `WebWayCMS.Core/Models/WidgetRegistration/WidgetRegistrationModel.cs` | The `widgets` admin content type |
| `[ContentZoneComponent]` | `WebWayCMS.Forms/Attributes/ContentZoneComponentAttribute.cs` | Marks a ViewComponent as a widget; read once at startup to seed its registration row |
| `[FormProperty]` / `EditorType` | `WebWayCMS.Forms/Attributes/FormPropertyAttribute.cs` | Drives config form field generation in the admin UI |

---

## Placing a Zone in a View

```cshtml
@* Page-scoped zone — unique per page *@
@await Component.InvokeAsync("ContentZone", new { zoneName = "Hero" })

@* Global zone — shared across all pages (nav, footer, etc.) *@
@await Component.InvokeAsync("ContentZone", new { zoneName = "Sidebar", IsGlobal = true })
```

- `zoneName` — slot name for the zone (e.g. `"Main"`, `"Sidebar"`); scoped to the current page or parent zone via a `ContentZoneAssignment` record.
- `IsGlobal = true` — bypasses the page context so one zone instance is shared across all pages.

In normal (non-admin) mode the component renders nothing if the zone has no items assigned.

---

## How to Add a New Widget

All files live in **`MySite`**. No changes to the CMS library are needed.

### Step 1 — (Optional) Create a configuration class

**`MySite/ViewComponents/MyWidgetConfiguration.cs`**

Properties decorated with `[FormProperty]` appear as form fields in the admin "Add Widget" modal. Omit this class entirely if the widget has no configuration.

```csharp
using WebWayCMS.Attributes;

namespace MySite.ViewComponents;

public class MyWidgetConfiguration
{
    [FormProperty(Label = "Heading", EditorType = EditorType.Text, Order = 1)]
    public string Heading { get; set; } = string.Empty;

    [FormProperty(Label = "Show Border", EditorType = EditorType.Checkbox, Order = 2)]
    public bool ShowBorder { get; set; }
}
```

Available `EditorType` values:

| Value | Editor rendered |
|---|---|
| `Text` | Single-line text input |
| `TextArea` | Multi-line textarea |
| `RichText` | Rich text / HTML editor |
| `Number` | Numeric input |
| `Checkbox` | Boolean checkbox |
| `Guid` | GUID input with optional entity picker |
| `Dropdown` | Select from predefined options |
| `Date` | Date picker |
| `DateTime` | Date + time picker |
| `Color` | Color picker |
| `Url` | URL input with validation |
| `Email` | Email input with validation |
| `ViewPicker` | Dropdown of available views for the component |
| `PageControllerPicker` | Dropdown populated client-side from the registered page types |
| `Hidden` | Hidden field (included in config, not shown) |

### Step 2 — Create the ViewComponent

**`MySite/ViewComponents/MyWidgetViewComponent.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;

namespace MySite.ViewComponents;

[ContentZoneComponent(
    DisplayName = "My Widget",
    Description = "Displays a custom widget.",
    Category = "General",
    ConfigurationType = typeof(MyWidgetConfiguration),
    IconClass = "fa-star",
    Order = 10
)]
public class MyWidgetViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(MyWidgetConfiguration config)
    {
        config ??= new MyWidgetConfiguration();
        return View(config);
    }
}
```

`[ContentZoneComponent]` properties:

| Property | Description |
|---|---|
| `DisplayName` | Label shown in the admin "Add Widget" dropdown |
| `Description` | Help text shown in the admin UI |
| `Category` | Groups widgets in the dropdown (e.g. `"General"`, `"Content"`, `"Navigation"`) |
| `ConfigurationType` | The config class from Step 1; omit if no config is needed |
| `IconClass` | Font Awesome class for the admin UI icon (e.g. `"fa-star"`) |
| `Order` | Sort order within the category; lower values appear first |

These values are the **seed defaults** written into the widget's registration row the first time the
CMS starts. After that, the row is authoritative — edit the widget at `/wadmin/widgets` rather than
changing the attribute.

### Step 3 — Create the Razor view

**`MySite/Views/Shared/Components/MyWidget/Default.cshtml`**

```cshtml
@model MySite.ViewComponents.MyWidgetConfiguration

<div class="my-widget @(Model.ShowBorder ? "bordered" : "")">
    <h2>@Model.Heading</h2>
</div>
```

Additional named views (e.g. `Compact.cshtml`) can be added in the same folder and selected via a `ViewPicker` config property.

### Step 4 — No registration required

At startup the CMS scans `Assembly.GetEntryAssembly()` (i.e. `MySite`) and seeds a
`WidgetRegistrations` row for your widget. No changes to `ServiceCollectionExtensions.cs` or
`Program.cs` are needed, and the widget appears in the "Add Widget" dropdown on the next request.

Two consequences worth knowing:

- **Seeding only inserts.** If you later change the attribute's `DisplayName`, `Category`, `Order`,
  or `ConfigurationType`, the stored row is *not* updated. Edit the widget at `/wadmin/widgets`, or
  delete its row and restart to re-seed. `WEBWAYCMS_SKIP_DEFAULTWIDGETS=true` suppresses seeding.
- **Widgets can be turned off without a deploy.** Clearing `IsActive` on the registration row
  removes the widget from the picker while leaving existing placements intact.

---

## How Zone Resolution Works

The `ContentZoneViewComponent` resolves zones via the `ContentZoneAssignments` table rather than path strings:

- **Page-scoped zones** look up by `(ParentPageMasterId, SlotName)` in `ContentZoneAssignments`. If no assignment exists and the user is an admin, a `ContentZoneDTO` + `ContentZoneAssignment` record are created automatically.
- **Nested zones** (zones rendered inside another zone's layout component) look up by `(ParentZoneId, SlotName)` using the parent zone's ID stored in render context.
- **Global zones** (`IsGlobal = true`) bypass assignment lookup and use name-based lookup on `ContentZoneDTO.Name`.

The `ContentZoneDTO.Name` field now stores a human-readable slot name (e.g. `"Main"`, `"Sidebar"`) rather than an opaque path. Zone identity is determined by the assignment record, not the name.

---

*For architectural reference — zone resolution algorithm, lazy zone creation, registry internals, nested zones, and component configuration contract — see [docs/architecture/04-content-zone-framework.md](architecture/04-content-zone-framework.md).*
