namespace WebWayCMS.Models.Page;

public sealed class PageNavigationItem
{
    public string Title { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public List<PageNavigationItem> Children { get; init; } = new();
}