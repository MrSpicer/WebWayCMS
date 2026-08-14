using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentVersionEntityConfiguration : IEntityTypeConfiguration<ContentVersion>
{
    public void Configure(EntityTypeBuilder<ContentVersion> entity)
    {
        entity.HasKey(v => v.Id);
        entity.HasOne(v => v.Node)
              .WithMany()
              .HasForeignKey(v => v.NodeId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(v => v.Node).AutoInclude();

        entity.Property(v => v.Culture).HasMaxLength(32).IsRequired();
        entity.Property(v => v.Segment).HasMaxLength(64).IsRequired();
        entity.Property(v => v.Title).HasMaxLength(20000);
        entity.Property(v => v.Slug).HasMaxLength(20000);

        // DB-enforced: at most one published version per variant.
        entity.HasIndex(v => new { v.NodeId, v.Culture, v.Segment })
              .IsUnique()
              .HasFilter("\"State\" = 3")
              .HasDatabaseName("UX_ContentVersion_PublishedVariant");

        // DB-enforced: exactly one current draft per variant. IsCurrentDraft is a redundant key
        // column (always true within the filter) included only so this index's property set is
        // distinct from the published index above — EF Core deduplicates HasIndex calls that share
        // the same columns, so two partial unique indexes on (NodeId, Culture, Segment) would collapse.
        entity.HasIndex(v => new { v.NodeId, v.Culture, v.Segment, v.IsCurrentDraft })
              .IsUnique()
              .HasFilter("\"IsCurrentDraft\"")
              .HasDatabaseName("UX_ContentVersion_DraftVariant");

        // DB-enforced: no duplicate version numbers.
        entity.HasIndex(v => new { v.NodeId, v.Culture, v.Segment, v.VersionNumber })
              .IsUnique()
              .HasDatabaseName("UX_ContentVersion_Number");

        entity.HasIndex(v => v.ChangeSetId);
        entity.HasIndex(v => v.Slug);

        entity.ToTable("ContentVersions");

        entity.OwnsMany(v => v.CustomFields, cf => cf.ToJson());
    }
}
