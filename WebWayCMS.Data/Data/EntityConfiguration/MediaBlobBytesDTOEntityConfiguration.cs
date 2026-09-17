using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class MediaBlobBytesDTOEntityConfiguration : IEntityTypeConfiguration<MediaBlobBytesDTO>
{
    public void Configure(EntityTypeBuilder<MediaBlobBytesDTO> entity)
    {
        entity.HasKey(e => e.Hash);
        entity.Property(e => e.Hash).ValueGeneratedNever().IsRequired().HasMaxLength(64).IsFixedLength();
        entity.Property(e => e.Bytes).IsRequired().HasColumnType("bytea");
        entity.ToTable("MediaBlobBytes");
    }
}
