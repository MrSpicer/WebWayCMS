using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

/// <summary>
/// A host-contributed piece of the EF Core model. Implementations are injected into
/// <see cref="CmsDbContext"/> and applied, in registration order, after the CMS's own entity
/// configurations. This is the seam a package consumer uses to add its own EF-backed content
/// types without editing CMS source.
/// </summary>
public interface ICmsModelExtension
{
    void Configure(ModelBuilder modelBuilder);
}
