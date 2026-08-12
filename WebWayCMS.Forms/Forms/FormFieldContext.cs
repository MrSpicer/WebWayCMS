namespace WebWayCMS.Forms;

/// <summary>
/// The single view model every form component receives. Carries the property metadata,
/// current value, rendering mode, and naming hints.
/// </summary>
public sealed class FormFieldContext
{
    /// <summary>
    /// The form property metadata.
    /// </summary>
    public required FormPropertyInfo Property { get; init; }

    /// <summary>
    /// The current value of the property on the model instance.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Write (edit) or Read (display) mode.
    /// </summary>
    public FormFieldMode Mode { get; init; } = FormFieldMode.Write;

    /// <summary>
    /// The HTML input name attribute value, e.g. "Title".
    /// Empty when <see cref="JsonBound"/> is true (the field uses <c>data-prop</c> instead).
    /// </summary>
    public string InputName { get; init; } = string.Empty;

    /// <summary>
    /// The HTML element id attribute value.
    /// </summary>
    public string ElementId { get; init; } = string.Empty;

    /// <summary>
    /// When true, the field emits a <c>data-prop</c> attribute and no <c>name</c> attribute,
    /// for use with JSON-serialized configuration sub-forms.
    /// </summary>
    public bool JsonBound { get; init; }

    // ─── Convenience properties delegating to Property ──────────────────────

    public string Name => Property.Name;
    public string Label => Property.Label;
    public string HelpText => Property.HelpText;
    public string Placeholder => Property.Placeholder;
    public bool IsRequired => Property.IsRequired;
    public double? Min => Property.Min;
    public double? Max => Property.Max;
    public int? MaxLength => Property.MaxLength;
    public string Pattern => Property.Pattern;
    public string PatternErrorMessage => Property.PatternErrorMessage;
    public Dictionary<string, string> DropdownOptions => Property.DropdownOptions;
    public string EntityType => Property.EntityType;
    public string ViewComponentName => Property.ViewComponentName;

    /// <summary>
    /// The value formatted as a string using <see cref="FormValueFormatter"/>.
    /// </summary>
    public string StringValue => FormValueFormatter.Format(Value);
}
