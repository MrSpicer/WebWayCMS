using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

/// <summary>
/// Shared EF Core configuration for the composed content model. The shared <see cref="ContentDTO"/>
/// fields live in a single "Content" table; each content type links to its row via a
/// shared-primary-key 1:1 relationship. Only the owning context emits DDL for the shared table; all
/// other contexts map it with <c>ExcludeFromMigrations</c> so they can still declare their foreign
/// keys without re-creating it.
/// </summary>
public static class ContentModelConfiguration
{
    /// <summary>
    /// Configures the shared <see cref="ContentDTO"/> entity / "Content" table.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="ownsTable">
    /// True for the single context that creates and migrates the "Content" table; false for the
    /// others, which reference it without emitting DDL.
    /// </param>
    public static void ConfigureContent(this ModelBuilder modelBuilder, bool ownsTable)
    {
        modelBuilder.Entity<ContentDTO>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(20000);
            entity.HasIndex(e => e.MasterId);
            entity.HasIndex(e => e.Slug);
            entity.HasIndex(e => e.ParentMasterId);

            if (ownsTable)
                entity.ToTable("Content");
            else
                entity.ToTable("Content", t => t.ExcludeFromMigrations());

            // Store CustomFields as JSON on the shared Content table.
            entity.OwnsMany(e => e.CustomFields, cf => cf.ToJson());
        });
    }

    /// <summary>
    /// Wires the shared-primary-key 1:1 relationship between a content type and its
    /// <see cref="ContentDTO"/> row, and marks the navigation as auto-included so the shared fields
    /// are always loaded.
    /// </summary>
    public static void ConfigureContentLink<T>(this EntityTypeBuilder<T> entity) where T : class, IContent
    {
        entity.HasKey(e => e.ContentId);
        entity.HasOne(e => e.ContentMeta)
              .WithOne()
              .HasForeignKey<T>(e => e.ContentId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(e => e.ContentMeta).AutoInclude();
    }
}