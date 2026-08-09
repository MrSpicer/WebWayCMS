using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class PageDTOEntityConfiguration : IEntityTypeConfiguration<PageDTO>
{
    public void Configure(EntityTypeBuilder<PageDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.Property(e => e.ConfigurationJson).HasMaxLength(4000);
        entity.ToTable("Pages");
    }
}
