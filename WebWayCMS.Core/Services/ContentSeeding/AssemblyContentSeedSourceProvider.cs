using System.Reflection;

using Microsoft.Extensions.Options;

using Serilog;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Discovers content seed files embedded as manifest resources in the scanned assemblies. A resource
/// qualifies when its name ends in <see cref="ContentSeedOptions.ResourceSuffix"/> (case-insensitive).
/// </summary>
public sealed class AssemblyContentSeedSourceProvider : IContentSeedSourceProvider
{
    private readonly IEnumerable<Assembly> _assemblies;
    private readonly ContentSeedOptions _options;
    private readonly ILogger _logger = Log.ForContext<AssemblyContentSeedSourceProvider>();

    public AssemblyContentSeedSourceProvider(IEnumerable<Assembly> assemblies, IOptions<ContentSeedOptions> options)
    {
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public IEnumerable<ContentSeedSource> GetSources()
    {
        var suffix = _options.ResourceSuffix ?? string.Empty;

        // An empty suffix would match every embedded resource; treat it as "no embedded seed files".
        if (string.IsNullOrWhiteSpace(suffix))
            yield break;

        foreach (var assembly in _assemblies)
        {
            string[] resourceNames;
            try
            {
                resourceNames = assembly.GetManifestResourceNames();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to enumerate embedded resources of assembly {Assembly}", assembly.FullName);
                continue;
            }

            foreach (var name in resourceNames)
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null)
                {
                    _logger.Warning("Embedded content seed resource '{Resource}' could not be opened.", name);
                    continue;
                }

                using var reader = new StreamReader(stream);
                yield return new ContentSeedSource(name, reader.ReadToEnd());
            }
        }
    }
}
