using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.DbContexts;

public static class ContentModelConfiguration
{
    public static void ConfigureContentLink<T>(this EntityTypeBuilder<T> entity) where T : class, IVersionedContent
    {
        entity.HasKey(e => e.VersionId);
        entity.HasOne(e => e.Version)
              .WithOne()
              .HasForeignKey<T>(e => e.VersionId)
              .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(e => e.Version).AutoInclude();
    }

    /// <summary>
    /// The catalog columns a component registration table shares (<see cref="IRegistrationDescriptor"/>):
    /// required display name and category, the icon class, and the category / active indexes.
    /// </summary>
    public static void ConfigureRegistrationColumns<T>(this EntityTypeBuilder<T> entity) where T : class, IRegistrationDescriptor
    {
        entity.Property<string>(nameof(IRegistrationDescriptor.DisplayName)).IsRequired().HasMaxLength(256);
        entity.Property<string>(nameof(IRegistrationDescriptor.Category)).IsRequired().HasMaxLength(128);
        entity.HasIndex(nameof(IRegistrationDescriptor.Category));
        entity.Property<string>(nameof(IRegistrationDescriptor.IconClass)).HasMaxLength(128);
        entity.HasIndex(nameof(IRegistrationDescriptor.IsActive));
    }

    /// <summary>
    /// Marks every entity whose CLR type lives in the <c>WebWayCMS.Data</c> assembly or the
    /// ASP.NET Identity stores assembly as excluded from migrations. Used by
    /// <see cref="CmsExtensionDbContext{TSelf}"/> so a host's migrations-only context leaves the
    /// CMS-owned and Identity tables alone (the CMS migrates those itself, first) and only ever
    /// emits the tables the host contributed. Assembly-based rather than a hardcoded list, so it
    /// survives new CMS entities.
    /// </summary>
    public static void ExcludeCmsOwnedTablesFromMigrations(this ModelBuilder modelBuilder)
    {
        var cmsAssembly = typeof(ContentModelConfiguration).Assembly;
        var identityAssembly = typeof(IdentityUser).Assembly;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType.Assembly == cmsAssembly || clrType.Assembly == identityAssembly)
            {
                entityType.SetIsTableExcludedFromMigrations(true);
            }
        }
    }
}
