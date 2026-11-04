namespace WebWayCMS.Data.Services;

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
