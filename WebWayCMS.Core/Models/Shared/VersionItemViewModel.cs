using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

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

    /// <summary>
    /// The history row for <paramref name="version"/>; <paramref name="maxVersion"/> is the newest version number
    /// of the same item. A combined history also names the row's widget and its current version.
    /// </summary>
    public static VersionItemViewModel From(
        IVersionedContent version, int maxVersion, string title, string? widgetLabel = null, int? nodeCurrentVersionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        var v = version.Version;
        return new VersionItemViewModel
        {
            Id = version.VersionId,
            Version = v.VersionNumber,
            Title = title,
            CreationDate = v.Node.CreatedUtc,
            ModificationDate = v.CreatedUtc,
            IsPublished = v.State == ContentVersionState.Published,
            IsDeleted = v.Node.IsDeleted,
            IsLatest = v.VersionNumber == maxVersion,
            CreatedBy = v.CreatedBy,
            ChangeNote = v.ChangeNote,
            State = v.State,
            ChangeSetId = v.ChangeSetId,
            NodeId = v.Node.Id,
            WidgetLabel = widgetLabel,
            NodeCurrentVersionNumber = nodeCurrentVersionNumber,
        };
    }
}
