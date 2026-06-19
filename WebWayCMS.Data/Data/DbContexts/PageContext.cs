using Microsoft.EntityFrameworkCore;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public class PageContext : DbContext
{
    public PageContext(DbContextOptions<PageContext> options) : base(options) { }

    public DbSet<PageDTO> Pages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ConfigureContent(ownsTable: false);

        modelBuilder.Entity<PageDTO>(entity =>
        {
            entity.ConfigureContentLink();
            entity.Property(e => e.Route).IsRequired().HasMaxLength(512);
            entity.HasIndex(e => e.Route);
            entity.Property(e => e.ControllerName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ConfigurationJson).HasMaxLength(4000);
            entity.ToTable("Pages");
        });
    }
}
