using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class CMSRouteDTOEntityConfiguration : IEntityTypeConfiguration<CMSRouteDTO>
{
    public void Configure(EntityTypeBuilder<CMSRouteDTO> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Pattern).IsRequired().HasMaxLength(512);
        entity.HasIndex(e => e.Pattern).IsUnique();
        entity.Property(e => e.NavigationName).HasMaxLength(CMSRouteDTO.NavigationNameMaxLength);
        entity.Property(e => e.DefaultsJson).HasMaxLength(4000);
        entity.Property(e => e.ConstraintsJson).HasMaxLength(2000);
        entity.Property(e => e.DataTokensJson).HasMaxLength(2000);
        entity.HasIndex(e => e.OwningContentNodeId);
        entity.ToTable("CMSRoutes");
    }
}
