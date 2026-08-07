using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentBlockDTOEntityConfiguration : IEntityTypeConfiguration<ContentBlockDTO>
{
    public void Configure(EntityTypeBuilder<ContentBlockDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.Content).IsRequired().HasMaxLength(10000);
        entity.ToTable("ContentBlocks");
    }
}
