using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class PageDTOEntityConfiguration : IEntityTypeConfiguration<PageDTO>
{
    public void Configure(EntityTypeBuilder<PageDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.Route).IsRequired().HasMaxLength(512);
        entity.HasIndex(e => e.Route);
        entity.Property(e => e.ControllerName).IsRequired().HasMaxLength(256);
        entity.Property(e => e.ConfigurationJson).HasMaxLength(4000);
        entity.ToTable("Pages");
    }
}
