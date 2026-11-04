using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

public sealed class FieldViewNameModel
{
    [FormProperty(Label = "Compact", FieldViewName = "Compact")]
    public string? Compact { get; set; }

    [FormProperty(Label = "Plain")]
    public string? Plain { get; set; }

    public string? NoAttribute { get; set; }
}
