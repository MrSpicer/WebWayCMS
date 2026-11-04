using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class UnconstrainedProbe
{
    [FormProperty(Pattern = "^[a-z]+$")]
    public string? Anything { get; set; }
}
