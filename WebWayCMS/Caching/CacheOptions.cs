namespace WebWayCMS.Caching;

/// <summary>
/// Caching configuration, bound from the "Caching" configuration section. Caching is on by default:
/// anonymous public output (the browser/CDN layer) and the published-mode content reads (the
/// database layer) are served from caches that every host evicts when the shared generation stamp
/// in the database moves.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Caching";

    /// <summary>
    /// Whether the output cache, the published-read cache, and the generation poller are active.
    /// Defaults to true. When false the CMS still registers everything (so the invalidation targets
    /// exist) but no policy runs, no read is cached, and no poller starts.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long an anonymous public response stays in the output cache before it is revalidated
    /// against the pipeline. Between 1 second and 1 day. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan OutputCacheExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a published-mode content read stays in the data cache before it is re-read from the
    /// database. Between 1 second and 1 day. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan DataCacheExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often a host polls the database generation stamp to pick up changes made by other hosts
    /// (a split deployment: rendering-only hosts see admin-host publishes this way). Zero disables
    /// polling — the host then relies on its own interceptor-driven bumps and the cache TTL.
    /// Between 0 and 10 minutes. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan GenerationPollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
