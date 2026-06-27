using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Contains metadata about a configurable property on a model.
/// Used by the admin UI to generate form fields dynamically.
/// </summary>
public class FormPropertyInfo
{
    /// <summary>
    /// Gets or sets the property name (used as the form field name / JSON key).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label for the form field.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the help text shown below the field.
    /// </summary>
    public string HelpText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the placeholder text for the input.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of editor to render.
    /// </summary>
    public EditorType EditorType { get; set; } = EditorType.Text;

    /// <summary>
    /// Gets or sets the CLR type of the property.
    /// </summary>
    public Type PropertyType { get; set; } = typeof(string);

    /// <summary>
    /// Gets or sets the display order within the form.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Gets or sets the CSS class(es) for the field container.
    /// </summary>
    public string CssClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this field groups with the next.
    /// </summary>
    public bool GroupWithNext { get; set; } = false;

    /// <summary>
    /// Gets or sets the group/section name for this property.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this property is required.
    /// </summary>
    public bool IsRequired { get; set; } = false;

    /// <summary>
    /// Gets or sets the entity type for GUID picker fields.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ViewComponent name for ViewPicker fields.
    /// </summary>
    public string ViewComponentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets dropdown options as key-value pairs.
    /// </summary>
    public Dictionary<string, string> DropdownOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum value for numeric fields.
    /// </summary>
    public double? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum value for numeric fields.
    /// </summary>
    public double? Max { get; set; }

    /// <summary>
    /// Gets or sets the maximum length for string fields.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the regex pattern for validation.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message for pattern validation.
    /// </summary>
    public string PatternErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default value for this property (as a string for JSON serialization).
    /// </summary>
    public object? DefaultValue { get; set; }
}