using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>Fields tagged per widget display mode, plus an untagged field shared by both.</summary>
public sealed class DisplayModeModel
{
    [FormProperty(Label = "Single Only", DisplayModes = WidgetDisplayMode.Single, Order = 1)]
    public string? SingleOnly { get; set; }

    [FormProperty(Label = "List Only", DisplayModes = WidgetDisplayMode.List, Order = 2)]
    public string? ListOnly { get; set; }

    [FormProperty(Label = "Shared", Order = 3)]
    public string? Shared { get; set; }
}
