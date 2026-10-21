using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentZoneAssignmentDTOEntityConfiguration : IEntityTypeConfiguration<ContentZoneAssignmentDTO>
{
    public void Configure(EntityTypeBuilder<ContentZoneAssignmentDTO> entity)
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.SlotName).IsRequired().HasMaxLength(256);
        entity.ToTable("ContentZoneAssignments",
            t => t.HasCheckConstraint("CK_ContentZoneAssignments_OneParent",
                "(\"ParentPageNodeId\" IS NOT NULL AND \"ParentItemNodeId\" IS NULL) OR " +
                "(\"ParentPageNodeId\" IS NULL AND \"ParentItemNodeId\" IS NOT NULL)"));

        entity.HasOne(e => e.ContentZoneNode)
              .WithMany()
              .HasForeignKey(e => e.ContentZoneNodeId)
              .OnDelete(DeleteBehavior.Cascade);

        // ContentZoneService deletes an item's nested zone tree before hard-deleting the item. Restrict
        // (not Cascade) makes a hard delete that skips that step fail loudly instead of silently
        // dropping the assignment rows and orphaning the nested zones, their items, and their routes.
        entity.HasOne(e => e.ParentItemNode)
              .WithMany()
              .HasForeignKey(e => e.ParentItemNodeId)
              .IsRequired(false)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.ParentPageNodeId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentPageNodeId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_PageSlot");

        entity.HasIndex(e => new { e.ParentItemNodeId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentItemNodeId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_ItemSlot");
    }
}
