using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class PageSetSnapshotItemEntityConfiguration : IEntityTypeConfiguration<PageSetSnapshotItem>
{
    public void Configure(EntityTypeBuilder<PageSetSnapshotItem> entity)
    {
        entity.HasKey(e => new { e.SnapshotId, e.ItemVersionId });
        entity.ToTable("PageSetSnapshotItems");

        entity.HasOne<ContentVersion>()
              .WithMany()
              .HasForeignKey(e => e.ItemVersionId)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => e.ItemVersionId);
        // The node/zone ids are denormalized for listings (no FK); indexed so a lookup by node or zone
        // never scans the table.
        entity.HasIndex(e => e.ItemNodeId);
        entity.HasIndex(e => e.ZoneNodeId);
    }
}
