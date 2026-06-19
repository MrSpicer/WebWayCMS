namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class ContentZoneWidgetRegistry : IContentZoneWidgetRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _map;

    public ContentZoneWidgetRegistry(IReadOnlyDictionary<string, Type> map)
        => _map = map ?? throw new ArgumentNullException(nameof(map));

    public Type? Resolve(string componentName)
        => _map.TryGetValue(componentName, out var type) ? type : null;
}
