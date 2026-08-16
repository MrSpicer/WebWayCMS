# Form Control System

Admin forms in the CMS are generated from C# attributes — no per-type Razor form boilerplate is
needed. Any configuration class whose properties are decorated with `[FormProperty]` automatically
gets a rendered form in the admin UI. This is the same mechanism behind content-type upsert forms
(see [`docs/content-system.md`](content-system.md)), page-controller configuration forms (see
[`docs/page-system.md`](page-system.md)), and widget configuration forms (see
[`docs/widget-system.md`](widget-system.md)).

## Table of Contents

- [System Overview](#system-overview)
- [Core Components](#core-components)
- [Using Form Fields in a View](#using-form-fields-in-a-view)
- [How to Add a New Form Control](#how-to-add-a-new-form-control)
- [EditorType Reference](#editortype-reference)

---

## System Overview

- Each property on a configuration class decorated with `[FormProperty]` (Label, HelpText,
  `EditorType`, validation hints, layout options) is reflected by `FormPropertyBuilder` into a
  `List<FormPropertyInfo>` — no attribute is required either; unmarked public properties still get
  a field with inferred defaults.
- For each field, `FormComponentResolver` picks a **form control** — a `Form*` ViewComponent — by
  asking `IFormComponentRegistry`: explicit component name first, then `EditorType` alias, then the
  default component for the property's CLR type, then a `"Text"` fallback.
- Form controls are ViewComponent classes decorated with `[CMSFormComponent]`. At startup the CMS
  reflects over its own assemblies and the entry assembly and **seeds a row per control** into the
  `FormComponentRegistrations` table (mirrors the widget and page-type seeders). From then on the
  runtime registry, `IFormComponentRegistry`, serves control metadata from the database — so
  controls can be recategorized or reordered from the admin UI at `/wadmin/formcomponents` with no
  code change.
- `<form-fields for="@Model" />` reflects the passed object and renders one `<form-field>` per
  property; each invokes the resolved `Form*` ViewComponent, which builds its HTML attributes via
  `FormAttributeBuilder` (every value HTML-encoded exactly once) and emits them with `@Html.Raw`.

---

## Core Components

| Class | File | Role |
|---|---|---|
| `FormPropertyAttribute` / `EditorType` | `WebWayCMS.Forms/Attributes/FormPropertyAttribute.cs`, `EditorType.cs` | Declares a property as a form field and its editor kind, validation hints, and layout options |
| `FormPropertyBuilder` | `WebWayCMS.Forms/Forms/FormPropertyBuilder.cs` | Reflects a config `Type` into `List<FormPropertyInfo>`, merging `[FormProperty]` with data annotations |
| `IFormComponentResolver` / `FormComponentResolver` | `WebWayCMS.Forms/Forms/FormComponentResolver.cs` | Pure resolution logic: picks which form control renders a given `FormPropertyInfo` |
| `IFormComponentRegistry` / `FormComponentRegistry` | `WebWayCMS.Core/Forms/FormComponentRegistry.cs` | Runtime form control metadata, loaded from the database with a 5-minute cache |
| `[CMSFormComponent]` | `WebWayCMS.Forms/Attributes/CMSFormComponentAttribute.cs` | Marks a ViewComponent as a form control; read once at startup to seed its registration row |
| `FormAttributeBuilder` | `WebWayCMS.Forms/Forms/FormAttributeBuilder.cs` | Fluent builder that produces HTML-encoded-once attribute strings for a field |
| `FormFieldsTagHelper` / `FormFieldTagHelper` | `WebWayCMS.Forms/TagHelpers/` | `<form-fields for="@Model" />` renders every field; `<form-field>` emits the shared chrome around one |
| `FormFieldViewComponentBase` | `WebWayCMS.Presentation/ViewComponents/Forms/FormFieldViewComponentBase.cs` | Base class for form controls; selects the Write or Read view from `FormFieldContext.Mode` |
| `FormComponentRegistrationModel` | admin content type at `/wadmin/formcomponents` | Lets admins edit a control's category, order, and display name without a deploy |

---

## Using Form Fields in a View

```cshtml
<form-fields for="@Model.Configuration" />
```

- `for` — any object; its runtime type is reflected to build the field list. Used for content-type
  upsert view models, and for page-controller / widget configuration objects.
- `Group` — properties sharing a `[FormProperty(Group = "...")]` name are wrapped together under a
  section heading.
- `GroupWithNext` — set on a property to place it and the next property side-by-side on one row.
- **Binding mode** — model-bound forms (`binding="Model"`, the default) post as normal form fields;
  JSON-bound forms (`binding="Json"`) post `data-prop` attributes instead, used by
  `DynamicConfigurationForm` to serve the dynamic `POST {contentType}/registry/{name}/form`
  sub-forms for page-controller and widget configuration.

---

## How to Add a New Form Control

All files can live in the **`WebWayCMS.Presentation`** ViewComponents/Views tree (for a control
shipped with the CMS) or in the host project (for a site-specific control) — the seeder scans both.

### Step 1 — (Optional) Add an `EditorType` value

Only needed if this is a genuinely new *kind* of editor that other developers should be able to
select with `[FormProperty(EditorType = EditorType.MyType)]`. A control can also be selected purely
by registry name via `[FormProperty(FormComponent = "MyControl")]`, with no enum change at all.

`WebWayCMS.Forms/Attributes/EditorType.cs`

```csharp
public enum EditorType
{
    // ...existing values...
    Rating,
}
```

### Step 2 — Create the ViewComponent

```csharp
using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Rating", DataTypes = new[] { typeof(int) },
                  EditorType = EditorType.Rating, IsDefaultForType = false,
                  Category = "Number", Order = 5,
                  DisplayName = "Star Rating", Description = "1-5 star rating input.")]
public sealed class FormRating : FormFieldViewComponentBase { }
```

`[CMSFormComponent]` properties:

| Property | Description |
|---|---|
| `Name` (constructor arg) | Registry key, e.g. `"Rating"`. Defaults to the class name minus `"ViewComponent"` if omitted |
| `DataTypes` | CLR types this control can edit, e.g. `new[] { typeof(int) }` |
| `IsDefaultForType` | This control is the default for its `DataTypes` when no other selector matches |
| `EditorType` | The `EditorType` alias this control answers to (optional — a control can be name-only) |
| `DisplayName` | Label shown in the admin form-components UI |
| `Description` | Help text shown in the admin UI |
| `Category` | Groups related controls, e.g. `"Text"`, `"Number"` |
| `IconClass` | Font Awesome class for the admin UI icon |
| `Order` | Sort order within the category; lower values appear first |
| `WriteViewName` / `ReadViewName` | View names to invoke; default to `"Write"` / `"Read"` |

These values are the **seed defaults** written into the control's registration row the first time
the CMS starts. After that, the row is authoritative — edit the control at `/wadmin/formcomponents`
rather than changing the attribute.

### Step 3 — Create the Razor view

**`Views/Shared/Components/FormRating/Write.cshtml`**

```cshtml
@model WebWayCMS.Forms.FormFieldContext
@{
    var attrs = WebWayCMS.Forms.FormAttributeBuilder.For(Model)
        .Type("number").Css("input")
        .Naming().Value().Required().DescribedBy()
        .Build();
}
<form-field field="@Model">
    <input min="1" max="5" @Html.Raw(attrs) />
</form-field>
```

The view receives a `FormFieldContext` (the field's name, value, label, required/validation state,
and read/write mode). Build attribute strings with `FormAttributeBuilder` and emit them with
`@Html.Raw` so nothing is double-encoded — wrap the input in `<form-field>` to get the standard
label/help/validation chrome for free.

For a control that renders its own layout (e.g. a checkbox with an inline `<label>`), pass
`chrome="none"` to `<form-field>` instead, as `FormCheckbox`'s view does. Add a `Read.cshtml` in the
same folder for a dedicated read-only rendering; omit it to fall back to the write view.

### Step 4 — No registration required

At startup the CMS scans its own assemblies and `Assembly.GetEntryAssembly()` and seeds a
`FormComponentRegistrations` row for your control. No changes to `ServiceCollectionExtensions.cs`
or `Program.cs` are needed, and the control is available to `[FormProperty(EditorType = ...)]` or
`[FormProperty(FormComponent = "Rating")]` on the next request.

Two consequences worth knowing:

- **Seeding only inserts.** If you later change the attribute's `DisplayName`, `Category`, `Order`,
  or `IsDefaultForType`, the stored row is *not* updated. Edit the control at
  `/wadmin/formcomponents`, or delete its row and restart to re-seed.
  `WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS=true` suppresses seeding.
- **A control that fails to resolve falls back to `"Text"`.** If the registry row is deleted or
  deactivated, fields that named it explicitly render as plain text inputs rather than failing.

---

## EditorType Reference

| Value | Editor rendered | Default for |
|---|---|---|
| `Text` | `<input type="text">` | `string` |
| `TextArea` | `<textarea>` | — |
| `RichText` | `<textarea class="rich-text-editor">` (CKEditor attached by admin JS) | — |
| `Number` | `<input type="number">`, respects `Min`/`Max` | `int`/`long`/`decimal`/`double`/`float` |
| `Checkbox` | `<input type="checkbox">` | `bool` |
| `Guid` | `<input type="text">`; `EntityType` enables a DB-backed picker | `Guid` |
| `Dropdown` | `<select>`, requires `DropdownOptions` | `enum` |
| `Date` | `<input type="date">` | `DateOnly` |
| `DateTime` | `<input type="datetime-local">` | `DateTime`/`DateTimeOffset` |
| `Color` | `<input type="color">` | — |
| `Url` | `<input type="url">` with validation | — |
| `Email` | `<input type="email">` with validation | — |
| `ViewPicker` | `<select>` populated from `IViewDiscoveryService`; requires `ViewComponentName` | — |
| `PageControllerPicker` | `<select>` populated client-side from the page-type registry | — |
| `Hidden` | `<input type="hidden">`, not displayed but included in the form post | — |

---

*For architectural reference — the full `[FormProperty]` attribute surface, resolution order,
`FormAttributeBuilder` contract, and dynamic sub-form materialization — see
[docs/architecture/02-form-generation.md](architecture/02-form-generation.md).*
