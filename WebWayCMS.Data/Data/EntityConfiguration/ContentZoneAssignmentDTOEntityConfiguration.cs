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
                "(\"ParentPageNodeId\" IS NOT NULL AND \"ParentZoneNodeId\" IS NULL) OR " +
                "(\"ParentPageNodeId\" IS NULL AND \"ParentZoneNodeId\" IS NOT NULL)"));

        entity.HasOne(e => e.ContentZoneNode)
              .WithMany()
              .HasForeignKey(e => e.ContentZoneNodeId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ParentZoneNode)
              .WithMany()
              .HasForeignKey(e => e.ParentZoneNodeId)
              .IsRequired(false)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.ParentPageNodeId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentPageNodeId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_PageSlot");

        entity.HasIndex(e => new { e.ParentZoneNodeId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentZoneNodeId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_ZoneSlot");
    }
}
