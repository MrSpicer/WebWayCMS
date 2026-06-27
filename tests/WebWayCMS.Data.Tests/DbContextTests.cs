using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// Exercises the contexts that no service touches directly, so their constructors and
/// OnModelCreating configuration are covered.
/// </summary>
[TestFixture]
public class DbContextTests
{
    [Test]
    public async Task ArticleContext_ConfiguresArticlesAndArticleLists()
    {
        var db = TestContexts.NewDb();
        await using (var ctx = TestContexts.Article(db))
        {
            var listId = Guid.NewGuid();
            var articleId = Guid.NewGuid();
            ctx.ArticleLists.Add(new ArticleListDTO { ContentId = listId, ContentMeta = new ContentDTO { Id = listId, Title = "List" } });
            ctx.Articles.Add(new ArticleDTO { ContentId = articleId, Body = "b", ArticleListMasterId = Guid.NewGuid(), ContentMeta = new ContentDTO { Id = articleId, Title = "Article" } });
            await ctx.SaveChangesAsync();
        }

        await using var verify = TestContexts.Article(db);
        Assert.Multiple(async () =>
        {
            Assert.That(await verify.Articles.CountAsync(), Is.EqualTo(1));
            Assert.That(await verify.ArticleLists.CountAsync(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplicationDbContext_CanBeConstructed()
    {
        using var ctx = TestContexts.Application(TestContexts.NewDb());

        Assert.That(ctx.Users, Is.Not.Null);
    }
}