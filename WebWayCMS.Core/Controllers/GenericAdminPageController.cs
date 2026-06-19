using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
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

    public GenericAdminPageController()
    {
    }

    public override Task<IActionResult> Index()
    {
        _logger.Information("Rendering generic admin page: {PageId} - {PageTitle}",
            CurrentPage?.ContentMeta.Id,
            CurrentPage?.ContentMeta.Title);

        var viewName = CurrentPage?.ViewName;
        if (!string.IsNullOrWhiteSpace(viewName))
            return Task.FromResult<IActionResult>(View(viewName, PageConfig));

        return Task.FromResult<IActionResult>(View(PageConfig));
    }
}
