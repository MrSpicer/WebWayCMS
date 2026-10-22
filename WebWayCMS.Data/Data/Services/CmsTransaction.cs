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

    public async Task<bool> RunAsync(Func<CancellationToken, Task<bool>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            if (await work(ct))
            {
                await transaction.CommitAsync(ct);
                return true;
            }
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }

        await RollbackAsync(transaction);
        return false;
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        // The rollback restores the rows but not the change tracker; the context is scoped and shared,
        // so a later SaveChanges would otherwise replay the abandoned changes outside any transaction.
        await transaction.RollbackAsync(CancellationToken.None);
        _context.ChangeTracker.Clear();
    }
}
