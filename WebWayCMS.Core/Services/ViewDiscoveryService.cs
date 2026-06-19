using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

namespace WebWayCMS.Services;

/// <summary>
/// Service for discovering available views for ViewComponents and controllers.
/// Combines two sources so the result is correct in both debug and Release/Docker builds:
/// compiled Razor views registered as application parts (the only source available when views
/// are compiled into assemblies and no .cshtml files exist on disk), and a filesystem scan of
/// the standard ASP.NET view locations (so freshly-added .cshtml files appear in development
/// without a rebuild).
/// </summary>
public sealed class ViewDiscoveryService : IViewDiscoveryService
{
    private readonly IWebHostEnvironment _env;
    private readonly ApplicationPartManager _apm;
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ViewDiscoveryService>();

    public ViewDiscoveryService(IWebHostEnvironment env, ApplicationPartManager apm)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _apm = apm ?? throw new ArgumentNullException(nameof(apm));
    }

    /// <summary>
    /// Gets a list of available view names for the specified ViewComponent, unioned from compiled
    /// application parts (for Release/Docker) and the filesystem (for development). Matches:
    /// - Views/Shared/Components/{componentName}/*.cshtml
    /// - Areas/*/Views/Shared/Components/{componentName}/*.cshtml
    /// </summary>
    public IReadOnlyList<string> GetAvailableViews(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return Array.Empty<string>();
        }

        var views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentRoot = _env.ContentRootPath;

        // Compiled views from application parts (works in Release/Docker where no files exist on disk)
        AddCompiledComponentViews(componentName, views);

        // Standard ViewComponent view locations
        var searchPaths = new[]
        {
            // Main project: Views/Shared/Components/{componentName}
            Path.Combine(contentRoot, "Views", "Shared", "Components", componentName),
            
            // Areas: Areas/*/Views/Shared/Components/{componentName}
            // We'll scan for area directories
        };

        // Scan main locations
        foreach (var searchPath in searchPaths)
        {
            ScanDirectory(searchPath, views);
        }

        // Scan areas
        var areasPath = Path.Combine(contentRoot, "Areas");
        if (Directory.Exists(areasPath))
        {
            foreach (var areaDir in Directory.GetDirectories(areasPath))
            {
                var areaComponentPath = Path.Combine(areaDir, "Views", "Shared", "Components", componentName);
                ScanDirectory(areaComponentPath, views);
            }
        }

        // Also scan class library projects (e.g., WebWayCMS)
        // Look for sibling directories that might contain the CMS library
        var parentDir = Directory.GetParent(contentRoot);
        if (parentDir != null)
        {
            foreach (var siblingDir in Directory.GetDirectories(parentDir.FullName))
            {
                // Skip the current project
                if (siblingDir.Equals(contentRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check for Views/Shared/Components in sibling projects
                var siblingComponentPath = Path.Combine(siblingDir, "Views", "Shared", "Components", componentName);
                ScanDirectory(siblingComponentPath, views);

                // Check areas in sibling projects
                var siblingAreasPath = Path.Combine(siblingDir, "Areas");
                if (Directory.Exists(siblingAreasPath))
                {
                    foreach (var areaDir in Directory.GetDirectories(siblingAreasPath))
                    {
                        var areaComponentPath = Path.Combine(areaDir, "Views", "Shared", "Components", componentName);
                        ScanDirectory(areaComponentPath, views);
                    }
                }
            }
        }

        var result = views.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        
        _logger.Debug("Discovered {Count} views for ViewComponent '{ComponentName}': {Views}", 
            result.Count, componentName, string.Join(", ", result));

        return result;
    }

    public IReadOnlyList<string> GetControllerViews(string controllerName)
    {
        if (string.IsNullOrWhiteSpace(controllerName))
            return Array.Empty<string>();

        var views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentRoot = _env.ContentRootPath;

        // Compiled views from application parts (works in Release/Docker where no files exist on disk)
        AddCompiledControllerViews(controllerName, views);

        ScanDirectory(Path.Combine(contentRoot, "Views", controllerName), views);

        var parentDir = Directory.GetParent(contentRoot);
        if (parentDir != null)
        {
            foreach (var siblingDir in Directory.GetDirectories(parentDir.FullName))
            {
                if (siblingDir.Equals(contentRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                ScanDirectory(Path.Combine(siblingDir, "Views", controllerName), views);
            }
        }

        var result = views.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.Debug("Discovered {Count} controller views for '{ControllerName}': {Views}",
            result.Count, controllerName, string.Join(", ", result));
        return result;
    }

    /// <summary>
    /// Adds compiled view names for a ViewComponent by inspecting the relative paths of all compiled
    /// Razor views registered as application parts. Matches paths whose tail is
    /// <c>Views/Shared/Components/{componentName}/{viewName}.cshtml</c> (with an optional
    /// <c>Areas/{area}/</c> prefix). Partials/layouts (names starting with '_') are skipped.
    /// </summary>
    private void AddCompiledComponentViews(string componentName, HashSet<string> views)
    {
        // Tail: Views / Shared / Components / {component} / {file}.cshtml
        foreach (var segments in EnumerateCompiledViewSegments())
        {
            if (TailMatches(segments, "Views", "Shared", "Components", componentName))
                AddViewName(segments[^1], views);
        }
    }

    /// <summary>
    /// Adds compiled view names for a controller by inspecting the relative paths of all compiled
    /// Razor views registered as application parts. Matches paths whose tail is
    /// <c>Views/{controllerName}/{viewName}.cshtml</c> (with an optional <c>Areas/{area}/</c> prefix).
    /// Partials/layouts (names starting with '_') are skipped.
    /// </summary>
    private void AddCompiledControllerViews(string controllerName, HashSet<string> views)
    {
        // Tail: Views / {controller} / {file}.cshtml
        foreach (var segments in EnumerateCompiledViewSegments())
        {
            if (TailMatches(segments, "Views", controllerName))
                AddViewName(segments[^1], views);
        }
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
    private static void AddViewName(string fileSegment, HashSet<string> views)
    {
        var fileName = Path.GetFileNameWithoutExtension(fileSegment);
        if (!fileName.StartsWith('_'))
            views.Add(fileName);
    }

    private void ScanDirectory(string directoryPath, HashSet<string> views)
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
            _logger.Warning(ex, "Error scanning directory {Directory} for ViewComponent views", directoryPath);
        }
    }
}
