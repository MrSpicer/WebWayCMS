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
