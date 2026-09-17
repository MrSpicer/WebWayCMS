using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using WebWayCMS.Data.Services;
using WebWayCMS.Security;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Resolves <c>@media:&lt;path&gt;</c> tokens in a seed item's field overlay by loading the referenced
/// image file, storing it in the media library, and substituting the resulting content hash.
/// </summary>
/// <remarks>
/// Mirrors <see cref="SeedReferenceResolver"/>: substitution is a plain string replace, so a token
/// works as a whole field value or embedded in a serialized-JSON string. The path is resolved
/// relative to the seed file's own directory, or — for an embedded seed resource — as a manifest
/// resource of the same assembly.
/// <para>
/// Images are content-addressed, so re-running a seed that references the same file stores nothing
/// new; it resolves to the hash already in the library.
/// </para>
/// </remarks>
public sealed class SeedMediaResolver
{
    /// <summary>The literal token prefix <c>@media:</c>.</summary>
    public const string TokenPrefix = "@media:";

    private static readonly Regex TokenPattern = new(@"@media:([^\s""']+)", RegexOptions.Compiled);

    private readonly IMediaLibrary _mediaLibrary;
    private readonly long _maxBytes;

    public SeedMediaResolver(IMediaLibrary mediaLibrary, long maxBytes)
    {
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
        _maxBytes = maxBytes;
    }

    /// <summary>Every distinct media path referenced by a string value anywhere in the tree.</summary>
    public static IReadOnlyCollection<string> CollectReferences(JsonNode? node)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, paths);
        return paths;
    }

    /// <summary>
    /// Loads and stores each referenced file, then replaces its token with the stored content hash.
    /// Returns the paths that could not be resolved; the overlay is left untouched for those.
    /// </summary>
    public async Task<IReadOnlyList<string>> SubstituteAsync(
        JsonObject overlay, ContentSeedSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(source);

        var references = CollectReferences(overlay);
        if (references.Count == 0)
            return [];

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new List<string>();

        foreach (var path in references)
        {
            var bytes = ReadBytes(path, source);
            if (bytes == null)
            {
                unresolved.Add(path);
                continue;
            }

            // Seeded files go through exactly the same validation as an admin upload.
            var validation = ImageValidator.Validate(bytes, _maxBytes);
            if (!validation.Success)
            {
                unresolved.Add(path);
                continue;
            }

            var added = await _mediaLibrary.AddAsync(
                bytes, validation.ContentType!, Path.GetFileName(path), validation.Width, validation.Height, ct);

            if (!added.Success || added.Blob == null)
            {
                unresolved.Add(path);
                continue;
            }

            resolved[path] = added.Blob.Hash;
        }

        Substitute(overlay, resolved);
        return unresolved;
    }

    /// <summary>Reads the referenced file from disk or from the seed source's assembly.</summary>
    internal static byte[]? ReadBytes(string path, ContentSeedSource source)
    {
        if (source.Assembly != null)
            return ReadEmbedded(path, source.Assembly);

        var directory = Path.GetDirectoryName(source.Name);
        if (string.IsNullOrEmpty(directory))
            return null;

        var full = Path.GetFullPath(Path.Combine(directory, path));

        // Never let a seed file reach outside its own directory.
        if (!full.StartsWith(Path.GetFullPath(directory), StringComparison.Ordinal))
            return null;

        return TryReadFile(full);
    }

    /// <remarks>
    /// The catch arms need a real IO fault (a disappearing file, a permission change) to exercise,
    /// which a unit test cannot arrange reliably; a seeding run must not crash on one.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static byte[]? TryReadFile(string full)
    {
        try
        {
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <remarks>
    /// Marked excluded for the same reason as <see cref="TryReadFile"/>: the failure arms need a
    /// dynamic or unloadable assembly to reach, which a unit test cannot arrange.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static byte[]? ReadEmbedded(string path, Assembly assembly)
    {
        // Embedded resource names use dots for separators, so match on the tail rather than
        // reconstructing the (root-namespace dependent) full name.
        var suffix = path.Replace('/', '.').Replace('\\', '.');

        string? name;
        try
        {
            name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (name == null)
            return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Collect(JsonNode? node, HashSet<string> paths)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prop in obj)
                    Collect(prop.Value, paths);
                break;
            case JsonArray array:
                foreach (var element in array)
                    Collect(element, paths);
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var text))
                {
                    foreach (Match match in TokenPattern.Matches(text))
                        paths.Add(match.Groups[1].Value);
                }
                break;
        }
    }

    private static JsonNode? Substitute(JsonNode? node, IReadOnlyDictionary<string, string> resolved)
    {
        switch (node)
        {
            // Only write back when substitution actually produced a new node: re-assigning the
            // same instance into its own slot throws "the node already has a parent".
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    var replacement = Substitute(obj[key], resolved);
                    if (!ReferenceEquals(replacement, obj[key]))
                        obj[key] = replacement;
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var replacement = Substitute(array[i], resolved);
                    if (!ReferenceEquals(replacement, array[i]))
                        array[i] = replacement;
                }
                return array;
            case JsonValue value:
                if (value.TryGetValue<string>(out var text))
                {
                    return JsonValue.Create(TokenPattern.Replace(text, match =>
                        resolved.TryGetValue(match.Groups[1].Value, out var hash) ? hash : match.Value));
                }
                return value;
            default:
                return node;
        }
    }
}
