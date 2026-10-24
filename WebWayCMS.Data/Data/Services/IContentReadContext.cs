namespace WebWayCMS.Data.Services;

public enum ContentReadMode
{
    Published = 0,
    Draft = 1,

    /// <summary>
    /// Reads a page-set history entry: nodes the snapshot governs read exactly the captured versions,
    /// every other node reads <see cref="Published"/>. Requires <see cref="IContentReadContext.Snapshot"/>.
    /// </summary>
    Snapshot = 2
}

/// <summary>
/// Describes which versions a read should select. Registered per deployment mode: a rendering-only
/// host resolves a hard-coded <see cref="ContentReadMode.Published"/> context, while an admin host
/// resolves a preview-aware context that can serve drafts (and page-set history snapshots) to
/// authenticated editors.
/// </summary>
public interface IContentReadContext
{
    ContentReadMode Mode { get; }
    string Culture { get; }
    string Segment { get; }

    /// <summary>The versions a <see cref="ContentReadMode.Snapshot"/> read selects; null otherwise.</summary>
    ContentReadSnapshot? Snapshot => null;
}

/// <summary>
/// A page-set history entry as a read filter. A node in <paramref name="GovernedNodeIds"/> reads only a
/// version in <paramref name="VersionIds"/> (or nothing, which hides a zone item added after the
/// snapshot); any other node reads its published version.
/// </summary>
public sealed record ContentReadSnapshot(Guid[] GovernedNodeIds, Guid[] VersionIds);
