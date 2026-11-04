using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>The final property keeps GroupWithNext set, exercising the loop's no-break exit.</summary>
public sealed class HorizontalGroupModel
{
    [FormProperty(GroupWithNext = true, Order = 1)]
    public string Left { get; set; } = "l";

    [FormProperty(GroupWithNext = true, Order = 2)]
    public string Right { get; set; } = "r";
}
