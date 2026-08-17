using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

/// <summary>
/// Base class for a host-owned, migrations-only <see cref="DbContext"/>. It inherits
/// <see cref="CmsDbContext.OnModelCreating"/> (so the host's <see cref="ICmsModelExtension"/>
/// list feeds both the runtime model and the migrations model), then marks every CMS-owned and
/// ASP.NET Identity table as excluded from migrations — the host context only ever migrates the
/// tables the host itself contributed. It is never injected; it exists only so
/// <c>dotnet ef migrations add</c> has a context to diff against.
/// </summary>
public abstract class CmsExtensionDbContext<TSelf> : CmsDbContext
    where TSelf : CmsExtensionDbContext<TSelf>
{
    protected CmsExtensionDbContext(DbContextOptions<TSelf> options, IEnumerable<ICmsModelExtension> modelExtensions)
        : base(options, modelExtensions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ExcludeCmsOwnedTablesFromMigrations();
    }
}
