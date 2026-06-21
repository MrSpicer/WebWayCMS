using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using WebWayCMS.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers;

/// <summary>
/// A generic page controller for rendering admin-only content pages.
/// </summary>
[Authorize(Roles = "Admin")]
[PageController(
    DisplayName = "Generic Admin Page",
    Description = "A simple admin-only page with configurable heading and content",
    Category = "General",
    Order = 1)]
public class GenericAdminPageController : PageControllerBase<GenericPageConfiguration>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<GenericAdminPageController>();
    private readonly ICmsPageRenderer _renderer;

    public GenericAdminPageController(ICmsPageRenderer renderer)
        => _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public override Task<IActionResult> Index()
    {
        _logger.Information("Rendering generic admin page: {PageId} - {PageTitle}",
            CurrentPage?.ContentMeta.Id,
            CurrentPage?.ContentMeta.Title);

        // The admin page (and its content zones) renders as Blazor SSR via the Presentation layer.
        return Task.FromResult(_renderer.RenderAdminPage(CurrentPage, PageConfig, CurrentPage?.ViewName));
    }
}
