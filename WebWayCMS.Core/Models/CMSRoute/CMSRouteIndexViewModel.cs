using WebWayCMS.Models.CMSRoute;

namespace WebWayCMS.Models.CMSRoute;

public sealed class CMSRouteIndexViewModel
{
    public List<CMSRouteItemViewModel> Routes { get; init; } = new();
}

public sealed class CMSRouteItemViewModel
{
    public Guid Id { get; init; }
    public Guid MasterId { get; init; }
    public int Version { get; init; }
    public string Pattern { get; init; } = string.Empty;
    public string? OwningContentType { get; init; }
    public bool IsPublished { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
}
