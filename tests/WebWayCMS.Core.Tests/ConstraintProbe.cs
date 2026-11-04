using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class ConstraintProbe
{
    [FormProperty(Min = 1, Max = 10)]
    public int Count { get; set; }

    [FormProperty(MaxLength = 5)]
    public string? Short { get; set; }

    [FormProperty(Pattern = "^[a-z]+$", PatternErrorMessage = "Lowercase only.")]
    public string? Code { get; set; }
}
