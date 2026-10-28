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
        entity.Property(e => e.MetadataJson).HasMaxLength(4000);
        entity.ToTable("ContentZoneItems");

        // Placement is the version's Zone relation (zone + ordinal); these accessors read it in memory.
        entity.Ignore(e => e.ContentZoneNodeId);
        entity.Ignore(e => e.Ordinal);
    }
}
