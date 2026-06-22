using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class ContentZoneViewRegistry : IContentZoneViewRegistry
{
    private readonly KeyedViewMap _map = new();

    /// <summary>
    /// Scans the given assemblies for components decorated with <see cref="ContentZoneViewAttribute"/>,
    /// keyed by <see cref="ContentZoneViewAttribute.ForComponent"/>.
    /// </summary>
    public ContentZoneViewRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var (type, attribute) in ComponentTypeScanner.Scan<ContentZoneViewAttribute>(assemblies))
            _map.Add(attribute.ForComponent, attribute.Name, type);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetComponentViews(string componentName) => _map.Views(componentName);

    /// <inheritdoc />
    public Type? Resolve(string componentName, string viewName) => _map.Resolve(componentName, viewName);
}
