using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Discovers content seed files from two disk sources: every <c>*.json</c> file under the configured
/// directory (resolved against the host's content root), plus the explicitly registered files. A
/// missing configured directory is empty, not an error; individual IO failures are logged and skipped.
/// </summary>
public sealed class FileContentSeedSourceProvider : IContentSeedSourceProvider
{
    private readonly IWebHostEnvironment _env;
    private readonly ContentSeedOptions _options;
    private readonly IReadOnlyList<string> _explicitFiles;
    private readonly ILogger _logger = Log.ForContext<FileContentSeedSourceProvider>();

    public FileContentSeedSourceProvider(
        IWebHostEnvironment env,
        IOptions<ContentSeedOptions> options,
        IReadOnlyList<string> explicitFiles)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _explicitFiles = explicitFiles ?? throw new ArgumentNullException(nameof(explicitFiles));
    }

    public IEnumerable<ContentSeedSource> GetSources()
    {
        // A blank path would scan the whole content root (appsettings.json, etc.); treat it as
        // "no directory to scan", mirroring the assembly provider's blank ResourceSuffix handling.
        if (!string.IsNullOrWhiteSpace(_options.Path))
        {
            var directory = Path.Combine(_env.ContentRootPath, _options.Path);

            if (Directory.Exists(directory))
            {
                var files = SafeGetFiles(directory);
                foreach (var file in files)
                {
                    var source = ReadFile(file);
                    if (source != null)
                        yield return source;
                }
            }
        }

        foreach (var explicitFile in _explicitFiles)
        {
            var source = ReadFile(ResolvePath(explicitFile));
            if (source != null)
                yield return source;
        }
    }

    private string[] SafeGetFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error scanning directory {Directory} for content seed files", directory);
            return Array.Empty<string>();
        }
    }

    private string ResolvePath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(_env.ContentRootPath, path);

    private ContentSeedSource? ReadFile(string path)
    {
        try
        {
            return new ContentSeedSource(path, File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read content seed file '{Path}'", path);
            return null;
        }
    }
}
