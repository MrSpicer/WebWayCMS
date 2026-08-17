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
