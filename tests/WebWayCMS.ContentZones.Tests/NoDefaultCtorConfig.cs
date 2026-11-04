using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones.Tests;

/// <summary>A configuration type with no parameterless constructor (CreateDefaultConfiguration fails).</summary>
public sealed class NoDefaultCtorConfig
{
    [FormProperty]
    public string Value { get; set; }

    public NoDefaultCtorConfig(string value) => Value = value;
}
