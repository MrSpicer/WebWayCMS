using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Route;

/// <summary>Editable fields of a route item (owned by its content type's <c>routes</c> relation).</summary>
public sealed class RouteUpsertViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public Guid? NodeId { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public int? ExpectedVersionNumber { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public bool IsPublished { get; set; }

    [FormProperty(Label = "Mode", EditorType = EditorType.Dropdown,
        DropdownOptions = "Derived:Derived from the owner's slug,Explicit:A fixed pattern",
        Order = 1, FormComponent = "Dropdown",
        HelpText = "Derived follows the owner's slug and parent. Explicit keeps the pattern you type.")]
    public RouteMode Mode { get; set; }

    [FormProperty(Label = "Pattern", EditorType = EditorType.Text, Order = 2, FormComponent = "Text",
        HelpText = "Used when Mode is Explicit, e.g. /documentation. Ignored for Derived routes.")]
    public string Pattern { get; set; } = string.Empty;

    [FormProperty(Label = "Navigation Name", EditorType = EditorType.Text, Order = 3, FormComponent = "Text",
        HelpText = "Link text in navigation widgets. Empty hides the route from them.")]
    public string? NavigationName { get; set; }

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 4, FormComponent = "Number")]
    public int Order { get; set; }
}
