using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public class ArticleContext : DbContext
{
    public ArticleContext(DbContextOptions<ArticleContext> options) : base(options) { }

    public DbSet<ArticleListDTO> ArticleLists { get; set; } = null!;

    public DbSet<ArticleDTO> Articles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ArticleContext owns and migrates the shared "Content" table.
        modelBuilder.ConfigureContent(ownsTable: true);

        modelBuilder.Entity<ArticleDTO>(entity =>
        {
            entity.ConfigureContentLink();
            entity.ToTable("Articles");
            entity.Property(e => e.ArticleListMasterId).HasColumnName("ArticleListMasterId");
            entity.HasIndex(e => e.ArticleListMasterId);
        });

        modelBuilder.Entity<ArticleListDTO>(entity =>
        {
            entity.ConfigureContentLink();
            entity.ToTable("ArticleLists");
        });
    }
}