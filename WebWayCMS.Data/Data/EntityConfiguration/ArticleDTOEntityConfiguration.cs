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
        entity.Property(e => e.ArticleListMasterId).HasColumnName("ArticleListMasterId");
        entity.HasIndex(e => e.ArticleListMasterId);
    }
}
