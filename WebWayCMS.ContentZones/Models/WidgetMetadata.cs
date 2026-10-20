using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones;

/// <summary>
/// Per-zone-item widget metadata, serialized into the zone item's <c>MetadataJson</c> column and
/// edited through the zone editor's settings (cog) dialog — separate from the content
/// configuration (<c>ComponentPropertiesJson</c>), so saving content config never touches it.
/// Carries the item's <see cref="DisplayMode"/> (single vs. list) plus presentation fields the zone
/// wrapper and the admin toolbar use (label, note, CSS class, anchor id).
/// </summary>
public class WidgetMetadata
{
    /// <summary>
    /// Whether the item renders a single item or a list. Only widgets that declare
    /// <see cref="ContentZoneComponentAttribute.SupportsList"/> may store <see cref="WidgetDisplayMode.List"/>
    /// (the save is rejected otherwise), and the settings dialog shows the field only for them.
    /// </summary>
    [FormProperty(
        Label = "Display Mode",
        HelpText = "Render a single item or a list. The configuration options change with the mode.",
        EditorType = EditorType.Dropdown,
        DropdownOptions = "Single:Single item,List:List",
        Order = 1,
        FormComponent = "Dropdown"
    )]
    [JsonConverter(typeof(WidgetDisplayModeJsonConverter))]
    public WidgetDisplayMode DisplayMode { get; set; } = WidgetDisplayMode.Single;

    /// <summary>
    /// Label shown for the item in the admin zone toolbar (falls back to the component name).
    /// </summary>
    [FormProperty(
        Label = "Admin Label",
        HelpText = "Shown in the zone editor toolbar instead of the component name.",
        EditorType = EditorType.Text,
        MaxLength = 100,
        Order = 2,
        FormComponent = "Text"
    )]
    [StringLength(100)]
    public string? AdminLabel { get; set; }

    /// <summary>
    /// Note shown under the admin toolbar label.
    /// </summary>
    [FormProperty(
        Label = "Admin Note",
        HelpText = "Optional note shown under the label in the zone editor.",
        EditorType = EditorType.TextArea,
        MaxLength = 500,
        Order = 3,
        FormComponent = "TextArea"
    )]
    [StringLength(500)]
    public string? AdminNote { get; set; }

    /// <summary>
    /// CSS classes appended to the rendered item's wrapper element. Letters, digits, underscores,
    /// hyphens and spaces only.
    /// </summary>
    [FormProperty(
        Label = "CSS Class",
        HelpText = "Extra CSS classes applied to the rendered item wrapper.",
        EditorType = EditorType.Text,
        MaxLength = 200,
        Pattern = "^[A-Za-z0-9_\\- ]*$",
        PatternErrorMessage = "CSS class may contain only letters, numbers, underscores, hyphens and spaces.",
        Order = 4,
        FormComponent = "Text"
    )]
    [StringLength(200)]
    [RegularExpression("^[A-Za-z0-9_\\- ]*$", ErrorMessage = "CSS class may contain only letters, numbers, underscores, hyphens and spaces.")]
    public string? CssClass { get; set; }

    /// <summary>
    /// Anchor id set on the rendered item's wrapper element. Must start with a letter; letters,
    /// digits, underscores, hyphens, colons and dots allowed afterwards (CSS identifier / id rules).
    /// </summary>
    [FormProperty(
        Label = "Anchor Id",
        HelpText = "Id attribute set on the rendered item wrapper (e.g. for in-page links).",
        EditorType = EditorType.Text,
        MaxLength = 100,
        Pattern = "^[A-Za-z][A-Za-z0-9_\\-:.]*$",
        PatternErrorMessage = "Anchor id must start with a letter and may contain only letters, numbers, underscores, hyphens, colons and dots.",
        Order = 5,
        FormComponent = "Text"
    )]
    [StringLength(100)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_\\-:.]*$", ErrorMessage = "Anchor id must start with a letter and may contain only letters, numbers, underscores, hyphens, colons and dots.")]
    public string? AnchorId { get; set; }
}
