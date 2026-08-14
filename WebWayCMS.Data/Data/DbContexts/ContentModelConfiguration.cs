using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public static class ContentModelConfiguration
{
    public static void ConfigureContentLink<T>(this EntityTypeBuilder<T> entity) where T : class, IVersionedContent
    {
        entity.HasKey(e => e.VersionId);
        entity.HasOne(e => e.Version)
              .WithOne()
              .HasForeignKey<T>(e => e.VersionId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(e => e.Version).AutoInclude();
    }
}
