using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// <see cref="CmsTransaction.RunWithResultAsync"/>'s control flow (InMemory ignores transactions; see
/// <see cref="CmsTransactionTests"/>).
/// </summary>
[TestFixture]
public class CmsTransactionWithResultTests
{
    [Test]
    public void RunWithResultAsync_NullWork_Throws()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());

        Assert.That(async () => await new CmsTransaction(ctx).RunWithResultAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task RunWithResultAsync_WorkSucceeds_ReturnsItsResultAndKeepsTrackedChanges()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());

        var result = await new CmsTransaction(ctx).RunWithResultAsync(_ =>
        {
            ctx.Add(new ContentNode());
            return Task.FromResult<(bool, string?)>((true, null));
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(ctx.ChangeTracker.Entries<ContentNode>(), Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task RunWithResultAsync_WorkFails_ReturnsItsErrorAndClearsTheChangeTracker()
    {
        using var ctx = TestContexts.Cms(TestContexts.NewDb());

        var result = await new CmsTransaction(ctx).RunWithResultAsync(_ =>
        {
            ctx.Add(new ContentNode());
            return Task.FromResult<(bool, string?)>((false, "nope"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((false, (string?)"nope")));
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
        });
    }
}
