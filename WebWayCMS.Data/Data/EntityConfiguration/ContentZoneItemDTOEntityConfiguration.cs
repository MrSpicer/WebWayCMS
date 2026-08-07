using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentZoneItemDTOEntityConfiguration : IEntityTypeConfiguration<ContentZoneItemDTO>
{
    public void Configure(EntityTypeBuilder<ContentZoneItemDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ComponentName).IsRequired().HasMaxLength(256);
        entity.Property(e => e.ComponentPropertiesJson).HasMaxLength(4000);
        entity.ToTable("ContentZoneItems");

        entity.HasIndex(e => new { e.ContentZoneId, e.Ordinal });
    }
}
