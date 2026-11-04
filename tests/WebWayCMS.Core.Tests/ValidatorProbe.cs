using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class ValidatorProbe
{
    [FormProperty(IsRequired = true)]
    public Guid? Optional { get; set; }

    [FormProperty(IsRequired = true)]
    public int Count { get; set; } = 5;
}
