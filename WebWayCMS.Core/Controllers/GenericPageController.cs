using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers;

namespace WebWayCMS.Controllers;

/// <summary>
/// Configuration model for generic pages.
/// </summary>
public class GenericPageConfiguration
{
    [FormProperty("Custom CSS", EditorType.TextArea,
        HelpText = "Inline CSS styles to be injected into the page head. Do not include <style> tags.",
        Order = 10,
        FormComponent = "TextArea")]
    public string Style { get; set; } = string.Empty;

    [FormProperty("Meta Tags", EditorType.TextArea,
        HelpText = "HTML meta tags for SEO and social media. Include full tags, e.g., <meta name=\"description\" content=\"...\">",
        Order = 30,
        FormComponent = "TextArea")]
    public string Meta { get; set; } = string.Empty;
}

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
            return Task.FromResult<IActionResult>(View(viewName, PageConfig));

        return Task.FromResult<IActionResult>(View(PageConfig));
    }
}
