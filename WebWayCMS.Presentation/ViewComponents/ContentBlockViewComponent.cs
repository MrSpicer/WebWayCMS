using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Models.ContentBlock;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Renders a reusable HTML content block by node ID.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Content Block",
    Description = "Renders a reusable HTML content block from the CMS.",
    Category = "Content",
    ConfigurationType = typeof(ContentBlockContentZoneConfiguration),
    IconClass = "fa-file-alt",
    Order = 1
)]
public class ContentBlockViewComponent : WidgetViewComponent
{
    private readonly IContentBlockModel _model;

    public ContentBlockViewComponent(IContentBlockModel model)
    {
        _model = model;
    }

    public async Task<IViewComponentResult> InvokeAsync(ContentBlockContentZoneConfiguration config)
    {
        if (config == null || config.ContentBlockID == Guid.Empty)
        {
            // No block picked yet: the zone editor shows its edit view's "No content" placeholder;
            // public pages render nothing.
            return RenderModeFor(config) == WidgetRenderMode.Edit
                ? ModeView(config, new ContentBlockViewModel())
                : Content(string.Empty);
        }

        var vm = await _model.GetViewModelByNodeIdAsync(config.ContentBlockID, CancellationToken.None);
        return ModeView(config, vm ?? new ContentBlockViewModel { NodeId = config.ContentBlockID });
    }
}
