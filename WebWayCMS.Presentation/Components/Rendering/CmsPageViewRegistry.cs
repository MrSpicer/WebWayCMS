using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class CmsPageViewRegistry : ICmsPageViewRegistry
{
    private readonly KeyedViewMap _map = new();

    /// <summary>
    /// Scans the given assemblies for components decorated with <see cref="CmsPageViewAttribute"/>,
    /// keyed by <see cref="CmsPageViewAttribute.ForController"/>.
    /// </summary>
    public CmsPageViewRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var (type, attribute) in ComponentTypeScanner.Scan<CmsPageViewAttribute>(assemblies))
            _map.Add(attribute.ForController, attribute.Name, type);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetControllerViews(string controllerName) => _map.Views(controllerName);

    /// <inheritdoc />
    public Type? Resolve(string controllerName, string viewName) => _map.Resolve(controllerName, viewName);
}
