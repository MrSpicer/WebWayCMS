using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.EntityConfiguration;

public sealed class ArticleListDTOEntityConfiguration : IEntityTypeConfiguration<ArticleListDTO>
{
    public void Configure(EntityTypeBuilder<ArticleListDTO> entity)
    {
        entity.ConfigureContentLink();
        entity.ToTable("ArticleLists");
    }
}
