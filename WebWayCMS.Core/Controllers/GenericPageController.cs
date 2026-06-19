using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using WebWayCMS.Rendering;
using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers;

/// <summary>
/// Configuration model for generic pages.
/// </summary>
public class GenericPageConfiguration
{
    [FormProperty("View Name", EditorType.Text,
        HelpText = "Override the view template for this page's JSON config block. Leave empty for default behavior.",
        Order = 0)]
    public string ViewName { get; set; } = string.Empty;

    [FormProperty("Custom CSS", EditorType.TextArea,
        HelpText = "Inline CSS styles to be injected into the page head. Do not include <style> tags.",
        Order = 10)]
    public string Style { get; set; } = string.Empty;

    [FormProperty("Custom JavaScript", EditorType.TextArea,
        HelpText = "Inline JavaScript to be injected at the bottom of the page. Do not include <script> tags.",
        Order = 20)]
    public string Script { get; set; } = string.Empty;

    [FormProperty("Meta Tags", EditorType.TextArea,
        HelpText = "HTML meta tags for SEO and social media. Include full tags, e.g., <meta name=\"description\" content=\"...\">",
        Order = 30)]
    public string Meta { get; set; } = string.Empty;
}

/// <summary>
/// A generic page controller for rendering simple content pages.
/// </summary>
[PageController(
    DisplayName = "Generic Page",
    Description = "A simple page with configurable heading and content",
    Category = "General",
    Order = 0)]
public class GenericPageController : PageControllerBase<GenericPageConfiguration>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<GenericPageController>();
    private readonly ICmsPageRenderer _renderer;

    public GenericPageController(ICmsPageRenderer renderer)
        => _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public override Task<IActionResult> Index()
    {
        _logger.Information("Rendering generic page: {PageId} - {PageTitle}",
            CurrentPage?.ContentMeta.Id,
            CurrentPage?.ContentMeta.Title);

        // The page (and its content zones) are rendered as Blazor SSR components via the
        // Presentation layer; routing/config resolution stays in the MVC pipeline.
        return Task.FromResult(_renderer.RenderPage(CurrentPage, PageConfig));
    }
}
