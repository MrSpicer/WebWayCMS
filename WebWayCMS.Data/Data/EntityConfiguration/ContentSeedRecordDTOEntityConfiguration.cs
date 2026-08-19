using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentSeedRecordDTOEntityConfiguration : IEntityTypeConfiguration<ContentSeedRecordDTO>
{
    public void Configure(EntityTypeBuilder<ContentSeedRecordDTO> entity)
    {
        entity.HasKey(e => e.SeedId);
        entity.Property(e => e.SeedId).ValueGeneratedNever();
        entity.Property(e => e.ContentTypeKey).IsRequired().HasMaxLength(128);
        entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
        entity.Property(e => e.Source).IsRequired().HasMaxLength(2048);
        entity.HasIndex(e => e.NodeId);
        entity.ToTable("ContentSeedRecords");
    }
}
