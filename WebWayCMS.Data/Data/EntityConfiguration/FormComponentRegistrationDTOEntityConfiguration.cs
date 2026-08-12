using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class FormComponentRegistrationDTOEntityConfiguration : IEntityTypeConfiguration<FormComponentRegistrationDTO>
{
    public void Configure(EntityTypeBuilder<FormComponentRegistrationDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ComponentName).IsRequired().HasMaxLength(256);
        entity.HasIndex(e => e.ComponentName).IsUnique();
        entity.Property(e => e.ViewComponentName).IsRequired().HasMaxLength(256);
        entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(256);
        entity.Property(e => e.Category).IsRequired().HasMaxLength(128);
        entity.HasIndex(e => e.Category);
        entity.Property(e => e.IconClass).HasMaxLength(128);
        entity.HasIndex(e => e.IsActive);
        entity.ToTable("FormComponentRegistrations");
    }
}
