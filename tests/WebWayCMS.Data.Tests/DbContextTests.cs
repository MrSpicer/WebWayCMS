using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class DbContextTests
{
    [Test]
    public async Task CmsDbContext_ConfiguresAllEntitySets()
    {
        var db = TestContexts.NewDb();
        var listId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        await using (var ctx = TestContexts.Cms(db))
        {
            ctx.Set<ArticleListDTO>().Add(new ArticleListDTO { ContentId = listId, ContentMeta = new ContentDTO { Id = listId, Title = "List" } });
            ctx.Set<ArticleDTO>().Add(new ArticleDTO { ContentId = articleId, Body = "b", ArticleListMasterId = Guid.NewGuid(), ContentMeta = new ContentDTO { Id = articleId, Title = "Article" } });
            await ctx.SaveChangesAsync();
        }

        await using var verify = TestContexts.Cms(db);
        Assert.Multiple(() =>
        {
            Assert.That(verify.Users, Is.Not.Null);
            Assert.That(verify.Set<ArticleDTO>().Count(), Is.EqualTo(1));
            Assert.That(verify.Set<ArticleListDTO>().Count(), Is.EqualTo(1));
        });
    }
}
