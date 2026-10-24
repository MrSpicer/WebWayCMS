using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

// The version/node FKs are Restrict: ContentStore removes every snapshot referencing a version
// before it hard-deletes that version, so a delete path that skips that step fails loudly instead of
// leaving a snapshot that can no longer be previewed or reverted.
public sealed class PageSetSnapshotEntityConfiguration : IEntityTypeConfiguration<PageSetSnapshot>
{
    public void Configure(EntityTypeBuilder<PageSetSnapshot> entity)
    {
        entity.HasKey(e => e.Id);
        entity.ToTable("PageSetSnapshots");

        entity.HasOne<ContentNode>()
              .WithMany()
              .HasForeignKey(e => e.PageNodeId)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<ContentVersion>()
              .WithMany()
              .HasForeignKey(e => e.PageVersionId)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(e => e.Items)
              .WithOne()
              .HasForeignKey(i => i.SnapshotId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.Property(e => e.Note).HasMaxLength(500);
        entity.Property(e => e.StateHash).IsRequired().HasMaxLength(64);

        entity.HasIndex(e => new { e.PageNodeId, e.CreatedUtc });
        entity.HasIndex(e => new { e.PageNodeId, e.Kind, e.CreatedUtc });
        entity.HasIndex(e => e.PageVersionId);
    }
}
