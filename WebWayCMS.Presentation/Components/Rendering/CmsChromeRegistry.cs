using System.Reflection;

using WebWayCMS.Attributes;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class CmsChromeRegistry : ICmsChromeRegistry
{
    /// <inheritdoc />
    public Type? ChromeType { get; }

    /// <summary>
    /// Scans the given assemblies for a component decorated with <see cref="CmsChromeAttribute"/>.
    /// When several are found the one with the lowest <see cref="CmsChromeAttribute.Order"/> wins,
    /// ties broken by full type name for determinism.
    /// </summary>
    public CmsChromeRegistry(IEnumerable<Assembly> assemblies)
    {
        ChromeType = ComponentTypeScanner.Scan<CmsChromeAttribute>(assemblies)
            .OrderBy(x => x.Attribute.Order)
            .ThenBy(x => x.Type.FullName, StringComparer.Ordinal)
            .Select(x => x.Type)
            .FirstOrDefault();
    }
}
