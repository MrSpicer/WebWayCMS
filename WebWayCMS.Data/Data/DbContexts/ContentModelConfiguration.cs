using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public static class ContentModelConfiguration
{
    public static void ConfigureContentLink<T>(this EntityTypeBuilder<T> entity) where T : class, IContent
    {
        entity.HasKey(e => e.ContentId);
        entity.HasOne(e => e.ContentMeta)
              .WithOne()
              .HasForeignKey<T>(e => e.ContentId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(e => e.ContentMeta).AutoInclude();
    }
}
