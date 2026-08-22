using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.CMSRoute;

public sealed class CMSRouteUpsertViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid? Id { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid? OwningContentNodeId { get; set; }

    [Required]
    [FormProperty(Label = "Pattern", EditorType = EditorType.Text, IsRequired = true, Order = 2,
        Placeholder = "/about or /blog/{slug}",
        HelpText = "The URL route pattern. Supports {parameter}, {parameter?} (optional), {parameter:regex(...)}, and {**slug} (catch-all).",
        FormComponent = "Text")]
    public string Pattern { get; set; } = string.Empty;

    [MaxLength(CMSRouteDTO.NavigationNameMaxLength)]
    [FormProperty(Label = "Navigation Name", EditorType = EditorType.Text, Order = 3,
        Placeholder = "About Us",
        HelpText = "Link text for this route in navigation widgets. Routes with no navigation name are not shown.",
        MaxLength = CMSRouteDTO.NavigationNameMaxLength, FormComponent = "Text")]
    public string? NavigationName { get; set; }

    [FormProperty(Label = "Defaults", EditorType = EditorType.TextArea, Order = 4,
        HelpText = "JSON of default route values, e.g. {\"controller\":\"GenericPage\",\"action\":\"Index\"}.",
        FormComponent = "TextArea")]
    public string DefaultsJson { get; set; } = "{}";

    [FormProperty(Label = "Constraints", EditorType = EditorType.TextArea, Order = 5,
        HelpText = "JSON of route constraints, e.g. {\"slug\":\"regex([a-z0-9-]+)\"}.",
        FormComponent = "TextArea")]
    public string ConstraintsJson { get; set; } = "{}";

    [FormProperty(Label = "Data Tokens", EditorType = EditorType.TextArea, Order = 6,
        HelpText = "JSON of additional route data tokens.",
        FormComponent = "TextArea")]
    public string DataTokensJson { get; set; } = "{}";

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 7,
        HelpText = "Route precedence. Lower numbers are matched first.",
        FormComponent = "Number")]
    public int Order { get; set; }

    [FormProperty(Label = "Reserved", EditorType = EditorType.Checkbox, Order = 8,
        HelpText = "Reserved routes block other routes from using this pattern, but do not route themselves.",
        FormComponent = "Checkbox")]
    public bool IsReserved { get; set; }

    [FormProperty(Label = "Owning Content Type", EditorType = EditorType.Text, Order = 100,
        HelpText = "The content type that owns this route (e.g. Page, ArticleWidget).",
        FormComponent = "Text")]
    public string? OwningContentType { get; set; }
}
