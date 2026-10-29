using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Route;

namespace WebWayCMS.Models.Page;

public sealed class PageUpsertViewModel : BaseContentViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public string? ParentRoutePrefix { get; set; }

    /// <summary>
    /// The parent page (a containment; empty for a root page). Changing it moves the page: a draft until
    /// publish, which re-derives the page's URL and its live descendants'.
    /// </summary>
    [ContentRelation("pages", Kind = ContentRelationKind.Containment)]
    [FormProperty(Label = "Parent Page", Order = 3,
        HelpText = "Leave empty for a top-level page. A move takes effect when the page is published.")]
    public Guid? ParentNodeId { get; set; }

    [Required]
    [FormProperty(Label = "Page Controller", EditorType = EditorType.PageControllerPicker, IsRequired = true, Order = 4,
        HelpText = "The page type determines what the page renders and what configuration options are available.",
        FormComponent = "PageControllerPicker")]
    public string ControllerName { get; set; } = string.Empty;

    [FormProperty(
    Label = "View Name",
    HelpText = "The view template to use. Leave empty for default behavior.",
    Placeholder = "e.g., Default",
    EditorType = EditorType.ViewPicker,
    ViewComponentName = "Page",
    Order = 90,
    FormComponent = "ViewPicker")]
    public string? ViewName { get; set; }

    /// <summary>
    /// Hidden field that carries the serialized page-type configuration JSON.
    /// Populated by <c>PageUpsert.js</c> on form submit from the dynamic config fields
    /// rendered inside <c>#configurationArea</c>. The server deserializes this into the
    /// controller's declared <see cref="WebWayCMS.Attributes.PageControllerAttribute.ConfigurationType"/>.
    /// </summary>
    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>The route items owned by this page; a new page mints one Derived route.</summary>
    [ContentRelation(RouteModel.ContentTypeKey, Kind = ContentRelationKind.Owned)]
    [FormProperty(Label = "Routes", Order = 5)]
    public List<Guid> RouteIds { get; set; } = [];
}