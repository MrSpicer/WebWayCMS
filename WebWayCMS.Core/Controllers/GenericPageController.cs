using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.Controllers;

/// <summary>
/// A generic page controller for rendering simple content pages.
/// </summary>
[PageController(
    DisplayName = "Generic Page",
    Description = "A simple page with configurable heading and content",
    Category = "General",
    ConfigurationType = typeof(GenericPageConfiguration),
    Order = 0)]
public class GenericPageController : PageControllerBase<GenericPageConfiguration>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<GenericPageController>();

    public GenericPageController()
    {
    }

    public override Task<IActionResult> Index()
    {
        _logger.Information("Rendering generic page: {PageId} - {PageTitle}",
            CurrentPage?.Version.Node.Id,
            CurrentPage?.Version.Title);

        var viewName = CurrentPage?.ViewName;
        if (!string.IsNullOrWhiteSpace(viewName) && !string.Equals(viewName, "Default", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IActionResult>(View(viewName, PageConfig.SanitizedForRender()));

        return Task.FromResult<IActionResult>(View(PageConfig.SanitizedForRender()));
    }
}
