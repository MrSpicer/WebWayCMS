using WebWayCMS.Attributes;

namespace WebWayCMS.Routing.Tests;

public sealed class NoDefaultCtorConfig
{
    [FormProperty]
    public string Value { get; set; }

    public NoDefaultCtorConfig(string value) => Value = value;
}
