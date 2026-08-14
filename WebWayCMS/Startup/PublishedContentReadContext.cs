using WebWayCMS.Data.Services;

namespace WebWayCMS.Startup;

/// <summary>
/// The rendering-only read context. Hard-codes <see cref="ContentReadMode.Published"/> so a
/// rendering-only host is physically incapable of serving a draft.
/// </summary>
internal sealed class PublishedContentReadContext : IContentReadContext
{
    public ContentReadMode Mode => ContentReadMode.Published;
    public string Culture => string.Empty;
    public string Segment => string.Empty;
}
