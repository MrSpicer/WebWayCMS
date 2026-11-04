namespace WebWayCMS.Data.Services;

/// <summary>
/// A page-set history entry as a read filter. A node in <paramref name="GovernedNodeIds"/> reads only a
/// version in <paramref name="VersionIds"/> (or nothing, which hides a zone item added after the
/// snapshot); any other node reads its published version.
/// </summary>
public sealed record ContentReadSnapshot(Guid[] GovernedNodeIds, Guid[] VersionIds);
