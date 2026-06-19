using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;
using WebWayCMS.Models.ContentBlock;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Renders a reusable HTML content block by ID.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Content Block",
    Description = "Renders a reusable HTML content block from the CMS.",
    Category = "Content",
    ConfigurationType = typeof(ContentBlockContentZoneConfiguration),
    IconClass = "fa-file-alt",
    Order = 1
)]
public class ContentBlockViewComponent : ViewComponent
{
    private readonly IContentBlockModel _model;

    public ContentBlockViewComponent(IContentBlockModel model)
    {
        _model = model;
    }

    public async Task<IViewComponentResult> InvokeAsync(ContentBlockContentZoneConfiguration config)
    {
        if (config == null || config.ContentBlockID == Guid.Empty)
            return Content(string.Empty);

        var vm = await _model.GetViewModelByMasterIdAsync(config.ContentBlockID, CancellationToken.None);
        return View(vm ?? new ContentBlockViewModel { Id = config.ContentBlockID });
    }
}