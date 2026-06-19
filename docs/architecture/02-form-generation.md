# Area 2: Form Generation & Configuration Metadata

**Namespaces:**
- `WebWayCMS.Attributes` — `FormPropertyAttribute`, `EditorType`, `PageControllerAttribute`, `ContentZoneComponentAttribute`
- `WebWayCMS.Forms` — `FormPropertyBuilder`, `FormPropertyInfo`
- `WebWayCMS.TagHelpers` — `FormFieldsTagHelper`

**Depends on:** Nothing (pure reflection; no external dependencies)
**Consumed by:** Page Routing Subsystem (registry validates config), Content Zone Component Framework (registry validates config), Admin CRUD Framework (`<form-fields>` tag helper in views)

---

## 1. Purpose

Admin forms in the CMS are generated from C# attributes — no per-type Razor boilerplate is needed. Any configuration class decorated with `[FormProperty]` attributes automatically gets a rendered form in the admin UI. The same mechanism drives:
- Page configuration forms (when editing a page's per-controller settings)
- Widget (content zone component) configuration forms
- Any future configuration class

The pipeline is: **attributes on a class → `FormPropertyBuilder` → `List<FormPropertyInfo>` → `FormFieldsTagHelper` → rendered HTML**.

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
| `ViewComponentName` | `string` | `""` | ViewComponent name for `ViewPicker` editors |

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
| `RichText` | `<textarea class="rich-text-editor">` | CKEditor is attached by admin JS |
| `Number` | `<input type="number">` | Respects `Min`/`Max` |
| `Checkbox` | `<input type="checkbox">` | Default for `bool` |
| `Guid` | `<input type="text">` | Default for `Guid`; `EntityType` enables DB-backed picker |
| `Dropdown` | `<select>` | Requires `DropdownOptions`; also auto-selected for enums |
| `Date` | `<input type="date">` | Default for `DateOnly` |
| `DateTime` | `<input type="datetime-local">` | Default for `DateTime`/`DateTimeOffset` |
| `Color` | `<input type="color">` | Browser color picker |
| `Url` | `<input type="url">` | URL validation |
| `Email` | `<input type="email">` | Email validation |
| `ViewPicker` | `<select>` | Populated with available views via `IViewDiscoveryService`; `ViewComponentName` required |
| `PageControllerPicker` | `<select data-page-controller-picker>` | Empty select populated client-side from the page controller registry (`/admin/pages/registry`); used by the page editor |
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

**Sorting:** Results are sorted by `Order` ascending, then alphabetically by property name. This is the order in which `FormFieldsTagHelper` renders fields.

**Dropdown parsing:** `DropdownOptions` string `"a:Alpha,b:Beta"` produces `{ "a": "Alpha", "b": "Beta" }`. If no `:` separator, value is used as label.

---

## 5. `FormFieldsTagHelper`

**Usage in Razor:**
```html
<form-fields for="@Model.Configuration" />
```

The tag helper inspects the passed object's runtime type, calls `FormPropertyBuilder.BuildPropertyInfos`, and emits Bulma-styled HTML. It renders no wrapper element of its own (`output.TagName = null`).

**Layout behavior:**
- Properties in the same `Group` are wrapped in `<div class="form-group-section">` with an `<h3>` heading.
- Properties with `GroupWithNext = true` are placed side-by-side in `<div class="field is-horizontal"><div class="field-body">`.
- All other properties are stacked vertically.

**Validation attributes emitted:**
- `required aria-required="true"` for required fields
- `maxlength="{n}"` for string fields with `MaxLength`
- `min="{n}"` and `max="{n}"` for number fields
- `pattern="{regex}"` for fields with `Pattern`
- `aria-describedby="{fieldId}_help"` when `HelpText` is set
- `<span role="alert" data-valmsg-for="{name}">` for client-side validation message display

**Value formatting for special types:**
- `DateTime` → `"yyyy-MM-ddTHH:mm"` for `<input type="datetime-local">`
- `DateOnly` → `"yyyy-MM-dd"`
- `Guid.Empty` → `""` (displayed as blank)

---

## 6. `[PageControllerAttribute]` and `[ContentZoneComponentAttribute]`

Both attributes follow the same structure. They are applied at the class level to mark a controller or ViewComponent as discoverable by the respective registry.

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
- Place alongside the controller or ViewComponent it belongs to
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
