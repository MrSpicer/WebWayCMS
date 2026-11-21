using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Serilog;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// Flags every save that changes public content and bumps the cross-host generation stamp when that
/// save commits. The bump is deferred to the outermost commit: saves inside a transaction (page
/// publish, zone writes, route projection) flush once in <see cref="IDbTransactionInterceptor.TransactionCommittedAsync"/>,
/// so a rolled-back transaction never bumps. A bump failure only logs — the local generation still
/// advances its local epoch so this host evicts and other hosts catch up at their TTL.
/// </summary>
/// <remarks>
/// Not sealed: tests subclass it to simulate an ambient transaction (the InMemory provider's
/// <c>CurrentTransaction</c> is always null).
/// </remarks>
public class PublicContentChangeInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly ConditionalWeakTable<CmsDbContext, PendingFlag> Pending = new();

    /// <summary>The per-context value type (ConditionalWeakTable values must be reference types).</summary>
    private sealed class PendingFlag
    {
        public bool IsPublic;
    }

    private readonly ICacheGenerationStore _store;
    private readonly ICmsCacheGeneration _generation;
    private readonly ILogger _logger = Log.ForContext<PublicContentChangeInterceptor>();

    public PublicContentChangeInterceptor(ICacheGenerationStore store, ICmsCacheGeneration generation)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
    }

    // ─── save hooks (async and sync share one implementation) ─────────────────

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        // Outside a transaction the save is already committed: flush now. Inside one, the commit
        // event is the flush point (and a rollback drops the flag instead).
        if (IsPendingOutsideTransaction(eventData.Context, out var context))
            await FlushAsync(context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (IsPendingOutsideTransaction(eventData.Context, out var context))
            FlushAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    /// <summary>
    /// Virtual so tests can simulate an ambient transaction (the InMemory provider's
    /// <c>CurrentTransaction</c> is always null). Excluded from coverage like the other
    /// provider-specific seams.
    /// </summary>
    [ExcludeFromCodeCoverage]
    protected virtual bool HasAmbientTransaction(CmsDbContext context) => context.Database.CurrentTransaction is not null;

    // A failed or canceled save only drops the flag outside a transaction: inside one, the outcome
    // is the transaction's (its rollback/failed event clears, its commit flushes).
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        => FinishSaveAsync(eventData.Context, cancellationToken);

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
        => FinishSaveAsync(eventData.Context, CancellationToken.None);

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData, CancellationToken cancellationToken = default)
        => FinishSaveAsync(eventData.Context, cancellationToken);

    // ─── transaction hooks ────────────────────────────────────────────────────

    public async Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (IsPending(eventData.Context, out var context))
            await FlushAsync(context, cancellationToken);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (IsPending(eventData.Context, out var context))
            FlushAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => ClearAsync(eventData.Context);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => ClearAsync(eventData.Context);

    public Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
        => ClearAsync(eventData.Context);

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        => ClearAsync(eventData.Context);

    private static void Detect(DbContext? context)
    {
        if (context is CmsDbContext cms && PublicContentChangeDetector.IsPublicChange(cms.ChangeTracker.Entries()))
            Pending.GetOrCreateValue(cms).IsPublic = true;
    }

    private static bool IsPending(DbContext? context, [NotNullWhen(true)] out CmsDbContext? cms)
    {
        cms = context as CmsDbContext;
        return cms is not null && Pending.TryGetValue(cms, out var pending) && pending.IsPublic;
    }

    private bool IsPendingOutsideTransaction(DbContext? context, [NotNullWhen(true)] out CmsDbContext? cms)
        => IsPending(context, out cms) && !HasAmbientTransaction(cms);

    private Task FinishSaveAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is CmsDbContext cms && !HasAmbientTransaction(cms))
            Pending.Remove(cms);
        return Task.CompletedTask;
    }

    private static Task ClearAsync(DbContext? context)
    {
        if (context is CmsDbContext cms)
            Pending.Remove(cms);
        return Task.CompletedTask;
    }

    private async Task FlushAsync(CmsDbContext context, CancellationToken ct)
    {
        Pending.Remove(context);
        try
        {
            var (success, value, _) = await _store.BumpAsync(ct);
            if (success)
                _generation.Advance(value!.Value);
            else
                // The stamp did not move in the database: still evict this host (its local epoch) so
                // its visitors see the change, without claiming a stamp value the database never
                // issued (that would hide the next real bump from another host). The other hosts
                // catch up at their TTL.
                _generation.AdvanceLocal();
        }
        catch (Exception ex)
        {
            // A flush must never fail the write that triggered it.
            _logger.Warning(ex, "Cache generation flush failed");
        }
    }
}
