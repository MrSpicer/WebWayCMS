using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentZoneDTOEntityConfiguration : IEntityTypeConfiguration<ContentZoneDTO>
{
    public void Configure(EntityTypeBuilder<ContentZoneDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
        entity.Property(e => e.Description).HasMaxLength(1000);
        entity.ToTable("ContentZones");
    }
}
