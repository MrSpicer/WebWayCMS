using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class ContentZoneWidgetRegistry : IContentZoneWidgetRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _map;

    public ContentZoneWidgetRegistry(IReadOnlyDictionary<string, Type> map)
        => _map = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>
    /// Builds the registry by scanning the given assemblies for Blazor components decorated with
    /// <see cref="ContentZoneComponentAttribute"/> (the built-in CMS widgets and any host-provided
    /// widgets in the entry assembly), keyed by their resolved component name.
    /// </summary>
    public static ContentZoneWidgetRegistry FromAssemblies(IEnumerable<Assembly> assemblies)
        => new(BuildMap(assemblies));

    public Type? Resolve(string componentName)
        => _map.TryGetValue(componentName, out var type) ? type : null;

    private static IReadOnlyDictionary<string, Type> BuildMap(IEnumerable<Assembly> assemblies)
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, attribute) in ComponentTypeScanner.Scan<ContentZoneComponentAttribute>(assemblies))
            map[ContentZoneComponentNaming.ResolveName(type, attribute)] = type;
        return map;
    }
}
