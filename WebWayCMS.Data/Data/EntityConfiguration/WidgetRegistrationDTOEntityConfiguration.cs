using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class WidgetRegistrationDTOEntityConfiguration : IEntityTypeConfiguration<WidgetRegistrationDTO>
{
    public void Configure(EntityTypeBuilder<WidgetRegistrationDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ComponentName).IsRequired().HasMaxLength(256);
        // Not unique: every version of a registration is its own row.
        entity.HasIndex(e => e.ComponentName);
        entity.ConfigureRegistrationColumns();
        entity.ToTable("WidgetRegistrations");
    }
}
