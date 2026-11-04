using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Non-minimum DateOnly / DateTimeOffset values for the formatted paths.</summary>
public sealed class TemporalModel
{
    [FormProperty(EditorType = EditorType.Date)]
    public DateOnly Day { get; set; } = new(2024, 5, 6);

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTimeOffset Moment { get; set; } = new(2024, 5, 6, 7, 8, 0, TimeSpan.Zero);
}
