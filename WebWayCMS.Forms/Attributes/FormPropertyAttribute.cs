namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a property on a model as configurable in the admin UI.
/// Provides metadata for form generation including labels, editor types, and validation hints.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FormPropertyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the label displayed for this property in the configuration form.
    /// If not specified, the property name is used with spacing inserted before capitals.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the help text/description shown below the input field.
    /// </summary>
    public string HelpText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the placeholder text for the input field.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of editor to render for this property.
    /// If not specified, the editor type is inferred from the property's CLR type.
    /// </summary>
    public EditorType EditorType { get; set; } = EditorType.Text;

    /// <summary>
    /// Gets or sets the display order of this property in the form.
    /// Lower values appear first. Default is 0.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Gets or sets the CSS class(es) to apply to the form field container.
    /// </summary>
    public string CssClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this property should be grouped
    /// with the next property on the same row (for responsive layouts).
    /// </summary>
    public bool GroupWithNext { get; set; } = false;

    /// <summary>
    /// Gets or sets the name of the group/section this property belongs to.
    /// Properties with the same group name are rendered together under a heading.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets dropdown options as a comma-separated string.
    /// Only used when <see cref="EditorType"/> is <see cref="Attributes.EditorType.Dropdown"/>.
    /// Format: "value1:Label 1,value2:Label 2" or "value1,value2" (value used as label).
    /// </summary>
    public string DropdownOptions { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entity type name for GUID pickers that need to query the database.
    /// Examples: "ContentBlock", "Article", "ContentZone".
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ViewComponent name for ViewPicker editors.
    /// Used to discover available views for the specified component.
    /// Only used when <see cref="EditorType"/> is <see cref="Attributes.EditorType.ViewPicker"/>.
    /// </summary>
    public string ViewComponentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this property is required.
    /// This is a convenience property; you can also use [Required] attribute.
    /// </summary>
    public bool IsRequired { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum value for numeric properties.
    /// Use <see cref="double.NaN"/> or don't set to indicate no minimum.
    /// </summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>
    /// Gets or sets the maximum value for numeric properties.
    /// Use <see cref="double.NaN"/> or don't set to indicate no maximum.
    /// </summary>
    public double Max { get; set; } = double.NaN;

    /// <summary>
    /// Gets or sets the maximum length for string properties.
    /// Use -1 or don't set to indicate no maximum length.
    /// </summary>
    public int MaxLength { get; set; } = -1;

    /// <summary>
    /// Gets or sets a regex pattern for string validation.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message to display when pattern validation fails.
    /// </summary>
    public string PatternErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormPropertyAttribute"/> class.
    /// </summary>
    public FormPropertyAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FormPropertyAttribute"/> class
    /// with a label and editor type.
    /// </summary>
    /// <param name="label">The label for the property.</param>
    /// <param name="editorType">The type of editor to use.</param>
    public FormPropertyAttribute(string label, EditorType editorType = EditorType.Text)
    {
        Label = label;
        EditorType = editorType;
    }
}
