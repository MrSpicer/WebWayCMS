using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Page;

public sealed class PageUpsertViewModel : BaseContentViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public string? ParentRoutePrefix { get; set; }

    [Required]
    [FormProperty(Label = "Page Controller", EditorType = EditorType.PageControllerPicker, IsRequired = true, Order = 3,
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
}