using System.Reflection;

using Microsoft.AspNetCore.Components;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Scans assemblies for Blazor component types decorated with a given attribute. Shared by the CMS
/// extension-point registries (chrome, page views, content-zone views, widgets) that discover
/// host-provided components by convention from the entry assembly. Assemblies that fail to load their
/// types are skipped rather than aborting the scan (mirrors the existing CMS registries).
/// </summary>
internal static class ComponentTypeScanner
{
    public static IEnumerable<(Type Type, TAttribute Attribute)> Scan<TAttribute>(IEnumerable<Assembly> assemblies)
        where TAttribute : Attribute
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!type.IsClass || type.IsAbstract || !typeof(IComponent).IsAssignableFrom(type))
                    continue;

                var attribute = type.GetCustomAttribute<TAttribute>();
                if (attribute is not null)
                    yield return (type, attribute);
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (Exception ex)
        {
            // Some assemblies may not be fully loadable; skip them (mirrors PageControllerRegistry /
            // ContentZoneComponentRegistry).
            System.Diagnostics.Debug.WriteLine($"Failed to scan assembly {assembly.FullName}: {ex.Message}");
            return Array.Empty<Type>();
        }
    }
}
