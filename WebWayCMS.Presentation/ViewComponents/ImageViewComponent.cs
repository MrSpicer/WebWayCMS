using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Image;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Renders an image content item by node ID.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Image",
    Description = "Renders an image from the CMS media library.",
    Category = "Media",
    ConfigurationType = typeof(ImageContentZoneConfiguration),
    IconClass = "fa-image",
    Order = 5
)]
public class ImageViewComponent : ViewComponent
{
    private readonly IImageModel _model;

    public ImageViewComponent(IImageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <remarks>
    /// Exactly one parameter, nullable with a default. The zone renderer passes the deserialized
    /// configuration as the whole arguments object, which binds directly only when the component
    /// takes a single assignable parameter; any other shape gets shredded into a property
    /// dictionary instead. The default also keeps the component renderable when no configuration
    /// type is registered yet.
    /// </remarks>
    public async Task<IViewComponentResult> InvokeAsync(ImageContentZoneConfiguration? config = null)
    {
        // This also renders live inside the admin inline editor, where a freshly dropped widget has
        // no configuration yet — degrade to nothing rather than throwing.
        if (config?.ImageNodeId == null || config.ImageNodeId == Guid.Empty)
            return Content(string.Empty);

        var vm = await _model.GetViewModelByNodeIdAsync(config.ImageNodeId.Value, CancellationToken.None);
        if (vm == null)
            return Content(string.Empty);

        vm.CssClass = config.CssClass;
        vm.ShowCaption = config.ShowCaption;

        return View(string.IsNullOrWhiteSpace(config.ViewName) ? "Default" : config.ViewName, vm);
    }
}
