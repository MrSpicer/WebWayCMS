using Microsoft.EntityFrameworkCore;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public class ContentBlockContext : DbContext
{
    public ContentBlockContext(DbContextOptions<ContentBlockContext> options) : base(options) { }

    public DbSet<ContentBlockDTO> ContentBlocks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ConfigureContent(ownsTable: false);

        modelBuilder.Entity<ContentBlockDTO>(entity =>
        {
            entity.ConfigureContentLink();
            entity.Property(e => e.Content).IsRequired().HasMaxLength(10000);
            entity.ToTable("ContentBlocks");
        });
    }
}