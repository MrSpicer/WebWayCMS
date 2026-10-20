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
public class ImageViewComponent : WidgetViewComponent
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
        var vm = config?.ImageNodeId is Guid imageNodeId && imageNodeId != Guid.Empty
            ? await _model.GetViewModelByNodeIdAsync(imageNodeId, CancellationToken.None)
            : null;

        if (vm == null)
        {
            // No image picked yet (a freshly dropped widget) or the image was deleted. The zone
            // editor shows its edit view's "No image selected" placeholder; public pages render
            // nothing rather than throwing.
            return RenderModeFor(config) == WidgetRenderMode.Edit
                ? ModeView(config, new ImageViewModel())
                : Content(string.Empty);
        }

        vm.CssClass = config!.CssClass;
        vm.ShowCaption = config.ShowCaption;

        return ModeView(config, vm);
    }
}
