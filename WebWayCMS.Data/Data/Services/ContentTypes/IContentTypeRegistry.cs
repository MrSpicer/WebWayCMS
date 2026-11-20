namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// Every content type registered with a content store in this host (CMS and host types), in registration order.
/// The in-memory source of truth the registration table is synced from.
/// </summary>
public interface IContentTypeRegistry
{
    IReadOnlyList<ContentTypeDescriptor> All { get; }

    /// <summary>The content type registered under <paramref name="key"/> (case-insensitive), or null.</summary>
    ContentTypeDescriptor? Get(string? key);
}
