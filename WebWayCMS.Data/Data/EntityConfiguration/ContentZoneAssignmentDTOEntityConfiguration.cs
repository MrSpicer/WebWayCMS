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
                "(\"ParentPageMasterId\" IS NOT NULL AND \"ParentZoneId\" IS NULL) OR " +
                "(\"ParentPageMasterId\" IS NULL AND \"ParentZoneId\" IS NOT NULL)"));

        entity.HasOne(e => e.ContentZone)
              .WithMany()
              .HasForeignKey(e => e.ContentZoneId)
              .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ParentZone)
              .WithMany()
              .HasForeignKey(e => e.ParentZoneId)
              .IsRequired(false)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.ParentPageMasterId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentPageMasterId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_PageSlot");

        entity.HasIndex(e => new { e.ParentZoneId, e.SlotName })
              .IsUnique()
              .HasFilter("\"ParentZoneId\" IS NOT NULL")
              .HasDatabaseName("IX_ContentZoneAssignments_ZoneSlot");
    }
}
