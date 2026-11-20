using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class PageControllerRegistrationDTOEntityConfiguration : IEntityTypeConfiguration<PageControllerRegistrationDTO>
{
    public void Configure(EntityTypeBuilder<PageControllerRegistrationDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ControllerName).IsRequired().HasMaxLength(256);
        // Not unique: every version of a registration is its own row.
        entity.HasIndex(e => e.ControllerName);
        entity.Property(e => e.ControllerTypeName).IsRequired().HasMaxLength(1024);
        entity.ConfigureRegistrationColumns();
        entity.ToTable("PageControllerRegistrations");
    }
}
