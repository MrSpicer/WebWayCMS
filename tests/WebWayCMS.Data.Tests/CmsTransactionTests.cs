using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// InMemory ignores transactions (see <see cref="TestReadContext"/>'s note), so these cover the helper's
/// control flow — commit vs. rollback, and the change-tracker reset on rollback — not the database's
/// atomicity, which is verified against PostgreSQL.
/// </summary>
[TestFixture]
public class CmsTransactionTests
{
    [Test]
    public void Constructor_NullContext_Throws()
    {
        Assert.That(() => new CmsTransaction(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void RunAsync_NullWork_Throws()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());

        Assert.That(async () => await new CmsTransaction(ctx).RunAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task RunAsync_WorkSucceeds_ReturnsTrueAndKeepsTrackedChanges()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());
        var tx = new CmsTransaction(ctx);

        var ok = await tx.RunAsync(_ =>
        {
            ctx.Add(new ContentNode());
            return Task.FromResult(true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(ctx.ChangeTracker.Entries<ContentNode>(), Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task RunAsync_WorkReturnsFalse_ReturnsFalseAndClearsTheChangeTracker()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());
        var tx = new CmsTransaction(ctx);

        var ok = await tx.RunAsync(_ =>
        {
            ctx.Add(new ContentNode());
            return Task.FromResult(false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
        });
    }

    [Test]
    public void RunAsync_WorkThrows_RethrowsAndClearsTheChangeTracker()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());
        var tx = new CmsTransaction(ctx);

        Assert.That(async () => await tx.RunAsync(_ =>
        {
            ctx.Add(new ContentNode());
            throw new DbUpdateException("boom");
        }), Throws.TypeOf<DbUpdateException>());
        Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
    }
}
