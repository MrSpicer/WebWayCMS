using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

internal static class TestStore
{
    public static ContentStore<T> Create<T>(
        CmsDbContext ctx,
        string contentTypeKey = "test",
        ContentReadMode mode = ContentReadMode.Published) where T : class, IVersionedContent
        => new(
            ctx,
            new TestReadContext(mode),
            new ChangeSetScope(ctx, new DefaultContentUserContext()),
            new DefaultContentUserContext(),
            contentTypeKey);
}
