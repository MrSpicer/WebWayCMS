namespace WebWayCMS.Models.Page;

public sealed class PageItemViewModel
{
    public Guid NodeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
}
