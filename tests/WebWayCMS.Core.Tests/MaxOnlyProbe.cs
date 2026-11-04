using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class MaxOnlyProbe
{
    [FormProperty(Max = 5)]
    public int Count { get; set; } = 3;
}
