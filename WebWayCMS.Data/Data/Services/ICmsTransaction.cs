namespace WebWayCMS.Data.Services;

/// <summary>
/// Runs several content writes — possibly across different <see cref="IContentStore{T}"/>s, which share
/// the scoped <c>CmsDbContext</c> — as one database transaction.
/// </summary>
/// <remarks>
/// When a transaction is already open on the context (a caller's), the work joins it and the caller commits or
/// rolls back: Npgsql has no nested transactions.
/// </remarks>
public interface ICmsTransaction
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction. Commits when it returns <c>true</c>; rolls back
    /// (and clears the scoped change tracker, so no half-applied change is replayed by a later save)
    /// when it returns <c>false</c> or throws. An exception is rethrown after the rollback.
    /// </summary>
    Task<bool> RunAsync(Func<CancellationToken, Task<bool>> work, CancellationToken ct = default);

    /// <summary>
    /// <see cref="RunAsync(Func{CancellationToken, Task{bool}}, CancellationToken)"/> for a write that reports
    /// why it failed: commits on <c>Success</c>, otherwise rolls back and returns the work's result unchanged.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> RunWithResultAsync(
        Func<CancellationToken, Task<(bool Success, string? ErrorMessage)>> work, CancellationToken ct = default);
}
