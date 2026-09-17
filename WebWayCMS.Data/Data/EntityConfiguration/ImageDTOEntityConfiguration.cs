using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ImageDTOEntityConfiguration : IEntityTypeConfiguration<ImageDTO>
{
    public void Configure(EntityTypeBuilder<ImageDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.BlobHash).IsRequired().HasMaxLength(64);
        entity.Property(e => e.AltText).IsRequired().HasMaxLength(500);
        entity.Property(e => e.Caption).HasMaxLength(1000);
        entity.HasIndex(e => e.BlobHash);
        entity.ToTable("Images");
    }
}
