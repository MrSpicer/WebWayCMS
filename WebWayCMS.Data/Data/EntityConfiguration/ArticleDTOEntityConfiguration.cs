using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ArticleDTOEntityConfiguration : IEntityTypeConfiguration<ArticleDTO>
{
    public void Configure(EntityTypeBuilder<ArticleDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.ToTable("Articles");

        entity.HasOne<ContentNode>()
              .WithMany()
              .HasForeignKey(e => e.ArticleListNodeId)
              .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(e => e.ArticleListNodeId);
    }
}
