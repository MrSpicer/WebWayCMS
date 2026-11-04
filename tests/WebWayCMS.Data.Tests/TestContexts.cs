using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Tests;

internal static class TestContexts
{
    public static CmsDbContext Cms(string db) =>
        new(Options<CmsDbContext>(db));

    private static DbContextOptions<T> Options<T>(string db) where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseInMemoryDatabase(db)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    public static string NewDb() => Guid.NewGuid().ToString();
}
