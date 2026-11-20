using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentTypeRegistrationDTOEntityConfiguration : IEntityTypeConfiguration<ContentTypeRegistrationDTO>
{
    public void Configure(EntityTypeBuilder<ContentTypeRegistrationDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ContentTypeKey).IsRequired().HasMaxLength(ContentTypeDescriptor.MaxKeyLength);
        // Not unique: every version of a registration is its own row.
        entity.HasIndex(e => e.ContentTypeKey);
        entity.Property(e => e.ClrTypeName).IsRequired().HasMaxLength(1024);
        entity.Property(e => e.PluralDisplayName).IsRequired().HasMaxLength(256);
        entity.Property(e => e.Description).HasMaxLength(2000);
        entity.ConfigureCatalogColumns();
        entity.ToTable("ContentTypeRegistrations");
    }
}
