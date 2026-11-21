namespace WebWayCMS.Data.Models;

/// <summary>
/// Cross-host cache invalidation stamp: a single row whose <see cref="Value"/> is bumped by every
/// host on any change visitors can see (a publish, an unpublish, a delete, a live CMS route change).
/// Rendering-only hosts share the database but not the process, so each host polls the value and
/// evicts its own caches when it moves. Not versioned — it is infrastructure, not content.
/// </summary>
public record CacheGenerationDTO
{
    public int Id { get; set; }

    public long Value { get; set; }
}
