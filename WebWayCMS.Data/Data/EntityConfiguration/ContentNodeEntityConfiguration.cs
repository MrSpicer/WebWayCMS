using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentNodeEntityConfiguration : IEntityTypeConfiguration<ContentNode>
{
    public void Configure(EntityTypeBuilder<ContentNode> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.ContentTypeKey).IsRequired().HasMaxLength(64);
        entity.HasIndex(e => e.ParentNodeId);
        entity.HasIndex(e => e.SiteId);
        entity.ToTable("ContentNodes");
    }
}
