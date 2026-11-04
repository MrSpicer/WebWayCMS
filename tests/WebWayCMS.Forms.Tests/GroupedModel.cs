using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Two grouped sections plus an ungrouped trailing field.</summary>
public sealed class GroupedModel
{
    [FormProperty(Group = "First", Order = 1)]
    public string A { get; set; } = "a";

    [FormProperty(Group = "Second", Order = 2)]
    public string B { get; set; } = "b";

    [FormProperty(Order = 3)]
    public string C { get; set; } = "c";
}
