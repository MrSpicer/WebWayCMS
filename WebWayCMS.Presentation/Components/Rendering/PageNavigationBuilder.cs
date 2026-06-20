using WebWayCMS.Models.Page;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Builds the filtered page-navigation tree from the raw page index, mirroring the rules of the
/// legacy PageViewComponent. Pure and static so the filtering and recursion are unit-testable
/// without rendering a component.
/// </summary>
public static class PageNavigationBuilder
{
    public static List<PageNavigationItem> Build(List<PageTreeNode> nodes, PageContentZoneConfiguration config)
    {
        var filtered = nodes
            .Where(n => n.PageId.HasValue)
            .Where(n => config.ShowDraftPages || n.IsPublished)
            .Where(n => config.ShowHiddenPages || !n.IsHidden);

        filtered = config.AdminPages
            ? filtered.Where(n => n.Route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            : filtered.Where(n => !n.Route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase));

        return filtered
            .Select(n => new PageNavigationItem
            {
                Title = n.Title,
                Route = n.Route,
                IsPublished = n.IsPublished,
                Children = Build(n.Children, config),
            })
            .ToList();
    }
}
