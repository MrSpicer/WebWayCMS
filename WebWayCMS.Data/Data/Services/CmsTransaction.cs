using Microsoft.EntityFrameworkCore.Storage;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Services;

/// <inheritdoc cref="ICmsTransaction"/>
public sealed class CmsTransaction : ICmsTransaction
{
    private readonly CmsDbContext _context;

    public CmsTransaction(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> RunAsync(Func<CancellationToken, Task<bool>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunCoreAsync(work, ok => ok, ct);
    }

    public Task<(bool Success, string? ErrorMessage)> RunWithResultAsync(
        Func<CancellationToken, Task<(bool Success, string? ErrorMessage)>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunCoreAsync(work, result => result.Success, ct);
    }

    private async Task<T> RunCoreAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool> succeeded, CancellationToken ct)
    {
        // A caller's open transaction owns the commit and the rollback.
        if (_context.Database.CurrentTransaction != null)
            return await work(ct);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        T result;
        try
        {
            result = await work(ct);
            if (succeeded(result))
            {
                await transaction.CommitAsync(ct);
                return result;
            }
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }

        await RollbackAsync(transaction);
        return result;
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        // The rollback restores the rows but not the change tracker; the context is scoped and shared,
        // so a later SaveChanges would otherwise replay the abandoned changes outside any transaction.
        await transaction.RollbackAsync(CancellationToken.None);
        _context.ChangeTracker.Clear();
    }
}
