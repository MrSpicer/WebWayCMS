namespace WebWayCMS.Models.Shared;

public sealed class VersionHistoryViewModel
{
    public string ContentType { get; init; } = string.Empty;
    public Guid MasterId { get; init; }
    public string ItemTitle { get; init; } = string.Empty;
    public string BackUrl { get; init; } = string.Empty;
    public string? ParentKey { get; init; }
    public string? ChildType { get; init; }
    public List<VersionItemViewModel> Versions { get; init; } = new();
}

public sealed class VersionItemViewModel
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
    public bool IsPublished { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsLatest { get; init; }
}