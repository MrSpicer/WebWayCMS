using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Default/empty values to exercise the "empty string" formatting paths.</summary>
public sealed class DefaultValuesModel
{
    [FormProperty(EditorType = EditorType.Guid)]
    public Guid EmptyGuid { get; set; } = Guid.Empty;

    [FormProperty(EditorType = EditorType.Date)]
    public DateTime MinDate { get; set; } = DateTime.MinValue;

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTime MinMoment { get; set; } = DateTime.MinValue;

    [FormProperty(EditorType = EditorType.Date)]
    public DateOnly DateOnlyValue { get; set; } = DateOnly.MinValue;

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTimeOffset MinOffset { get; set; } = DateTimeOffset.MinValue;

    [FormProperty(EditorType = EditorType.Text)]
    public NullStringType? Weird { get; set; } = new NullStringType();
}
