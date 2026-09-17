using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class MediaBlobDTOEntityConfiguration : IEntityTypeConfiguration<MediaBlobDTO>
{
    public void Configure(EntityTypeBuilder<MediaBlobDTO> entity)
    {
        entity.HasKey(e => e.Hash);
        entity.Property(e => e.Hash).ValueGeneratedNever().IsRequired().HasMaxLength(64).IsFixedLength();
        entity.Property(e => e.ContentType).IsRequired().HasMaxLength(128);
        entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(260);
        entity.HasIndex(e => e.CreatedUtc);
        entity.ToTable("MediaBlobs");
    }
}
