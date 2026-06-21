using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Models;
using WebWayCMS.Presentation.Components;
using WebWayCMS.Rendering;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Renders CMS pages through the <see cref="CmsPageHost"/> Blazor root component, returning a
/// <see cref="RazorComponentResult{TComponent}"/> wrapped so MVC controllers can return it.
/// </summary>
public sealed class CmsPageRenderer : ICmsPageRenderer
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CmsPageRenderer(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    public IActionResult RenderPage(PageDTO? page, object config)
    {
        var subRoute = _httpContextAccessor.HttpContext?.Items["CMS:SubRoute"] as string;

        var parameters = new Dictionary<string, object?>
        {
            [nameof(CmsPageHost.Page)] = page,
            [nameof(CmsPageHost.Config)] = config,
            [nameof(CmsPageHost.SubRoute)] = subRoute,
        };

        return new ResultActionResult(new RazorComponentResult<CmsPageHost>(parameters));
    }

    public IActionResult RenderAdminPage(PageDTO? page, object config, string? viewName)
    {
        var subRoute = _httpContextAccessor.HttpContext?.Items["CMS:SubRoute"] as string;

        var parameters = new Dictionary<string, object?>
        {
            [nameof(AdminPageHost.Page)] = page,
            [nameof(AdminPageHost.Config)] = config,
            [nameof(AdminPageHost.SubRoute)] = subRoute,
            [nameof(AdminPageHost.ViewName)] = viewName,
        };

        return new ResultActionResult(new RazorComponentResult<AdminPageHost>(parameters));
    }
}
