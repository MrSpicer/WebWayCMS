namespace WebWayCMS.Forms;

/// <summary>
/// Controls how form field values are bound.
/// </summary>
public enum FormFieldBinding
{
    /// <summary>The field emits a <c>name</c> attribute for standard model binding.</summary>
    Model,

    /// <summary>The field emits a <c>data-prop</c> attribute and no <c>name</c>,
    /// for JSON-serialized configuration sub-forms.</summary>
    Json
}
