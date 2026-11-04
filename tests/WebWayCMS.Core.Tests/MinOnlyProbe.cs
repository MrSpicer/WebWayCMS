using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class MinOnlyProbe
{
    [FormProperty(Min = 5)]
    public int Count { get; set; } = 7;
}
