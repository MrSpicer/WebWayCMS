using Microsoft.AspNetCore.Mvc;
using WebWayCMS.Attributes;
using WebWayCMS.Models.Page;

namespace WebWayCMS.ViewComponents;

[ContentZoneComponent(
    DisplayName = "Page Navigation",
    Description = "Renders navigation links for published CMS pages.",
    Category = "Navigation",
    ConfigurationType = typeof(PageContentZoneConfiguration),
    IconClass = "fa-sitemap",
    Order = 10
)]
public class PageViewComponent : ViewComponent
{
    private readonly IPageModel _model;

    public PageViewComponent(IPageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public async Task<IViewComponentResult> InvokeAsync(PageContentZoneConfiguration? config = null)
    {
        config ??= new PageContentZoneConfiguration();
        var index = await _model.GetPageIndexAsync();
        var items = MapNodes(index.Pages, config);
        var viewName = config.ViewName ?? "Default";
        return View(viewName, new PageNavigationViewModel { Items = items });
    }

    private static List<PageNavigationItem> MapNodes(List<PageTreeNode> nodes, PageContentZoneConfiguration config)
    {
        var filteredNodes = nodes
            .Where(n => n.PageId.HasValue)
            .Where(n => config.ShowDraftPages || n.IsPublished)
            .Where(n => config.ShowHiddenPages || !n.IsHidden);

        if (config.AdminPages)
        {
            filteredNodes = filteredNodes.Where(n => n.Route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            filteredNodes = filteredNodes.Where(n => !n.Route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase));
        }
        
        return filteredNodes
            .Select(n => new PageNavigationItem
            {
                Title = n.Title,
                Route = n.Route,
                IsPublished = n.IsPublished,
                Children = MapNodes(n.Children, config)
            })
            .ToList();
    }
}
