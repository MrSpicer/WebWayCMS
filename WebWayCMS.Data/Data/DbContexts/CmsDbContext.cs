using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

public class CmsDbContext : IdentityDbContext
{
    private readonly IEnumerable<ICmsModelExtension> _modelExtensions;

    public CmsDbContext(DbContextOptions<CmsDbContext> options, IEnumerable<ICmsModelExtension>? modelExtensions = null)
        : base(options)
    {
        _modelExtensions = modelExtensions ?? Array.Empty<ICmsModelExtension>();
    }

    protected CmsDbContext(DbContextOptions options, IEnumerable<ICmsModelExtension>? modelExtensions)
        : base(options)
    {
        _modelExtensions = modelExtensions ?? Array.Empty<ICmsModelExtension>();
    }

    /// <summary>
    /// True while a database transaction is open on this context. Published-mode reads skip the content
    /// read cache then: they may see the transaction's uncommitted writes (which a rollback would leave
    /// cached) and must see them (a cached pre-transaction value would hide them from the writer).
    /// Virtual because the InMemory provider never reports a transaction; tests override it.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public virtual bool HasOpenTransaction => Database.CurrentTransaction is not null;

    internal IReadOnlyList<Type> ModelExtensionTypes => _modelExtensions.Select(e => e.GetType()).ToArray();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var extension in _modelExtensions)
        {
            extension.Configure(modelBuilder);
        }
    }
}
