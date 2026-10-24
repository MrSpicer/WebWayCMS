using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

public sealed class VersionHistoryViewModel
{
    public string ContentType { get; init; } = string.Empty;
    public Guid NodeId { get; init; }
    public string ItemTitle { get; init; } = string.Empty;
    public string BackUrl { get; set; } = string.Empty;

    /// <summary>
    /// A local URL the history page was opened from (e.g. the page zone editor). It replaces
    /// <see cref="BackUrl"/> and is carried through the page's restore/delete forms.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>The current draft's version number (a restore's expected version), when known.</summary>
    public int? CurrentVersionNumber { get; init; }

    public string? ParentKey { get; init; }
    public string? ChildType { get; init; }

    /// <summary>
    /// True when the rows include other items' versions (a container's page lists the widgets inside it):
    /// the page shows a Widget column, and each row's restore expects its own item's current version.
    /// </summary>
    public bool IsCombined { get; init; }

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
    /// <summary>True for the newest version of the row's own item.</summary>
    public bool IsLatest { get; init; }

    /// <summary>The item the version belongs to (a combined history mixes several).</summary>
    public Guid NodeId { get; init; }

    /// <summary>The item's label in a combined history.</summary>
    public string? WidgetLabel { get; init; }

    /// <summary>The row's item's current draft version number (its restore's expected version), when known.</summary>
    public int? NodeCurrentVersionNumber { get; init; }
}
