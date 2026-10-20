using System.Collections.Concurrent;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

using WebWayCMS.Attributes;

namespace WebWayCMS.Services;

/// <summary>
/// Service for discovering available views for ViewComponents and controllers.
/// Combines two sources so the result is correct in both debug and Release/Docker builds:
/// compiled Razor views registered as application parts (the only source available when views
/// are compiled into assemblies and no .cshtml files exist on disk), and a filesystem scan of
/// the standard ASP.NET view locations (so freshly-added .cshtml files appear in development
/// without a rebuild).
///
/// Widget view files are named <c>{mode}.{variant}.cshtml</c> (mode lowercase, e.g.
/// <c>view.TwoColumnEqual.cshtml</c>, <c>edit.default.cshtml</c>). Un-prefixed files are ignored:
/// they are either host-internal states or pre-render-mode leftovers.
///
/// Component views are discovered only where the Razor view engine looks for a (non-area) view
/// component's views — <c>/Views/Shared/Components/{name}/</c> in a compiled application part or
/// under the content root — because widget rendering trusts a discovery hit and renders it with no
/// further fallback. A view under <c>Areas/*</c> or a sibling folder would be reported but could
/// never load.
///
/// Each component's scan is cached for the instance's lifetime. The host registers one shared
/// instance outside Development (views are compiled, so the set cannot change) and one per request
/// in Development (so freshly-added .cshtml files show up without a restart).
/// </summary>
public sealed class ViewDiscoveryService : IViewDiscoveryService
{
    private readonly IWebHostEnvironment _env;
    private readonly ApplicationPartManager _apm;
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ViewDiscoveryService>();

    // Widget view resolution calls TryResolveView several times per widget render, and a page
    // renders many widgets, so without this every call would rescan the disk and the compiled
    // views. Concurrent because the production registration shares one instance across requests.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _componentViewCache = new(StringComparer.OrdinalIgnoreCase);

    public ViewDiscoveryService(IWebHostEnvironment env, ApplicationPartManager apm)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _apm = apm ?? throw new ArgumentNullException(nameof(apm));
    }

    /// <summary>
    /// Gets the variant names of the views available to the specified ViewComponent for the
    /// specified render mode, unioned from compiled application parts (for Release/Docker) and the
    /// filesystem (for development). Only files named <c>{mode}.{variant}.cshtml</c> are listed;
    /// the returned names are the variant portion with the mode prefix stripped (case-insensitive
    /// match, so <c>view.TwoColumnEqual</c> lists as <c>TwoColumnEqual</c>). Un-prefixed files and
    /// <c>_</c> partials are ignored.
    /// </summary>
    public IReadOnlyList<string> GetAvailableViews(string componentName, WidgetRenderMode mode)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return Array.Empty<string>();
        }

        var prefix = mode.ToFilePrefix() + ".";
        var variants = DiscoverViewFileNames(componentName)
            .Values
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[prefix.Length..])
            .Where(variant => variant.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.Debug("Discovered {Count} {Mode} views for ViewComponent '{ComponentName}': {Views}",
            variants.Count, mode, componentName, string.Join(", ", variants));

        return variants;
    }

    /// <summary>
    /// Resolves a (component, mode, variant) triple to the actual view file name to render (e.g.
    /// <c>view.TwoColumnEqual</c>), matching case-insensitively against the discovered file names.
    /// Case-insensitive matching returns the real casing on disk, which protects against
    /// case-sensitive Linux filesystem lookups when views are runtime-compiled. Returns false when
    /// no view file with that exact mode+variant exists (falling back between variants is the
    /// caller's job, see <c>IWidgetViewResolver</c>).
    /// </summary>
    public bool TryResolveView(string componentName, WidgetRenderMode mode, string? variant, out string? viewName)
    {
        viewName = null;
        if (string.IsNullOrWhiteSpace(componentName))
            return false;

        var normalizedVariant = string.IsNullOrWhiteSpace(variant) || string.Equals(variant, "default", StringComparison.OrdinalIgnoreCase)
            ? "default"
            : variant;

        var target = mode.ToFilePrefix() + "." + normalizedVariant;
        return DiscoverViewFileNames(componentName).TryGetValue(target, out viewName);
    }

    public IReadOnlyList<string> GetControllerViews(string controllerName)
    {
        if (string.IsNullOrWhiteSpace(controllerName))
            return Array.Empty<string>();

        var views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentRoot = _env.ContentRootPath;

        // Compiled views from application parts (works in Release/Docker where no files exist on disk)
        AddCompiledControllerViews(controllerName, views);

        ScanControllerDirectory(Path.Combine(contentRoot, "Views", controllerName), views);

        var parentDir = Directory.GetParent(contentRoot);
        if (parentDir != null)
        {
            foreach (var siblingDir in Directory.GetDirectories(parentDir.FullName))
            {
                if (siblingDir.Equals(contentRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                ScanControllerDirectory(Path.Combine(siblingDir, "Views", controllerName), views);
            }
        }

        var result = views.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.Debug("Discovered {Count} controller views for '{ControllerName}': {Views}",
            result.Count, controllerName, string.Join(", ", result));
        return result;
    }

    private void ScanControllerDirectory(string directoryPath, HashSet<string> views)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.GetFiles(directoryPath, "*.cshtml", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Skip files that start with underscore (partials/layouts)
                if (!fileName.StartsWith("_"))
                {
                    views.Add(fileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error scanning directory {Directory} for controller views", directoryPath);
        }
    }

    /// <summary>
    /// Discovers every view file name (without .cshtml) in the component's view locations, unioning
    /// compiled application parts (for Release/Docker) and the content root on disk (for development
    /// with runtime compilation). Only <c>/Views/Shared/Components/{componentName}/</c> is searched
    /// (see the type documentation). The result is keyed case-insensitively and maps a normalized
    /// name to the real casing on disk. Partials/layouts (names starting with '_') are excluded.
    /// </summary>
    private IReadOnlyDictionary<string, string> DiscoverViewFileNames(string componentName)
        => _componentViewCache.GetOrAdd(componentName, ScanComponentViews);

    private IReadOnlyDictionary<string, string> ScanComponentViews(string componentName)
    {
        var views = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Compiled views from application parts (works in Release/Docker where no files exist on
        // disk). The full path must match: an Areas/{area}/ prefix is not a non-area location.
        foreach (var segments in EnumerateCompiledViewSegments())
        {
            if (segments.Length == 5 && TailMatches(segments, "Views", "Shared", "Components", componentName))
                AddViewName(segments[^1], views);
        }

        ScanDirectory(Path.Combine(_env.ContentRootPath, "Views", "Shared", "Components", componentName), views);

        return views;
    }

    /// <summary>
    /// Adds compiled view names for a controller by inspecting the relative paths of all compiled
    /// Razor views registered as application parts. Matches paths whose tail is
    /// <c>Views/{controllerName}/{viewName}.cshtml</c> (with an optional <c>Areas/{area}/</c> prefix).
    /// Partials/layouts (names starting with '_') are skipped.
    /// </summary>
    private void AddCompiledControllerViews(string controllerName, HashSet<string> views)
    {
        foreach (var segments in EnumerateCompiledViewSegments())
        {
            if (TailMatches(segments, "Views", controllerName))
                AddViewName(segments[^1], views);
        }
    }

    /// <summary>
    /// Adds a controller view name to a set, skipping partials/layouts whose names start with '_'.
    /// </summary>
    private static void AddViewName(string fileSegment, HashSet<string> views)
    {
        var fileName = Path.GetFileNameWithoutExtension(fileSegment);
        if (!fileName.StartsWith('_'))
            views.Add(fileName);
    }

    /// <summary>
    /// Returns true when the segments immediately preceding the final (file) segment match
    /// <paramref name="expected"/> case-insensitively. The final segment is the view file itself,
    /// so a path matches when its tail is <c>{expected...}/{file}</c> (with any leading segments,
    /// e.g. an <c>Areas/{area}/</c> prefix, allowed).
    /// </summary>
    private static bool TailMatches(string[] segments, params string[] expected)
    {
        // Need expected segments plus the trailing file segment.
        if (segments.Length < expected.Length + 1)
            return false;

        var offset = segments.Length - 1 - expected.Length;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!segments[offset + i].Equals(expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Enumerates the path segments of every compiled Razor view registered with the application
    /// part manager (e.g. "/Views/Home/Index.cshtml" -> ["Views", "Home", "Index.cshtml"]).
    /// </summary>
    private IEnumerable<string[]> EnumerateCompiledViewSegments()
    {
        var feature = new ViewsFeature();
        _apm.PopulateFeature(feature);

        foreach (var descriptor in feature.ViewDescriptors)
        {
            yield return descriptor.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    /// <summary>
    /// Adds the view name from a "{viewName}.cshtml" file segment to the set, skipping partials and
    /// layouts whose names start with '_'.
    /// </summary>
    private static void AddViewName(string fileSegment, Dictionary<string, string> views)
    {
        var fileName = Path.GetFileNameWithoutExtension(fileSegment);
        if (fileName.StartsWith('_'))
            return;

        // Case-insensitive key, value keeps the real casing for disk lookup.
        views.TryAdd(fileName, fileName);
    }

    private void ScanDirectory(string directoryPath, Dictionary<string, string> views)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.GetFiles(directoryPath, "*.cshtml", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Skip files that start with underscore (partials/layouts)
                if (!fileName.StartsWith("_"))
                {
                    views.TryAdd(fileName, fileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error scanning directory {Directory} for ViewComponent views", directoryPath);
        }
    }
}
