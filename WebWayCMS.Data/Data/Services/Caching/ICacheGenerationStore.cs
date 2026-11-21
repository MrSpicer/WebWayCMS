namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// Reads and bumps the cross-host <c>CacheGenerations</c> stamp. The bump runs on a fresh context
/// from a scope so it never re-enters the saving context (or the interceptor watching it).
/// </summary>
public interface ICacheGenerationStore
{
    /// <summary>The current stamp value.</summary>
    Task<long> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the stamp and returns the new value.
    /// </summary>
    /// <returns>(true, new value, null) on success, or (false, null, error) on failure.</returns>
    Task<(bool Success, long? Value, string? ErrorMessage)> BumpAsync(CancellationToken ct = default);
}
