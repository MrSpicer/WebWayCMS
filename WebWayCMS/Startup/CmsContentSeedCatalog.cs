using System.Reflection;

namespace WebWayCMS.Startup;

/// <summary>
/// Holds the content seed files and assemblies contributed via
/// <c>IWebWayCmsBuilder.AddContentSeedFile</c> / <c>AddContentSeedAssembly</c>. Registered as a
/// singleton so the JSON content seeder's source providers can discover them at startup.
/// </summary>
internal sealed class CmsContentSeedCatalog
{
    public CmsContentSeedCatalog(IReadOnlyList<string> files, IReadOnlyList<Assembly> assemblies)
    {
        Files = files;
        Assemblies = assemblies;
    }

    public IReadOnlyList<string> Files { get; }

    public IReadOnlyList<Assembly> Assemblies { get; }
}
