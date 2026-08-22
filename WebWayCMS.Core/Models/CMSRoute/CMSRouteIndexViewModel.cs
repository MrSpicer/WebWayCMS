namespace WebWayCMS.Models.CMSRoute;

public sealed class CMSRouteIndexViewModel
{
    public List<CMSRouteItemViewModel> Routes { get; init; } = new();
}

public sealed class CMSRouteItemViewModel
{
    public Guid Id { get; init; }
    public string Pattern { get; init; } = string.Empty;
    public string? NavigationName { get; init; }
    public string? OwningContentType { get; init; }
    public bool IsReserved { get; init; }
}
