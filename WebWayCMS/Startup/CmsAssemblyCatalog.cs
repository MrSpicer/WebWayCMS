using System.Reflection;

namespace WebWayCMS.Startup;

/// <summary>
/// Holds the host assemblies contributed via <c>IWebWayCmsBuilder.AddApplicationAssembly</c>.
/// Registered as a singleton so downstream startup steps (the seeders and MVC application-part
/// wiring) can discover host-defined content types without touching CMS source.
/// </summary>
internal sealed class CmsAssemblyCatalog
{
    public CmsAssemblyCatalog(IReadOnlyList<Assembly> assemblies) => Assemblies = assemblies;

    public IReadOnlyList<Assembly> Assemblies { get; }
}
