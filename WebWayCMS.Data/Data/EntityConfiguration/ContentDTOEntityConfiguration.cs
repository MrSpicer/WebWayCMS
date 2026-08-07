using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentDTOEntityConfiguration : IEntityTypeConfiguration<ContentDTO>
{
    public void Configure(EntityTypeBuilder<ContentDTO> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Title).IsRequired().HasMaxLength(20000);
        entity.HasIndex(e => e.MasterId);
        entity.HasIndex(e => e.Slug);
        entity.HasIndex(e => e.ParentMasterId);

        entity.ToTable("Content");

        entity.OwnsMany(e => e.CustomFields, cf => cf.ToJson());
    }
}
