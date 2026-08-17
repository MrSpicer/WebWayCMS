using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ModelExtensionTests
{
    // Each context gets a unique in-memory database and the custom model cache key factory, so two
    // CmsDbContext instances carrying different extension sets in one test process don't share a
    // stale model (exactly the production seam).
    private static DbContextOptions<CmsDbContext> CmsOptions()
    {
        var builder = new DbContextOptionsBuilder<CmsDbContext>();
        builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
        builder.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
        return builder.Options;
    }

    private static DbContextOptions<TestExtensionDbContext> ExtensionOptions()
    {
        var builder = new DbContextOptionsBuilder<TestExtensionDbContext>();
        builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
        builder.ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>();
        return builder.Options;
    }

    [Test]
    public void AssemblyModelExtension_NullAssembly_Throws()
    {
        Assert.That(() => new AssemblyModelExtension(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AssemblyModelExtension_AppliesConfigurationsFromAssembly()
    {
        var extension = new AssemblyModelExtension(typeof(HostEntity).Assembly);
        using var db = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { extension });

        var entityType = db.Model.FindEntityType(typeof(HostEntity));
        Assert.Multiple(() =>
        {
            Assert.That(entityType, Is.Not.Null);
            Assert.That(entityType!.FindPrimaryKey()!.Properties.Single().Name, Is.EqualTo("Id"));
        });
    }

    [Test]
    public void DelegateModelExtension_NullAction_Throws()
    {
        Assert.That(() => new DelegateModelExtension(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void DelegateModelExtension_InvokesConfigure()
    {
        var invoked = false;
        var extension = new DelegateModelExtension(_ => invoked = true);

        extension.Configure(new ModelBuilder());

        Assert.That(invoked, Is.True);
    }

    [Test]
    public void CmsDbContext_NullExtensions_DefaultToEmpty()
    {
        using var db = new CmsDbContext(CmsOptions());

        Assert.That(db.ModelExtensionTypes, Is.Empty);
    }

    [Test]
    public void CmsDbContext_ProtectedCtor_NullExtensions_DefaultsToEmpty()
    {
        var builder = new DbContextOptionsBuilder<NullExtensionDbContext>();
        builder.UseInMemoryDatabase(Guid.NewGuid().ToString());

        using var db = new NullExtensionDbContext(builder.Options);

        Assert.That(db.ModelExtensionTypes, Is.Empty);
    }

    [Test]
    public void CmsDbContext_AppliesExtensionsInRegistrationOrder()
    {
        var calls = new List<string>();

        using var db = new CmsDbContext(CmsOptions(), new ICmsModelExtension[]
        {
            new RecordingExtension("first", calls),
            new RecordingExtension("second", calls),
        });

        _ = db.Model;

        Assert.That(calls, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void CmsExtensionDbContext_ExcludesCmsAndIdentityButNotHostTables()
    {
        var extension = new AssemblyModelExtension(typeof(HostEntity).Assembly);
        using var db = new TestExtensionDbContext(ExtensionOptions(), new ICmsModelExtension[] { extension });

        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Multiple(() =>
        {
            Assert.That(model.FindEntityType(typeof(ContentVersion))!.IsTableExcludedFromMigrations(), Is.True);
            Assert.That(model.FindEntityType(typeof(IdentityUser))!.IsTableExcludedFromMigrations(), Is.True);
            Assert.That(model.FindEntityType(typeof(HostEntity))!.IsTableExcludedFromMigrations(), Is.False);
        });
    }

    [Test]
    public void ExcludeCmsOwnedTablesFromMigrations_ExcludesCmsAndIdentityButNotHost()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<ContentVersion>();
        modelBuilder.Entity<IdentityUser>();
        modelBuilder.Entity<HostEntity>();

        modelBuilder.ExcludeCmsOwnedTablesFromMigrations();

        var model = modelBuilder.Model;
        Assert.Multiple(() =>
        {
            Assert.That(model.FindEntityType(typeof(ContentVersion))!.IsTableExcludedFromMigrations(), Is.True);
            Assert.That(model.FindEntityType(typeof(IdentityUser))!.IsTableExcludedFromMigrations(), Is.True);
            Assert.That(model.FindEntityType(typeof(HostEntity))!.IsTableExcludedFromMigrations(), Is.False);
        });
    }

    private sealed class RecordingExtension : ICmsModelExtension
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public RecordingExtension(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public void Configure(ModelBuilder modelBuilder) => _calls.Add(_name);
    }

    private sealed class TestExtensionDbContext : CmsExtensionDbContext<TestExtensionDbContext>
    {
        public TestExtensionDbContext(
            DbContextOptions<TestExtensionDbContext> options,
            IEnumerable<ICmsModelExtension> modelExtensions)
            : base(options, modelExtensions)
        {
        }
    }

    private sealed class NullExtensionDbContext : CmsDbContext
    {
        public NullExtensionDbContext(DbContextOptions<NullExtensionDbContext> options)
            : base(options, null!)
        {
        }
    }

    private sealed class HostEntity
    {
        public int Id { get; set; }
    }

    private sealed class HostEntityConfiguration : IEntityTypeConfiguration<HostEntity>
    {
        public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<HostEntity> entity)
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("HostEntities");
        }
    }
}
