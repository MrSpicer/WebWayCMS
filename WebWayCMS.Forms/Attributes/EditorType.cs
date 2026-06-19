namespace WebWayCMS.Attributes;

/// <summary>
/// Defines the type of editor to use for a content zone configuration property.
/// </summary>
public enum EditorType
{
    /// <summary>
    /// Single-line text input.
    /// </summary>
    Text,

    /// <summary>
    /// Multi-line text area.
    /// </summary>
    TextArea,

    /// <summary>
    /// Rich text / HTML editor.
    /// </summary>
    RichText,

    /// <summary>
    /// Numeric input.
    /// </summary>
    Number,

    /// <summary>
    /// Checkbox for boolean values.
    /// </summary>
    Checkbox,

    /// <summary>
    /// GUID input with optional entity picker.
    /// </summary>
    Guid,

    /// <summary>
    /// Dropdown select from predefined options.
    /// </summary>
    Dropdown,

    /// <summary>
    /// Date picker.
    /// </summary>
    Date,

    /// <summary>
    /// Date and time picker.
    /// </summary>
    DateTime,

    /// <summary>
    /// Color picker.
    /// </summary>
    Color,

    /// <summary>
    /// URL input with validation.
    /// </summary>
    Url,

    /// <summary>
    /// Email input with validation.
    /// </summary>
    Email,

    /// <summary>
    /// Dropdown populated with available views for a ViewComponent.
    /// Requires ViewComponentName to be specified in the FormPropertyAttribute.
    /// </summary>
    ViewPicker,

    /// <summary>
    /// Dropdown populated at runtime with the registered page controllers.
    /// Rendered as an empty select (marked with a <c>data-page-controller-picker</c> attribute)
    /// whose options are loaded client-side from the page controller registry; mirrors
    /// <see cref="ViewPicker"/>, which is likewise populated by client-side script.
    /// </summary>
    PageControllerPicker,

    /// <summary>
    /// Hidden field (not displayed in form but included in configuration).
    /// </summary>
    Hidden
}
