using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ContentRelationEntityConfiguration : IEntityTypeConfiguration<ContentRelation>
{
    public void Configure(EntityTypeBuilder<ContentRelation> entity)
    {
        entity.HasKey(r => r.Id);
        entity.HasOne<ContentVersion>()
              .WithMany(v => v.Relations)
              .HasForeignKey(r => r.SourceVersionId)
              .OnDelete(DeleteBehavior.Cascade);

        // Deliberately no FK on TargetNodeId: historical versions may keep pointing at a deleted
        // target (it resolves to nothing); the relation engine enforces integrity for live versions.
        entity.Property(r => r.Name).HasMaxLength(ContentRelation.NameMaxLength).IsRequired();
        entity.Property(r => r.Qualifier).HasMaxLength(ContentRelation.QualifierMaxLength).IsRequired();

        entity.HasIndex(r => new { r.SourceVersionId, r.Name });
        entity.HasIndex(r => new { r.TargetNodeId, r.Name });

        entity.ToTable("ContentRelations");
    }
}
