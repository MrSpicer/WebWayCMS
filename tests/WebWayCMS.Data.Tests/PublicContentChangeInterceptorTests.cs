using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PublicContentChangeInterceptorTests
{
    private ICacheGenerationStore _store = null!;
    private CmsCacheGeneration _generation = null!;
    private PublicContentChangeInterceptor _interceptor = null!;
    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        _store = Substitute.For<ICacheGenerationStore>();
        _generation = new CmsCacheGeneration();
        _interceptor = new PublicContentChangeInterceptor(_store, _generation);
    }

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    private static void AddVersion(CmsDbContext ctx, ContentVersionState state) => ctx.Add(new ContentVersion
    {
        Id = Guid.NewGuid(),
        NodeId = Guid.NewGuid(),
        State = state,
    });

    private static DbContextEventData Started(CmsDbContext ctx) => new(null!, (_, _) => "", ctx);
    private static SaveChangesCompletedEventData Saved(CmsDbContext ctx) => new(null!, (_, _) => "", ctx, 1);
    private static DbContextErrorEventData Failed(CmsDbContext ctx) => new(null!, (_, _) => "", ctx, new InvalidOperationException("boom"));
    private static TransactionEndEventData Committed(CmsDbContext ctx) => new(
        null!, (_, _) => "", Substitute.For<DbTransaction>(), ctx, Guid.NewGuid(), Guid.NewGuid(), false, DateTimeOffset.UtcNow, TimeSpan.Zero);
    private static TransactionEndEventData RolledBack(CmsDbContext ctx) => new(
        null!, (_, _) => "", Substitute.For<DbTransaction>(), ctx, Guid.NewGuid(), Guid.NewGuid(), false, DateTimeOffset.UtcNow, TimeSpan.Zero);
    private static TransactionErrorEventData TxFailed(CmsDbContext ctx) => new(
        null!, (_, _) => "", Substitute.For<DbTransaction>(), ctx, Guid.NewGuid(), Guid.NewGuid(), false, "err", new InvalidOperationException("boom"), DateTimeOffset.UtcNow, TimeSpan.Zero);

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new PublicContentChangeInterceptor(null!, _generation));
            Assert.Throws<ArgumentNullException>(() => new PublicContentChangeInterceptor(_store, null!));
        });
    }

    [Test]
    public async Task SaveChangesFailedAsync_NonCmsContext_DoesNothing()
    {
        // A non-CMS context on the same pipeline: the interceptor leaves it alone.
        var options = new DbContextOptionsBuilder<DbContext>().UseInMemoryDatabase(TestContexts.NewDb()).Options;
        using var plain = new DbContext(options);

        await _interceptor.SaveChangesFailedAsync(new DbContextErrorEventData(null!, (_, _) => "", plain, new InvalidOperationException("boom")), default);
        await _interceptor.SaveChangesCanceledAsync(new DbContextEventData(null!, (_, _) => "", plain), default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    private async Task FlagPublicSaveAsync(CmsDbContext ctx)
    {
        AddVersion(ctx, ContentVersionState.Published);
        await _interceptor.SavingChangesAsync(Started(ctx), InterceptionResult<int>.SuppressWithResult(1), default);
    }

    private async Task FlagDraftSaveAsync(CmsDbContext ctx)
    {
        AddVersion(ctx, ContentVersionState.Draft);
        await _interceptor.SavingChangesAsync(Started(ctx), InterceptionResult<int>.SuppressWithResult(1), default);
    }

    [Test]
    public async Task SavingChanges_PublicChange_NoTransaction_FlushesOnSave()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.SavedChangesAsync(Saved(ctx), 1, default);

        await _store.Received(1).BumpAsync(default);
        Assert.That(_generation.Current, Is.EqualTo(5));
    }

    [Test]
    public async Task SavingChanges_DraftOnly_NoTransaction_DoesNotBump()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagDraftSaveAsync(ctx);
        await _interceptor.SavedChangesAsync(Saved(ctx), 1, default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
        Assert.That(_generation.Current, Is.Zero);
    }

    [Test]
    public async Task SavingChanges_PublicChange_InTransaction_FlushesAtCommit()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        var inTransaction = new InTransactionInterceptor(_store, _generation);
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Published);
        await inTransaction.SavingChangesAsync(Started(ctx), InterceptionResult<int>.SuppressWithResult(1), default);
        // The save completed, but inside a transaction the flush waits for the commit event.
        await inTransaction.SavedChangesAsync(Saved(ctx), 1, default);
        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());

        await inTransaction.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx), default);

        await _store.Received(1).BumpAsync(default);
        Assert.That(_generation.Current, Is.EqualTo(5));
    }

    [Test]
    public async Task SavingChanges_FailedSave_InTransaction_KeepsFlagUntilCommit()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        var inTransaction = new InTransactionInterceptor(_store, _generation);
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Published);
        await inTransaction.SavingChangesAsync(Started(ctx), InterceptionResult<int>.SuppressWithResult(1), default);
        await inTransaction.SaveChangesFailedAsync(Failed(ctx), default);
        await inTransaction.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx), default);

        await _store.Received(1).BumpAsync(default);
    }

    /// <summary>Simulates an ambient transaction (the InMemory provider's CurrentTransaction is always null).</summary>
    private sealed class InTransactionInterceptor : PublicContentChangeInterceptor
    {
        public InTransactionInterceptor(ICacheGenerationStore store, ICmsCacheGeneration generation)
            : base(store, generation)
        {
        }

        protected override bool HasAmbientTransaction(CmsDbContext context) => true;
    }

    [Test]
    public async Task SavingChanges_PublicChange_Rollback_DoesNotBump()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.TransactionRolledBackAsync(Substitute.For<DbTransaction>(), RolledBack(ctx), default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
        Assert.That(_generation.Current, Is.Zero);
    }

    [Test]
    public async Task SavingChanges_PublicChange_CommitFails_DoesNotBump()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.TransactionFailedAsync(Substitute.For<DbTransaction>(), TxFailed(ctx), default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
        Assert.That(_generation.Current, Is.Zero);
    }

    [Test]
    public async Task SavingChanges_FailedSave_NoTransaction_ClearsFlag()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.SaveChangesFailedAsync(Failed(ctx), default);
        // A later commit of another save on the same context must not flush the dropped save.
        await _interceptor.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx), default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavingChanges_CanceledSave_NoTransaction_ClearsFlag()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.SaveChangesCanceledAsync(Started(ctx), default);
        await _interceptor.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx), default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavingChanges_BumpFails_AdvancesLocalEpochOnlyAndDoesNotThrow()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((false, null, "db down"));
        _generation.Advance(4);
        var raised = new List<long>();
        _generation.Changed += raised.Add;
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.SavedChangesAsync(Saved(ctx), 1, default);
        // Another host's real bump to 5 must still be seen by this host's poller.
        _generation.Advance(5);

        Assert.Multiple(() =>
        {
            // This host evicted (its local epoch moved) without claiming stamp 5 the DB never issued.
            Assert.That(raised, Is.EqualTo(new[] { 4L, 5L }));
            Assert.That(_generation.Current, Is.EqualTo(5));
            Assert.That(_generation.Key, Is.EqualTo("5.1"));
        });
    }

    // ─── sync hooks: a synchronous SaveChanges()/Commit() bumps like the async path ───

    [Test]
    public void SavingChanges_Sync_PublicChange_NoTransaction_FlushesOnSave()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Published);
        _interceptor.SavingChanges(Started(ctx), InterceptionResult<int>.SuppressWithResult(1));
        var result = _interceptor.SavedChanges(Saved(ctx), 1);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(1));
            Assert.That(_generation.Current, Is.EqualTo(5));
        });
    }

    [Test]
    public void SavingChanges_Sync_DraftOnly_DoesNotBump()
    {
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Draft);
        _interceptor.SavingChanges(Started(ctx), InterceptionResult<int>.SuppressWithResult(1));
        _interceptor.SavedChanges(Saved(ctx), 1);

        _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void SavingChanges_Sync_InTransaction_FlushesAtCommit()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns((true, 5L, null));
        var inTransaction = new InTransactionInterceptor(_store, _generation);
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Published);
        inTransaction.SavingChanges(Started(ctx), InterceptionResult<int>.SuppressWithResult(1));
        inTransaction.SavedChanges(Saved(ctx), 1);
        _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());

        inTransaction.TransactionCommitted(Substitute.For<DbTransaction>(), Committed(ctx));

        Assert.That(_generation.Current, Is.EqualTo(5));
    }

    [Test]
    public void TransactionCommitted_Sync_NothingPending_DoesNotBump()
    {
        using var ctx = NewContext();

        _interceptor.TransactionCommitted(Substitute.For<DbTransaction>(), Committed(ctx));

        _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveChangesFailed_Sync_NoTransaction_ClearsFlag()
    {
        using var ctx = NewContext();

        AddVersion(ctx, ContentVersionState.Published);
        _interceptor.SavingChanges(Started(ctx), InterceptionResult<int>.SuppressWithResult(1));
        _interceptor.SaveChangesFailed(Failed(ctx));
        _interceptor.TransactionCommitted(Substitute.For<DbTransaction>(), Committed(ctx));

        _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void TransactionRolledBackAndFailed_Sync_ClearFlag()
    {
        using var ctx1 = NewContext();
        using var ctx2 = NewContext();

        AddVersion(ctx1, ContentVersionState.Published);
        _interceptor.SavingChanges(Started(ctx1), InterceptionResult<int>.SuppressWithResult(1));
        AddVersion(ctx2, ContentVersionState.Published);
        _interceptor.SavingChanges(Started(ctx2), InterceptionResult<int>.SuppressWithResult(1));
        _interceptor.TransactionRolledBack(Substitute.For<DbTransaction>(), RolledBack(ctx1));
        _interceptor.TransactionFailed(Substitute.For<DbTransaction>(), TxFailed(ctx2));
        _interceptor.TransactionCommitted(Substitute.For<DbTransaction>(), Committed(ctx1));
        _interceptor.TransactionCommitted(Substitute.For<DbTransaction>(), Committed(ctx2));

        _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavingChanges_BumpThrows_SwallowsTheFailure()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<(bool Success, long? Value, string? ErrorMessage)>(new InvalidOperationException("db down")));
        using var ctx = NewContext();

        await FlagPublicSaveAsync(ctx);
        await _interceptor.SavedChangesAsync(Saved(ctx), 1, default);

        Assert.That(_generation.Current, Is.Zero);
        await _store.Received(1).BumpAsync(default);
    }

    [Test]
    public async Task SavingChanges_NonCmsContext_DoesNothing()
    {
        using var ctx = new DbContext(new DbContextOptionsBuilder<DbContext>().UseInMemoryDatabase("x").Options);

        await _interceptor.SavingChangesAsync(new DbContextEventData(null!, (_, _) => "", ctx), InterceptionResult<int>.SuppressWithResult(1), default);
        await _interceptor.SavedChangesAsync(new SaveChangesCompletedEventData(null!, (_, _) => "", ctx, 1), 1, default);

        await _store.DidNotReceive().BumpAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavingChanges_TwoContexts_FlagsAreIndependent()
    {
        _store.BumpAsync(Arg.Any<CancellationToken>()).Returns(ci => (true, 1L + _generation.Current, null));
        using var ctx1 = NewContext();
        using var ctx2 = NewContext();

        await FlagPublicSaveAsync(ctx1);
        await FlagDraftSaveAsync(ctx2);
        await _interceptor.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx1), default);
        await _interceptor.TransactionCommittedAsync(Substitute.For<DbTransaction>(), Committed(ctx2), default);

        // Only ctx1's save was public.
        await _store.Received(1).BumpAsync(default);
        Assert.That(_generation.Current, Is.EqualTo(1));
    }
}
