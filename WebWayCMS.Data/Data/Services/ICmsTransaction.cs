namespace WebWayCMS.Data.Services;

/// <summary>
/// Runs several content writes — possibly across different <see cref="IContentStore{T}"/>s, which share
/// the scoped <c>CmsDbContext</c> — as one database transaction.
/// </summary>
public interface ICmsTransaction
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction. Commits when it returns <c>true</c>; rolls back
    /// (and clears the scoped change tracker, so no half-applied change is replayed by a later save)
    /// when it returns <c>false</c> or throws. An exception is rethrown after the rollback.
    /// </summary>
    Task<bool> RunAsync(Func<CancellationToken, Task<bool>> work, CancellationToken ct = default);
}
