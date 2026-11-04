using WebWayCMS.Attributes;

namespace WebWayCMS.Core.Tests;

public sealed class UnanchoredProbe
{
    [FormProperty(Pattern = "[a-z0-9-]+")]
    public string? Slug { get; set; }
}
