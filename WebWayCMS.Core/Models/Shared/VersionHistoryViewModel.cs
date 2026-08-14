using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

public sealed class VersionHistoryViewModel
{
    public string ContentType { get; init; } = string.Empty;
    public Guid NodeId { get; init; }
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
    public string? AuthorName { get; init; }
    public Guid? CreatedBy { get; init; }
    public string? ChangeNote { get; init; }
    public ContentVersionState State { get; init; }
    public Guid ChangeSetId { get; init; }
    public DateTime CreationDate { get; init; }
    public DateTime ModificationDate { get; init; }
    public bool IsPublished { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsLatest { get; init; }
}
