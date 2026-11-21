using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CacheGenerationStoreTests
{
    private ServiceProvider _services = null!;
    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        var services = new ServiceCollection();
        services.AddDbContext<CmsDbContext>(options => options.UseInMemoryDatabase(_db));
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    // The migration seeds the single row {1, 0} on a real database; the InMemory
    // provider does not apply HasData, so the tests seed it explicitly.
    private async Task EnsureRowAsync(long value = 0)
    {
        // A fresh scope: resolving the scoped context from the root provider would hand back
        // (and dispose) the same instance a later test method resolves.
        using var scope = _services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        if (!await ctx.Set<CacheGenerationDTO>().AnyAsync())
        {
            ctx.Add(new CacheGenerationDTO { Id = 1, Value = value });
            await ctx.SaveChangesAsync();
        }
    }

    private CacheGenerationStore NewStore() => new(_services.GetRequiredService<IServiceScopeFactory>());

    [Test]
    public void Ctor_NullScopeFactory_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CacheGenerationStore(null!));

    [Test]
    public async Task ReadAsync_ReturnsRowValue()
    {
        await EnsureRowAsync(7);

        Assert.That(await NewStore().ReadAsync(), Is.EqualTo(7));
    }

    [Test]
    public async Task ReadAsync_MissingRow_ReturnsZero()
    {
        // Fresh database, row never seeded.
        Assert.That(await NewStore().ReadAsync(), Is.Zero);
    }

    [Test]
    public async Task BumpAsync_IncrementsAndReturnsNewValue()
    {
        await EnsureRowAsync();
        var store = new TestableStore(_services.GetRequiredService<IServiceScopeFactory>());

        var (success, value, error) = await store.BumpAsync();
        var (success2, value2, _) = await store.BumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(error, Is.Null);
            Assert.That(success2, Is.True);
            Assert.That(value2, Is.EqualTo(2), "the stamp is monotonic");
        });
    }

    [Test]
    public async Task BumpAsync_MissingRow_CreatesItAtOne()
    {
        // Fresh database, row never seeded: the first write must still move the stamp (reads of a
        // missing row count as generation 0), so the upsert creates the row at 1, not 0.
        var store = new TestableStore(_services.GetRequiredService<IServiceScopeFactory>());

        var (success, value, error) = await store.BumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(error, Is.Null);
        });
    }

    [Test]
    public async Task BumpAsync_RunsOnFreshScope_NeverTheAmbientContext()
    {
        await EnsureRowAsync();
        var store = new TestableStore(_services.GetRequiredService<IServiceScopeFactory>());
        using var ambient = _services.GetRequiredService<CmsDbContext>();
        ambient.Set<CacheGenerationDTO>().Add(new CacheGenerationDTO { Id = 42, Value = 99 });
        // An unflushed ambient change: if the bump re-entered this context it would save it too.

        await store.BumpAsync();

        using var verify = _services.CreateScope().ServiceProvider.GetRequiredService<CmsDbContext>();
        Assert.That(await verify.Set<CacheGenerationDTO>().AnyAsync(r => r.Id == 42), Is.False,
            "the bump must run on its own scope, not the ambient saving context");
    }

    [Test]
    public async Task BumpAsync_CoreFails_ReturnsFailureTuple()
    {
        var store = new FailingStore(_services.GetRequiredService<IServiceScopeFactory>());

        var (success, value, error) = await store.BumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(error, Does.Contain("boom"));
        });
    }

    /// <summary>InMemory-compatible bump: the same read-modify-write the SQL statement performs.</summary>
    private sealed class TestableStore : CacheGenerationStore
    {
        public TestableStore(IServiceScopeFactory scopeFactory) : base(scopeFactory)
        {
        }

        protected override async Task<long> BumpCoreAsync(CmsDbContext context, CancellationToken ct)
        {
            // Mirrors the SQL upsert: increment when the row exists, create it at 1 otherwise.
            var row = await context.Set<CacheGenerationDTO>().FirstOrDefaultAsync(r => r.Id == SingleRowId, ct);
            if (row is null)
            {
                row = new CacheGenerationDTO { Id = SingleRowId, Value = 1 };
                context.Add(row);
            }
            else
            {
                row.Value += 1;
            }
            await context.SaveChangesAsync(ct);
            return row.Value;
        }
    }

    private sealed class FailingStore : CacheGenerationStore
    {
        public FailingStore(IServiceScopeFactory scopeFactory) : base(scopeFactory)
        {
        }

        protected override Task<long> BumpCoreAsync(CmsDbContext context, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }
}
