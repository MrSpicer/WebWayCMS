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
        var listNodeId = Guid.NewGuid();
        var listVersionId = Guid.NewGuid();
        var articleNodeId = Guid.NewGuid();
        var articleVersionId = Guid.NewGuid();
        await using (var ctx = TestContexts.Cms(db))
        {
            ctx.Set<ArticleListDTO>().Add(new ArticleListDTO
            {
                VersionId = listVersionId,
                Version = new ContentVersion
                {
                    Id = listVersionId,
                    NodeId = listNodeId,
                    Node = new ContentNode { Id = listNodeId, ContentTypeKey = "articlelists" },
                    Title = "List"
                }
            });
            ctx.Set<ArticleDTO>().Add(new ArticleDTO
            {
                VersionId = articleVersionId,
                Body = "b",
                ArticleListNodeId = listNodeId,
                Version = new ContentVersion
                {
                    Id = articleVersionId,
                    NodeId = articleNodeId,
                    Node = new ContentNode { Id = articleNodeId, ContentTypeKey = "articles" },
                    Title = "Article"
                }
            });
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
