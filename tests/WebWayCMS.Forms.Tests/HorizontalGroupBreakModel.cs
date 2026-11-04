using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>A horizontal group that ends with a non-grouped field (exercises the break path).</summary>
public sealed class HorizontalGroupBreakModel
{
    [FormProperty(GroupWithNext = true, Order = 1)]
    public string Left { get; set; } = "l";

    [FormProperty(GroupWithNext = false, Order = 2)]
    public string Right { get; set; } = "r";
}
