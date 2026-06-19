using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// Factory helpers for EF Core InMemory contexts. Each test uses a unique database name so
/// fixtures are fully isolated. The transaction-ignored warning is suppressed because the
/// services use real transactions which the InMemory provider treats as no-ops.
/// </summary>
internal static class TestContexts
{
	public static ContentBlockContext ContentBlock(string db) =>
		new(Options<ContentBlockContext>(db));

	public static PageContext Page(string db) =>
		new(Options<PageContext>(db));

	public static ContentZoneContext ContentZone(string db) =>
		new(Options<ContentZoneContext>(db));

	public static ArticleContext Article(string db) =>
		new(Options<ArticleContext>(db));

	public static ApplicationDbContext Application(string db) =>
		new(Options<ApplicationDbContext>(db));

	private static DbContextOptions<T> Options<T>(string db) where T : DbContext =>
		new DbContextOptionsBuilder<T>()
			.UseInMemoryDatabase(db)
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;

	public static string NewDb() => Guid.NewGuid().ToString();
}
