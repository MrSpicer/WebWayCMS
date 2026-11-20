namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// Collects the <see cref="ContentTypeDescriptor"/> singletons. Two descriptors sharing a key throw when the
/// registry is first resolved (the same rule as two admin handlers sharing a content type key).
/// </summary>
public sealed class ContentTypeRegistry : IContentTypeRegistry
{
    private readonly Dictionary<string, ContentTypeDescriptor> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public ContentTypeRegistry(IEnumerable<ContentTypeDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var all = new List<ContentTypeDescriptor>();
        foreach (var descriptor in descriptors)
        {
            if (!_byKey.TryAdd(descriptor.Key, descriptor))
                throw new InvalidOperationException(
                    $"Content type key '{descriptor.Key}' is registered twice ({_byKey[descriptor.Key].ClrType.FullName} and {descriptor.ClrType.FullName}).");
            all.Add(descriptor);
        }
        All = all;
    }

    public IReadOnlyList<ContentTypeDescriptor> All { get; }

    public ContentTypeDescriptor? Get(string? key)
        => key != null && _byKey.TryGetValue(key, out var descriptor) ? descriptor : null;
}
