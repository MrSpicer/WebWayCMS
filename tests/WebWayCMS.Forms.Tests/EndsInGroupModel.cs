using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>The final property belongs to a group, so a group div stays open at the end.</summary>
public sealed class EndsInGroupModel
{
    [FormProperty(Group = "Only", Order = 1)]
    public string A { get; set; } = "a";

    [FormProperty(Group = "Only", Order = 2)]
    public string B { get; set; } = "b";
}
