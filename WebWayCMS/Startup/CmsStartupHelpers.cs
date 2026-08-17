using Microsoft.Extensions.DependencyInjection;

namespace WebWayCMS.Startup;

internal static class CmsStartupHelpers
{
    internal static string GetControllerName(Type type)
    {
        const string suffix = "Controller";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }

    internal static bool IsSkipped(string envVar)
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(envVar),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the assembly list a seeder scans: the CMS core assemblies, then the host's entry
    /// assembly, then any host assemblies contributed via <c>IWebWayCmsBuilder.AddApplicationAssembly</c>,
    /// distinct and null-free.
    /// </summary>
    internal static IEnumerable<System.Reflection.Assembly> SeedAssemblies(
        IServiceProvider services,
        params System.Reflection.Assembly[] coreAssemblies)
    {
        var catalog = services.GetService<CmsAssemblyCatalog>();
        return CombineAssemblies(coreAssemblies, System.Reflection.Assembly.GetEntryAssembly(), catalog?.Assemblies);
    }

    internal static IEnumerable<System.Reflection.Assembly> CombineAssemblies(
        IEnumerable<System.Reflection.Assembly> core,
        System.Reflection.Assembly? entry,
        IEnumerable<System.Reflection.Assembly>? host)
    {
        var assemblies = core;
        if (entry != null)
            assemblies = assemblies.Append(entry);
        if (host != null)
            assemblies = assemblies.Concat(host);

        return assemblies.Distinct();
    }
}
