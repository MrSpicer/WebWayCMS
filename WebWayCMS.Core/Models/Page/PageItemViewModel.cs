namespace WebWayCMS.Models.Page;

public sealed class PageItemViewModel
{
    public Guid Id { get; init; }
    public Guid MasterId { get; init; }
    public int Version { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string ControllerName { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
}
