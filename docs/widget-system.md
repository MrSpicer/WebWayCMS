# Widget System (Content Zones)

Content Zones are named, database-backed regions in a view that an admin can populate with **widgets** at runtime through the CMS admin UI — no code deploys required.

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Placing a Zone in a View](#placing-a-zone-in-a-view)
- [Editing and Publishing a Page's Zones](#editing-and-publishing-a-pages-zones)
- [Render Modes](#render-modes)
- [How to Add a New Widget](#how-to-add-a-new-widget)
- [Routable Widgets](#routable-widgets)

---

## System Overview

- A **Content Zone** is a named slot in a Razor view. Each zone stores an ordered list of widget instances in the database.
- The `ContentZone` view component renders all widgets assigned to a zone path. When an admin is viewing the page it also renders an inline "Add Widget" button and edit controls.
- **Widgets** are ViewComponent classes decorated with `[ContentZoneComponent]`. At startup the CMS reflects over the `WebWayCMS.Presentation` assembly and the entry assembly (`MySite`) and **seeds a row per widget** into the `WidgetRegistrations` table. From then on the runtime registry, `IWidgetRegistry`, serves widget metadata from the database — so widgets can be renamed, re-categorised, or deactivated from the admin UI at `/wadmin/widgets` with no code change.
- Each widget can declare a typed **configuration class** deriving from `WidgetConfiguration`. Properties on that class decorated with `[FormProperty]` are rendered as form fields in the admin "Add Widget" modal; the base class contributes the per-mode view pickers.
- Widgets participate in **render modes**: their views are named `{mode}.{variant}.cshtml` (public `view.*`/`list.*`, admin-page `admin.*`/`adminlist.*`, zone-editor `edit.*`); the active mode is derived from where the widget renders and the item's Single/List display mode. See [Render Modes](#render-modes).

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
| `WidgetViewComponent` | `WebWayCMS.Presentation/ViewComponents/WidgetViewComponent.cs` | Base class for mode-aware widgets; `ModeView(config, model)` resolves the active mode's view, `DisplayModeFor(config)` reports Single/List |
| `WidgetConfiguration` | `WebWayCMS.ContentZones/Models/WidgetConfiguration.cs` | Abstract base for widget configs; carries the per-mode view pickers and the attached `WidgetMetadata` |
| `WidgetMetadata` | `WebWayCMS.ContentZones/Models/WidgetMetadata.cs` | Per-item metadata (display mode, admin label/note, CSS class, anchor) stored in the zone item's `MetadataJson` column |
| `IWidgetViewResolver` / `WidgetViewResolver` | `WebWayCMS.Core/Services/WidgetViewResolver.cs` | Pure per-mode fallback logic (chosen variant → mode default → mode fallback → generic card/nothing) |
| `IViewDiscoveryService` / `ViewDiscoveryService` | `WebWayCMS.Core/Services/ViewDiscoveryService.cs` | Discovers `{mode}.{variant}.cshtml` files across host views and package application parts |

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

## Editing and Publishing a Page's Zones

Each row of the admin Pages list has an **Edit Zones** button that opens the page zone editor
(`/wadmin/contentzones/page/{pageNodeId}`): every zone assigned to the page, stacked on one screen in
the normal zone editor (Add Widget, edit, cog settings, delete; nested Layout zones inline). A toolbar
shows the draft status with **Publish**, **Discard changes**, **Unpublish**, **Edit page**, and **Back**;
a **Preview** tab shows the draft page in an iframe.

- **Edits to a page's zones are drafts.** Adding, editing, re-configuring, reordering, or deleting a
  widget in any zone under a page (nested zones included) saves a draft version; the live site is
  unchanged. A deleted widget that was live becomes an inactive draft; a never-published one is removed.
  This applies to every write path — this editor, the single-zone editor, the Advanced item pages, MCP.
- **Publishing the page publishes its zones.** Any page publish (this toolbar, the Pages list, the page
  form, MCP `publish_content`) publishes the page and every pending zone item under it in one change
  set. The Pages list shows **Pending** while zone drafts exist.
- **Discard changes** reverts every pending zone item to its published version (never-published items
  are deleted). Page field edits are not touched.
- **Global zones** (`IsGlobal = true`, e.g. a site footer) belong to no page, so their edits still go
  live immediately.
- A page's slots are created the first time its template renders. The editor handles a brand-new page by
  rendering the preview in the background and reloading once.

---

## Render Modes

A widget's views are split by **render mode**. View files are named `{mode}.{variant}.cshtml`
(mode lowercase, e.g. `view.TwoColumnEqual.cshtml`, `adminlist.default.cshtml`). The modes exist so a
site can be styled independently of the admin while the same content still renders correctly inside
the admin. Exactly one mode is active per render, and it is **derived, never chosen** by the widget or
its caller:

| Mode | Active when |
|---|---|
| `View` | A public page, item display mode `Single` (the default) |
| `List` | A public page, item display mode `List` |
| `Admin` | An admin page (request path under `/wadmin`), display mode `Single` |
| `AdminList` | An admin page, display mode `List` |
| `Edit` | The admin zone editor (`ContentZone` renders it with a light Bulma canvas; the editor is a CMS-owned surface, so edit views are plain Bulma markup). Only the zone being in edit mode selects it |

`WidgetViewComponent.GetContextMode` returns `Edit` when the zone's `ContentZone:RenderMode`
ViewData says so; otherwise admin vs. public comes from `AdminPathPrefix.Matches(Request.Path)` and
single vs. list from the item's display mode. Non-zone callers (e.g. the admin navbar's
`RouteNavigation`) get the right mode automatically from the path.

### Display mode: Single vs. List

A widget opts in to list rendering with `[ContentZoneComponent(SupportsList = true)]`. For such a
widget each zone item stores a **display mode** (`WidgetDisplayMode.Single` or `List`) in its metadata
(`WidgetMetadata.DisplayMode`):

- The zone editor's **Add Widget** modal shows a Single/List toggle; the **settings (cog)** dialog
  shows a Display Mode field. Neither appears for widgets without list support.
- Single and List have **different configurators**: tag each config field with the mode(s) it
  applies to, `[FormProperty(DisplayModes = WidgetDisplayMode.List, …)]` (untagged fields apply to
  both). `POST /wadmin/widgets/registry/{name}/form?displayMode=single|list` renders only that mode's
  fields. The zone editor merges the rendered fields over the stored configuration JSON, so switching
  modes keeps the other mode's values.
- The widget branches on `DisplayModeFor(config)` to load one item or a collection, then calls
  `ModeView(config, model)`; the base picks `view`/`list`/`admin`/`adminlist`/`edit` for it.
- Saving `List` for a widget that does not support it is **rejected** by
  `WidgetMetadataJson.Normalize` on every write path (zone API, MCP child saves, content seeds). A
  stored `List` on a widget that later drops support renders as `Single`, and is grandfathered so the
  item stays editable: an update of an item that already stores `List` keeps it, and restoring a
  historical `List` version (via `restore_child_version`) restores it as `Single`.
- `SupportsList` is stored on the widget registration and synced from the attribute on every boot
  by the registration seeder (it is shown read-only in the admin widget screen).

The TestHost's `Faq` widget is the reference example: Single resolves one FAQ from the URL slug or a
configured FAQ (`view.*`), List renders a sorted, limited list (`list.*`).

### Resolution fallback

`WidgetViewResolver.Resolve` applies a per-mode fallback chain. The admin and edit modes **mirror the
public variant name** — there are no separate admin/edit pickers — so a widget shipping `view.Card`
and `admin.Card` renders the matching admin view automatically:

- **View:** `view.{ViewName}` → `view.default` → nothing (the widget renders an empty string).
- **List:** `list.{ListViewName}` → `list.default` → the View chain.
- **Admin:** `admin.{ViewName}` → `admin.default` → the View chain.
- **AdminList:** `adminlist.{ListViewName}` → `adminlist.default` → `admin.{ViewName}` →
  `admin.default` → the List chain (and so the View chain).
- **Edit:** `edit.{ViewName}` (or `edit.{ListViewName}` when the item is in List mode, so a Layout's
  editor shows the same slots as its public variant) → `edit.default` → the generic CMS edit card
  (`_WidgetEditCard.cshtml`, showing the widget's registration info).

Empty or `"default"` always means the mode's default view. View pickers on the config form only list
the views that exist for that mode (a mode with no views renders no picker), so a stored value for a
renamed or missing view is kept selectable rather than silently dropped.

### Per-item metadata (the cog)

Each zone item has a `MetadataJson` column (4000 chars, normalized to `"{}"`) edited through the
zone editor's settings (cog) button: the display mode (list-capable widgets only) plus admin
presentation fields (label, note, CSS class, anchor id). The metadata is deserialized into
`WidgetMetadata` and attached to the configuration at render time, so views can also read
`Configuration.Metadata`. Saving is `PUT /api/contentzones/items/{id}/metadata` (the dialog always
sends the whole document; an empty payload clears it). A new item's metadata (the Add Widget modal's
display mode) travels on `POST /api/contentzones/items` as `metadataJson`. Through the generic
zone-item child save (MCP `create_child`/`update_child`, content seeds) an empty `metadataJson` means
"not supplied" and keeps the existing value. Every write path validates the JSON as a
`WidgetMetadata` (display mode, CSS-class and anchor-id patterns, lengths) via
`WidgetMetadataJson.Normalize` and stores the CMS's re-serialized canonical form, never the caller's
JSON verbatim.

### Breaking change: un-prefixed views and legacy `ViewName` values

Render modes are a hard break — there is no compatibility shim:

- Un-prefixed view files (`Default.cshtml`, `Compact.cshtml`) are ignored. A host override that is not
  renamed to `view.default.cshtml` (etc.) **silently stops overriding** and the CMS view renders instead.
- List rendering reads `ListViewName`, not `ViewName`. Items saved before the switch with a list
  variant in `viewName` render `list.default` until an editor re-picks the *List View*.
- Metadata keys from the first render-mode iteration (`EditViewName`, `SummaryViewName`,
  `AdminViewName`) and the `Summary` mode are gone; stored keys are ignored on parse.

### Known exception

`ArticleViewComponent` (the blog widget) predates render modes and still calls `View(...)` directly
with un-prefixed view names (`Article`, `List`, `UpsertForm`); it renders its public views in every
mode. Migrating it to the scheme is a known follow-up.

---

## How to Add a New Widget

All files live in **`MySite`**. No changes to the CMS library are needed.

### Step 1 — (Optional) Create a configuration class

**`MySite/ViewComponents/MyWidgetConfiguration.cs`**

Properties decorated with `[FormProperty]` appear as form fields in the admin "Add Widget" modal.
The class derives from `WidgetConfiguration`, which contributes the per-mode view pickers
(`ViewName`, `ListViewName`) — do not declare your own `ViewPicker` properties. Omit this class
entirely if the widget has no configuration (the component then passes `null` to `ModeView`).

```csharp
using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace MySite.ViewComponents;

public class MyWidgetConfiguration : WidgetConfiguration
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
| `ViewPicker` | Dropdown of the component's available views for the field's render mode (`RenderMode` on `[FormProperty]`) |
| `PageControllerPicker` | Dropdown populated client-side from the registered page types |
| `Hidden` | Hidden field (included in config, not shown) |

### Step 2 — Create the ViewComponent

**`MySite/ViewComponents/MyWidgetViewComponent.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;
using WebWayCMS.ViewComponents;

namespace MySite.ViewComponents;

[ContentZoneComponent(
    DisplayName = "My Widget",
    Description = "Displays a custom widget.",
    Category = "General",
    ConfigurationType = typeof(MyWidgetConfiguration),
    IconClass = "fa-star",
    Order = 10
)]
public class MyWidgetViewComponent : WidgetViewComponent
{
    public IViewComponentResult Invoke(MyWidgetConfiguration config)
    {
        config ??= new MyWidgetConfiguration();
        return ModeView(config, config);
    }
}
```

The component **must** derive from `WidgetViewComponent` (not plain `ViewComponent`) and return
`ModeView(config, model)` instead of `View(...)`: `ModeView` resolves the active render mode's view
through the `IWidgetViewResolver` (which the base class pulls from `HttpContext.RequestServices` on
first use — no constructor plumbing needed) and falls back to the generic edit card or empty content
per mode. The derived class name drives the component name (`MyWidgetViewComponent` → `MyWidget`),
which must match the `[ContentZoneComponent]` registration and the view directory name. A widget
that can also render a list sets `SupportsList = true`, tags its config fields with
`DisplayModes`, and branches its data loading on `DisplayModeFor(config)` (see
[Display mode](#display-mode-single-vs-list)).

`[ContentZoneComponent]` properties:

| Property | Description |
|---|---|
| `DisplayName` | Label shown in the admin "Add Widget" dropdown |
| `Description` | Help text shown in the admin UI |
| `Category` | Groups widgets in the dropdown (e.g. `"General"`, `"Content"`, `"Navigation"`) |
| `ConfigurationType` | The config class from Step 1; omit if no config is needed |
| `IconClass` | Font Awesome class for the admin UI icon (e.g. `"fa-star"`) |
| `Order` | Sort order within the category; lower values appear first |
| `SupportsList` | The widget can render a list as well as a single item (enables the zone editor's Single/List choice) |

These values are the **seed defaults** written into the widget's registration row the first time the
CMS starts. After that, the row is authoritative — edit the widget at `/wadmin/widgets` rather than
changing the attribute. The exception is `SupportsList`, a code capability that the seeder re-syncs from the attribute on
every boot.

### Step 3 — Create the Razor views

Views are named **`{mode}.{variant}.cshtml`** (mode lowercase). At minimum a public view:

**`MySite/Views/Shared/Components/MyWidget/view.default.cshtml`**

```cshtml
@model MySite.ViewComponents.MyWidgetConfiguration

<div class="my-widget @(Model.ShowBorder ? "bordered" : "")">
    <h2>@Model.Heading</h2>
</div>
```

Additional variants (e.g. `view.Compact.cshtml`) are picked by the `View` picker on the config form.
Optional per-mode files:

- `edit.default.cshtml` (+ variants) — the admin zone editor's rendering. Plain **Bulma** markup on a
  light canvas; without any `edit.*` file the editor shows the generic widget card instead.
- `list.default.cshtml` (+ variants) — list rendering, for an item in List display mode
  (`SupportsList` widgets).
- `admin.*` / `adminlist.*` — the same content rendered on an admin page (admin styling), mirroring
  the `view.*` / `list.*` variant names; without them the admin falls back to the public views.

Un-prefixed files are ignored by view discovery (they are host-internal or pre-render-mode
leftovers). A mode with no views resolves through the fallback chain in
[Render Modes](#render-modes).

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

## Routable Widgets

A widget can own **routes**: it implements `IRoutableViewComponent` and declares the route patterns
(`CMSRouteDTO`) it wants registered. When a request matches one of those patterns, the CMS dispatches
to the parent page's controller, which renders the page's `Main` zone — so the routing widget renders
in the same zone as everything else and discriminates on the route values it captured (e.g. `slug`
vs `topic`).

### The interface

`WebWayCMS.Interfaces.IRoutableViewComponent`:

```csharp
public interface IRoutableViewComponent
{
    string ComponentName { get; }

    Task<IReadOnlyList<CMSRouteDTO>> GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, CancellationToken ct);
}
```

- `ComponentName` — the widget's ViewComponent name (without the `ViewComponent` suffix).
- `GenerateRoutesAsync` — returns the route(s) the widget wants, given the parent page's route
  pattern and the content-zone item node id. Set `OwningContentNodeId` to the item node id and give
  each route an explicit `OwningContentType` (the fallback default is `"ArticleWidget"`, which is
  Article-specific). `Pattern` is a **relative** pattern (e.g. `"{slug}"` or `topics/{topic}`) — the
  CMS prefixes it with the parent page's route.

### Required DI registration

`RouteRegistrationService` collects widgets through `IEnumerable<IRoutableViewComponent>`, so each
routable widget needs two registrations in `Program.cs`:

```csharp
builder.Services.AddScoped<MyRoutableViewComponent>();
builder.Services.AddScoped<IRoutableViewComponent>(sp => sp.GetRequiredService<MyRoutableViewComponent>());
```

The widget-registration seeder still discovers the component from its `[ContentZoneComponent]`
attribute — this registration is *only* what makes routing work.

### Route lifecycle

Route rows follow what is **live**, so when they change depends on the zone:

- **Page zones** (the usual case — a routable widget's routes are prefixed with its page's route): an
  item is a draft until the page is published, so its routes are (re)registered by the **page publish
  cascade** (`PageModel.PublishPageAsync` → `TryRegisterWidgetRoutesAsync` for each item that went live,
  with `isActive = false` for a published removal, which deletes the owner's routes). Saving or deleting
  a draft never touches the route table.
- **Global zones** — items publish on write, so `ContentZoneApiController` (and the admin child handler)
  call `IRouteRegistrationService.TryRegisterWidgetRoutesAsync` on **add**, **edit** (re-registers,
  sweeping the owner's stale routes first), and **delete / deactivate** (`isActive = false`).

A non-routable component simply isn't found in the collection, so `TryRegisterWidgetRoutesAsync`
early-outs and writes no routes.

### Parent-route prefixing

`RegisterWidgetRoutesAsync` prefixes each widget pattern with the parent page's route pattern, then
normalises the result. A `Faq` widget under a page at `/faq-test` therefore produces
`/faq-test/{slug}`.

### Route constraints must be inline

Put constraints in the pattern itself — `Pattern = "{slug:regex(^[a-z0-9-]+$)}"` — not in
`CMSRouteDTO.ConstraintsJson`. Route matching (`CMSRouteService.TryMatchPattern`) parses constraints
out of the pattern text and **never reads `ConstraintsJson`**, so a constraint declared there is
silently unenforced. Supported inline constraints are `int`, `guid`, `bool` and `regex(...)`.

### Lowercasing caveat

`NormalizePattern` lowercases the **entire** pattern, including any inline regex constraint. Keep
every literal segment and every inline regex (`regex(...)`) in a widget pattern lowercase, or the
constraint will silently stop matching after normalisation.

### Configuration properties must tolerate `null`

The admin form serializer writes `null` for any empty field. A non-nullable value-type config
property (e.g. `int PageSize`) therefore fails deserialization on an empty input, and the failure
discards the widget's **entire** stored configuration rather than just that value. Declare numeric
properties as nullable (`int?`) and apply the fallback in the widget.

---

## How Zone Resolution Works

The `ContentZoneViewComponent` resolves zones via the `ContentZoneAssignments` table rather than path strings:

- **Page-scoped zones** look up by `(ParentPageNodeId, SlotName)` in `ContentZoneAssignments`. If no assignment exists and the user is an admin, a `ContentZoneDTO` + `ContentZoneAssignment` record are created automatically.
- **Nested zones** (zones rendered inside another zone's layout component) look up by `(ParentZoneNodeId, SlotName)` using the parent zone's ID stored in render context.
- **Global zones** (`IsGlobal = true`) bypass assignment lookup and use name-based lookup on `ContentZoneDTO.Name`.

The `ContentZoneDTO.Name` field now stores a human-readable slot name (e.g. `"Main"`, `"Sidebar"`) rather than an opaque path. Zone identity is determined by the assignment record, not the name.

---

*For architectural reference — zone resolution algorithm, lazy zone creation, registry internals, nested zones, and component configuration contract — see [docs/architecture/04-content-zone-framework.md](architecture/04-content-zone-framework.md).*

## The Image widget

`ImageViewComponent` (category "Media") renders an image content item chosen with an entity picker.
Its configuration stores `Guid? ImageNodeId` — **nullable on purpose**: a non-nullable value type in
a widget configuration silently discards the entire saved configuration when the field is left
empty, because the editor writes `null`, deserialization throws, and the zone model falls back to an
empty configuration object.

See [architecture/16-media-and-images](architecture/16-media-and-images.md).
