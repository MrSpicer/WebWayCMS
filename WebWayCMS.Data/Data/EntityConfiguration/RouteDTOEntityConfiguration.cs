using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class RouteDTOEntityConfiguration : IEntityTypeConfiguration<RouteDTO>
{
    public void Configure(EntityTypeBuilder<RouteDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.Pattern).HasMaxLength(512);
        entity.Property(e => e.NavigationName).HasMaxLength(CMSRouteDTO.NavigationNameMaxLength);
        entity.ToTable("Routes");
    }
}
