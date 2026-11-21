using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

internal static class TestStore
{
    public static ContentStore<T> Create<T>(
        CmsDbContext ctx,
        string contentTypeKey = "test",
        ContentReadMode mode = ContentReadMode.Published,
        IContentReadCache? readCache = null) where T : class, IVersionedContent
        => new(
            ctx,
            new TestReadContext(mode),
            new ChangeSetScope(ctx, new DefaultContentUserContext()),
            new DefaultContentUserContext(),
            contentTypeKey,
            readCache: readCache);
}
