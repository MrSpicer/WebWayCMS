namespace WebWayCMS.Data.Models;

/// <summary>Whether a page-history entry records the page's draft or what went live.</summary>
public enum PageSetSnapshotKind
{
    /// <summary>The current drafts after a change (a save, add, remove, move, revert, discard…).</summary>
    Draft = 0,

    /// <summary>What was live right after a publish.</summary>
    Published = 1
}
