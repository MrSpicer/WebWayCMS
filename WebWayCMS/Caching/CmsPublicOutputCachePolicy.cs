using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;
using WebWayCMS.Mcp;

namespace WebWayCMS.Caching;

/// <summary>
/// The one output cache policy: anonymous 200 GET/HEAD responses on public paths, stored for
/// <see cref="CacheOptions.OutputCacheExpiration"/>. The cache key carries the cross-host
/// generation stamp (<c>VaryByValues["gen"]</c>), so a publish on any host moves the key and the
/// old responses stop matching; <see cref="CmsCacheInvalidation"/> additionally evicts the
/// <see cref="Tag"/> locally on every bump.
/// Excluded paths never enter the cache: the admin area (<c>/wadmin</c>, segment boundary, like
/// Presentation's internal <c>AdminPathPrefix</c>), the draft preview (<c>/_preview</c>), Identity
/// (per-user), <c>/api</c> (per-user JSON), the MCP endpoint (bearer tokens), <c>/media</c>
/// (immutable bytes with ETag), and <c>/.well-known</c> (discovery). A response is stored only if
/// it is 200, sets no <c>Set-Cookie</c>, is not marked <c>no-store</c>/<c>private</c>, was not
/// turned into a draft or snapshot read while it rendered (the route transformer marks those
/// after <see cref="CacheRequestAsync"/> ran), and the generation did not move meanwhile.
/// </summary>
public sealed class CmsPublicOutputCachePolicy : IOutputCachePolicy
{
    /// <summary>The tag every stored public response carries, evicted on a generation bump.</summary>
    public const string Tag = "cms-public";

    internal const string GenerationItemKey = "CmsCache:Generation";
    private const string AdminPrefix = "/wadmin";

    private readonly ICmsCacheGeneration _generation;
    private readonly CacheOptions _options;
    private readonly string _mcpPath;

    public CmsPublicOutputCachePolicy(
        ICmsCacheGeneration generation,
        IOptions<CacheOptions> options,
        IOptions<McpOptions> mcpOptions)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _mcpPath = mcpOptions?.Value.Path ?? throw new ArgumentNullException(nameof(mcpOptions));
    }

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken = default)
    {
        var request = context.HttpContext.Request;
        var cacheable = (request.Method == HttpMethods.Get || request.Method == HttpMethods.Head)
            && !context.HttpContext.User.Identity!.IsAuthenticated
            && !request.Headers.ContainsKey("Authorization")
            && !IsExcludedPath(request.Path);
        context.EnableOutputCaching = cacheable;
        // The middleware decides lookup and body capture right after this call: without both flags a
        // response is neither served from the cache nor buffered for storing (ServeResponseAsync can
        // still veto the store, it cannot start a capture).
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        if (cacheable)
        {
            var generation = _generation.Key;
            context.HttpContext.Items[GenerationItemKey] = generation;
            context.CacheVaryByRules.VaryByValues["gen"] = generation;
            // The catch-all route puts the page path in the query-less slug, but admin pages pass
            // ?role= and friends: vary on the whole query string.
            context.CacheVaryByRules.QueryKeys = "*";
            context.Tags.Add(Tag);
            context.ResponseExpirationTimeSpan = _options.OutputCacheExpiration;
        }
        return default;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken = default)
    {
        context.AllowCacheLookup = context.EnableOutputCaching;
        return default;
    }

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken = default)
    {
        var httpContext = context.HttpContext;
        var items = httpContext.Items!;
        var cacheable = context.EnableOutputCaching
            && httpContext.Response.StatusCode == StatusCodes.Status200OK
            && !httpContext.Response.Headers.ContainsKey("Set-Cookie")
            && !HasNoStoreOrPrivate(httpContext.Response.Headers["Cache-Control"].ToString())
            && items.TryGetValue(GenerationItemKey, out var stored)
            && stored is string generation
            && _generation.Key == generation
            && items[PreviewConstants.DraftRequestItemKey] is not true
            && items[PreviewConstants.SnapshotRequestItemKey] is not ContentReadSnapshot;
        context.AllowCacheStorage = cacheable;
        return default;
    }

    private static bool HasNoStoreOrPrivate(string cacheControl)
        => (cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)
            || cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase));

    private bool IsExcludedPath(PathString path)
        => OnSegment(path, AdminPrefix)
            || OnSegment(path, PreviewConstants.PathPrefix)
            || OnSegment(path, "/Identity")
            || OnSegment(path, "/api")
            || OnSegment(path, _mcpPath)
            || OnSegment(path, "/media")
            || OnSegment(path, "/.well-known");

    /// <summary>Segment-boundary prefix match: <c>/wadmin</c> excludes <c>/wadmin/users</c>, not <c>/wadmin-x</c>.</summary>
    private static bool OnSegment(PathString path, string prefix)
    {
        if (!path.HasValue)
            return false;
        var value = path.Value!;
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (value.Length == prefix.Length || value[prefix.Length] == '/');
    }
}
