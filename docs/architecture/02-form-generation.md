# Area 2: Form Generation & Configuration Metadata

**Namespaces:**
- `WebWayCMS.Attributes` — `FormPropertyAttribute`, `EditorType`, `PageControllerAttribute`, `ContentZoneComponentAttribute`
- `WebWayCMS.Forms` — `FormPropertyBuilder`, `FormPropertyInfo`, `FormOption`
- `WebWayCMS.Presentation.Components.Admin` — `InteractiveFormFields` (Blazor form renderer)
- `WebWayCMS.Presentation.Rendering` — `IFormOptionsProvider`

**Depends on:** Nothing (pure reflection; no external dependencies)
**Consumed by:** Page Routing Subsystem (registry validates config), Content Zone Component Framework (registry validates config), Admin CRUD Framework (`InteractiveFormFields.razor` in admin pages)

---

## 1. Purpose

Admin forms in the CMS are generated from C# attributes — no per-type Razor boilerplate is needed. Any configuration class decorated with `[FormProperty]` attributes automatically gets a rendered form in the admin UI. The same mechanism drives:
- Page configuration forms (when editing a page's per-controller settings)
- Widget (content zone component) configuration forms
- Any future configuration class

The pipeline is: **attributes on a class → `FormPropertyBuilder` → `List<FormPropertyInfo>` → `InteractiveFormFields.razor` → rendered fields**.

---

## 2. `[FormProperty]` Reference

`FormPropertyAttribute` is applied to individual properties on configuration classes. All properties are optional.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Label` | `string` | Property name with spaces inserted before capitals | Display label in the form |
| `HelpText` | `string` | `""` | Help text shown below the field |
| `Placeholder` | `string` | `""` | Input placeholder |
| `EditorType` | `EditorType` | `EditorType.Text` | Which HTML editor to render (see §3) |
| `Order` | `int` | `0` | Sort order within the form; lower = first; secondary sort by property name |
| `Group` | `string` | `""` | Section heading; properties sharing a group name are rendered under that heading |
| `GroupWithNext` | `bool` | `false` | Render this field and the next on the same horizontal row |
| `CssClass` | `string` | `""` | Extra CSS class(es) on the field container `<div>` |
| `IsRequired` | `bool` | `false` | Convenience shorthand for `[Required]` |
| `Min` | `double` | `NaN` (no minimum) | Minimum value for numeric fields |
| `Max` | `double` | `NaN` (no maximum) | Maximum value for numeric fields |
| `MaxLength` | `int` | `-1` (no limit) | Maximum character count for string fields |
| `Pattern` | `string` | `""` | Regex pattern for validation |
| `PatternErrorMessage` | `string` | `""` | Error message shown when pattern fails |
| `DropdownOptions` | `string` | `""` | Comma-separated `"value:Label,value:Label"` pairs for `Dropdown` editors |
| `EntityType` | `string` | `""` | Entity type name for GUID pickers, e.g. `"ContentBlock"` |
| `ViewComponentName` | `string` | `""` | Picker-source key for `ViewPicker` editors (e.g. `"Layout"`); resolved by `IFormOptionsProvider` |

**Constructors:**
```csharp
[FormProperty]                                         // all defaults
[FormProperty("My Label")]                             // label only
[FormProperty("My Label", EditorType.TextArea)]        // label + editor type
```

---

## 3. `EditorType` Enum

| Value | HTML Rendered | Notes |
|-------|---------------|-------|
| `Text` | `<input type="text">` | Default for `string` |
| `TextArea` | `<textarea>` | Multi-line |
| `RichText` | `<RichTextEditor>` component | CKEditor 5 via the `richtext.js` JS-interop wrapper |
| `Number` | `<input type="number">` | Respects `Min`/`Max` |
| `Checkbox` | `<input type="checkbox">` | Default for `bool` |
| `Guid` | `<input type="text">` | Default for `Guid`; `EntityType` enables DB-backed picker |
| `Dropdown` | `<select>` | Requires `DropdownOptions`; also auto-selected for enums |
| `Date` | `<input type="date">` | Default for `DateOnly` |
| `DateTime` | `<input type="datetime-local">` | Default for `DateTime`/`DateTimeOffset` |
| `Color` | `<input type="color">` | Browser color picker |
| `Url` | `<input type="url">` | URL validation |
| `Email` | `<input type="email">` | Email validation |
| `ViewPicker` | `<select>` | Populated in-circuit by `IFormOptionsProvider` (e.g. the Layout widget's templates); `ViewComponentName` selects the source |
| `PageControllerPicker` | `<select>` | Rendered by the dedicated `AdminPageUpsert` page editor, populated in-circuit from `IPageControllerRegistry`; used by the page editor |
| `Hidden` | `<input type="hidden">` | Not displayed; included in form POST |

**Type inference** (when `EditorType` is not set on `[FormProperty]` and there is no attribute at all):

```
Guid → Guid
bool → Checkbox
int/long/short/decimal/double/float → Number
DateTime/DateTimeOffset → DateTime
DateOnly → Date
enum → Dropdown
everything else → Text
```

---

## 4. `FormPropertyBuilder.BuildPropertyInfos`

`FormPropertyBuilder` is a static class. `BuildPropertyInfos(Type modelType)` reflects over every public read-write instance property and builds a `FormPropertyInfo` for it. Properties without `[FormProperty]` are still included (using inferred defaults), which means all public properties on a config class become form fields unless you omit `[FormProperty]` and the type is not appropriate.

**Merge order for validation constraints:**
1. `[FormProperty]` attribute values take precedence
2. Standard data annotation attributes (`[Required]`, `[Range]`, `[StringLength]`, `[RegularExpression]`) fill in where `[FormProperty]` does not specify

**Sorting:** Results are sorted by `Order` ascending, then alphabetically by property name. This is the order in which `InteractiveFormFields` renders fields.

**Dropdown parsing:** `DropdownOptions` string `"a:Alpha,b:Beta"` produces `{ "a": "Alpha", "b": "Beta" }`. If no `:` separator, value is used as label.

---

## 5. `InteractiveFormFields.razor`

The metadata-driven form is an **Interactive Server Blazor component** (replacing the former
`<form-fields>` tag helper). It is used by the admin CRUD pages (`AdminUpsert` / `AdminChildUpsert` /
`AdminPageUpsert`) and the content-zone editor (`ContentZoneItemForm`).

**Usage in a Razor component:**
```razor
<InteractiveFormFields Model="_config"
                       Properties="_props"
                       Options="_options" />
```

- `Model` — the configuration/ViewModel object being edited; each field reads/writes the bound
  property by reflection (`FormValueConverter`), so edits update the model in place ready for save or
  JSON serialization.
- `Properties` — the `List<FormPropertyInfo>` from `FormPropertyBuilder.BuildPropertyInfos`.
- `Options` — an optional `IReadOnlyDictionary<string, IReadOnlyList<FormOption>>` keyed by property
  name, supplying selectable options for entity/view picker fields. The hosting page obtains these from
  `IFormOptionsProvider.GetOptionsAsync(prop)`; fields without an entry render plain inputs.

**Layout behavior:** fields are grouped under their `FormProperty` `Group` heading and rendered with
Bulma styling, in the `FormPropertyBuilder` sort order.

**Editor coverage:** Hidden, Text, TextArea, DateTime, Date, Number, Url, Email, Color, Checkbox,
Dropdown, entity/view pickers (via `Options`), and RichText (the `RichTextEditor` CKEditor component).

Validation runs against the model's DataAnnotations on the hosting page's submit, and the page surfaces
the resulting messages.

---

## 6. `[PageControllerAttribute]` and `[ContentZoneComponentAttribute]`

Both attributes follow the same structure. They are applied at the class level to mark a controller or Blazor widget as discoverable by the respective registry.

`[PageControllerAttribute]` properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Controller name with spaces | Shown in page type dropdown |
| `Description` | `string` | `""` | Help text in admin UI |
| `Category` | `string` | `"General"` | Groups related page types |
| `ConfigurationType` | `Type?` | `null` | Config class whose properties become the page's configuration form |
| `IconClass` | `string` | `""` | CSS icon class for admin UI |
| `Order` | `int` | `0` | Sort order within category |

`[ContentZoneComponentAttribute]` properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | Component name with spaces | Shown in widget picker |
| `Description` | `string` | `""` | Help text |
| `Category` | `string` | `"General"` | Groups related widgets |
| `ConfigurationType` | `Type?` | `null` | Config class whose properties become the widget's config form |
| `IconClass` | `string` | `""` | CSS icon class |
| `Order` | `int` | `0` | Sort order within category |

Full registration and discovery details: see [Area 3](03-page-routing.md) and [Area 4](04-content-zone-framework.md).

---

## 7. Configuration Class Conventions

A configuration class is any POCO whose properties are decorated with `[FormProperty]`. Use one when:
- A page type or widget needs per-instance settings that the editor configures in the admin UI
- Those settings are stored as JSON (`ConfigurationJson` on `PageDTO`, `ComponentPropertiesJson` on `ContentZoneItemDTO`)

**Conventions:**
- Place alongside the controller or widget it belongs to
- Use simple value types or nullable types only (must survive JSON round-tripping)
- Use `[FormProperty]` on every property that should be editable; omit it for computed/internal properties
- Name the class `{PageType}PageConfiguration` or `{ComponentName}Configuration` by convention

**Annotated example:**
```csharp
public class FeaturedArticleConfiguration
{
    [FormProperty("Article List", EditorType.Guid,
        HelpText = "The article list to pull the featured item from",
        EntityType = "ArticleList",
        IsRequired = true)]
    public Guid ArticleListId { get; set; }

    [FormProperty("Show Excerpt", Order = 10)]
    public bool ShowExcerpt { get; set; } = true;

    [FormProperty("Max Items", EditorType.Number, Order = 20, Min = 1, Max = 10)]
    public int MaxItems { get; set; } = 3;
}
```
