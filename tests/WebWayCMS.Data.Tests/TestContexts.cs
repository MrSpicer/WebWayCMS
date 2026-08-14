using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

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

/// <summary>
/// NOTE — the InMemory coverage gap: this suite uses <c>UseInMemoryDatabase</c>, which enforces
/// NEITHER filtered unique indexes NOR check constraints, and silently ignores transactions. The
/// three invariant indexes on ContentVersion (UX_ContentVersion_PublishedVariant,
/// UX_ContentVersion_DraftVariant, UX_ContentVersion_Number) and the CK_ContentZoneAssignments_OneParent
/// check constraint are therefore untestable here — InMemory will happily let two published versions
/// coexist and every unit test will pass. The service enforces the invariants in code as the primary
/// mechanism (with the indexes as the DB backstop), and the constraints themselves must be verified
/// against real PostgreSQL via <c>./scripts/StartIntegrationHost.sh</c>, not asserted from unit tests.
/// </summary>
internal sealed class TestReadContext : IContentReadContext
{
    public TestReadContext(ContentReadMode mode) => Mode = mode;

    public ContentReadMode Mode { get; }
    public string Culture => string.Empty;
    public string Segment => string.Empty;
}

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
