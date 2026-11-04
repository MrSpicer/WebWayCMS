namespace WebWayCMS.Data.Models;

/// <summary>
/// One entry in a page's page-set history: the exact versions of a page and every active zone item in
/// its zone tree (nested zones included), recorded by <c>IPageHistoryRecorder</c> after every change to
/// the page set (<see cref="PageSetSnapshotKind.Draft"/>) and after every publish
/// (<see cref="PageSetSnapshotKind.Published"/>). Versions are never rewritten, so an entry stays
/// faithful; a version an entry references cannot be deleted on its own.
/// </summary>
public record PageSetSnapshot
{
    public Guid Id { get; set; }

    /// <summary>The page's <see cref="ContentNode.Id"/>.</summary>
    public Guid PageNodeId { get; set; }

    /// <summary>The page version the entry captured.</summary>
    public Guid PageVersionId { get; set; }

    public PageSetSnapshotKind Kind { get; set; }

    /// <summary>What the change was, e.g. "Added ContentBlock to Main" or "Published".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// <c>PageSetEquality.ContentHash</c> of the captured state (content only, no version ids), used to
    /// skip an entry identical to the previous one.
    /// </summary>
    public string StateHash { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public Guid? CreatedBy { get; set; }

    /// <summary>The change set that produced this entry.</summary>
    public Guid ChangeSetId { get; set; }

    public int ItemCount { get; set; }

    public List<PageSetSnapshotItem> Items { get; set; } = new();
}
