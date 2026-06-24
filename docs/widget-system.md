# Widget System (Content Zones)

Content Zones are named, database-backed regions in a Razor component that an admin can populate with **widgets** at runtime through the CMS admin UI — no code deploys required.

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Placing a Zone in a Component](#placing-a-zone-in-a-component)
- [How to Add a New Widget](#how-to-add-a-new-widget)

---

## System Overview

- A **Content Zone** is a named slot, placed with the `<ContentZone />` Blazor component. Each zone stores an ordered list of widget instances in the database.
- `ContentZone.razor` renders all widgets assigned to a zone, dispatching each to its Razor component with `<DynamicComponent>`. Editing happens separately in the admin content-zone editor (`ContentZoneEditor.razor`), not inline in the public page.
- **Widgets** are **Blazor components** in `Components/Widgets/` decorated with `[ContentZoneComponent]`. They are discovered automatically at startup — the registries scan both the CMS Presentation assembly and the entry assembly (`MySite`), so host-provided widgets work with no registration.
- Each widget can declare a typed **configuration class**. Properties on that class decorated with `[FormProperty]` are rendered as form fields in the admin widget editor.

---

## Core Components

| Class | File | Role |
|---|---|---|
| `ContentZone` | `WebWayCMS.Presentation/Components/ContentZone.razor` | Renders a zone by name; dispatches each item to its widget via `<DynamicComponent>` |
| `ContentZoneEditor` | `WebWayCMS.Presentation/Components/Admin/ContentZoneEditor.razor` | Interactive Server admin editor (add/reorder/delete + config form) |
| `ContentZoneComponentRegistry` | `WebWayCMS.ContentZones/ContentZones/ContentZoneComponentRegistry.cs` | Scans assemblies and caches widget **metadata** (admin picker, config-form generation) |
| `IContentZoneWidgetRegistry` | `WebWayCMS.Presentation/Components/Rendering/ContentZoneWidgetRegistry.cs` | Runtime **name → component `Type`** dispatch map, scanned from CMS + entry assemblies |
| `[ContentZoneComponent]` | `WebWayCMS.Forms/Attributes/ContentZoneComponentAttribute.cs` | Marks a Blazor component as a widget available in the admin UI |
| `[FormProperty]` / `EditorType` | `WebWayCMS.Forms/Attributes/FormPropertyAttribute.cs` | Drives config form field generation in the admin UI |

---

## Placing a Zone in a Component

```razor
@* Page-scoped zone — unique per page *@
<ContentZone ZoneName="Hero" />

@* Global zone — shared across all pages (nav, footer, etc.) *@
<ContentZone ZoneName="Sidebar" IsGlobal="true" />
```

- `ZoneName` — slot name for the zone (e.g. `"Main"`, `"Sidebar"`); scoped to the current page or parent zone (via the cascaded `CmsRenderContext` / `ParentZoneId`) through a `ContentZoneAssignment` record.
- `IsGlobal="true"` — bypasses the page context so one zone instance is shared across all pages.

`ContentZone` renders nothing when the zone has no items assigned.

---

## How to Add a New Widget

All files live in **`MySite`**. No changes to the CMS library are needed.

### Step 1 — (Optional) Create a configuration class

**`MySite/Components/Widgets/MyWidgetConfiguration.cs`**

Properties decorated with `[FormProperty]` appear as form fields in the admin widget editor. Omit this class entirely if the widget has no configuration.

```csharp
using WebWayCMS.Attributes;

namespace MySite.Components.Widgets;

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
| `RichText` | Rich text editor (CKEditor 5) |
| `Number` | Numeric input |
| `Checkbox` | Boolean checkbox |
| `Guid` | GUID input with optional entity picker |
| `Dropdown` | Select from predefined options |
| `Date` | Date picker |
| `DateTime` | Date + time picker |
| `Color` | Color picker |
| `Url` | URL input with validation |
| `Email` | Email input with validation |
| `ViewPicker` | Dropdown of picker options (used by the Layout widget for its templates) |
| `Hidden` | Hidden field (included in config, not shown) |

### Step 2 — Create the widget component

**`MySite/Components/Widgets/MyWidget.razor`**

```razor
@attribute [ContentZoneComponent(DisplayName = "My Widget", Description = "Displays a custom widget.", Category = "General", ConfigurationType = typeof(MyWidgetConfiguration), IconClass = "fa-star", Order = 10)]
@using MySite.Components.Widgets

<div class="my-widget @(Config?.ShowBorder == true ? "bordered" : "")">
    <h2>@Config?.Heading</h2>
</div>

@code {
    [Parameter] public MyWidgetConfiguration? Config { get; set; }
}
```

The widget component **is** the view — there is no separate `.cshtml`. It receives the deserialized
configuration through its `[Parameter] Config` (typed to `ConfigurationType`).

`[ContentZoneComponent]` properties:

| Property | Description |
|---|---|
| `Name` | Stored component name; defaults to the class name minus a `Widget`/`ViewComponent` suffix (e.g. `MyWidget` → `MyWidget`) |
| `DisplayName` | Label shown in the admin widget picker |
| `Description` | Help text shown in the admin UI |
| `Category` | Groups widgets in the picker (e.g. `"General"`, `"Content"`, `"Navigation"`) |
| `ConfigurationType` | The config class from Step 1; omit if no config is needed |
| `IconClass` | Font Awesome class for the admin UI icon (e.g. `"fa-star"`) |
| `Order` | Sort order within the category; lower values appear first |

### Step 3 — (Optional) Alternate renderings ("sub-views")

A widget needs no second component to work. If you want an *alternate* rendering of an existing widget
that shares its `Config`, add a `[ContentZoneView(ForComponent = "MyWidget", Name = "Compact")]`
component accepting the same `[Parameter] Config`. It appears in the widget editor's **View** dropdown
and is dispatched in place of the default widget when selected (persisted as `ContentZoneItemDTO.ViewName`).

### Step 4 — No registration required

The widget registries scan `Assembly.GetEntryAssembly()` (i.e. `MySite`) automatically at startup:
`ContentZoneComponentRegistry` (admin metadata) and `IContentZoneWidgetRegistry` (render dispatch). No
changes to `ServiceCollectionExtensions.cs` or `Program.cs` are needed. The widget appears in the admin
component picker under its `Category`.

---

## How Zone Resolution Works

`ContentZone.razor` delegates to `IContentZoneResolver`, which resolves zones via the
`ContentZoneAssignments` table rather than path strings:

- **Page-scoped zones** look up by `(ParentPageMasterId, SlotName)`. If no assignment exists, a
  `ContentZoneDTO` + `ContentZoneAssignment` record are created automatically (lazily, in a transaction)
  on first resolve.
- **Nested zones** (zones rendered inside another zone's layout component) look up by
  `(ParentZoneId, SlotName)`, using the parent zone's ID carried by the `ParentZoneId` cascading value.
- **Global zones** (`IsGlobal="true"`) bypass assignment lookup and use name-based lookup on
  `ContentZoneDTO.Name`.

`ContentZoneDTO.Name` stores a human-readable slot name (e.g. `"Main"`, `"Sidebar"`); zone identity is
determined by the assignment record, not the name.

---

*For architectural reference — zone resolution algorithm, lazy zone creation, registry internals, nested zones, and component configuration contract — see [docs/architecture/04-content-zone-framework.md](architecture/04-content-zone-framework.md).*
