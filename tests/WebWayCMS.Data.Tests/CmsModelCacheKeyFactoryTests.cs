using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CmsModelCacheKeyFactoryTests
{
    private static DbContextOptions<CmsDbContext> CmsOptions() =>
        new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static DbContextOptions<PlainDbContext> PlainOptions() =>
        new DbContextOptionsBuilder<PlainDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Test]
    public void Create_SameExtensionTypes_ProduceEqualKeys()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db1 = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { new DelegateModelExtension(_ => { }) });
        using var db2 = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { new DelegateModelExtension(_ => { }) });

        var key1 = factory.Create(db1, false);
        var key2 = factory.Create(db2, false);

        Assert.Multiple(() =>
        {
            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
        });
    }

    [Test]
    public void Create_DifferentExtensionTypes_ProduceDifferentKeys()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db1 = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { new DelegateModelExtension(_ => { }) });
        using var db2 = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { new AssemblyModelExtension(typeof(CmsDbContext).Assembly) });

        var key1 = factory.Create(db1, false);
        var key2 = factory.Create(db2, false);

        Assert.Multiple(() =>
        {
            Assert.That(key1, Is.Not.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.Not.EqualTo(key2.GetHashCode()));
        });
    }

    [Test]
    public void Create_EmptyVsNonEmptyExtensions_ProduceDifferentKeys()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db1 = new CmsDbContext(CmsOptions(), Array.Empty<ICmsModelExtension>());
        using var db2 = new CmsDbContext(CmsOptions(), new ICmsModelExtension[] { new DelegateModelExtension(_ => { }) });

        var key1 = factory.Create(db1, false);
        var key2 = factory.Create(db2, false);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void Create_DifferentContextTypes_ProduceDifferentKeys()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var cms = new CmsDbContext(CmsOptions());
        using var plain = new PlainDbContext(PlainOptions());

        var key1 = factory.Create(cms, false);
        var key2 = factory.Create(plain, false);

        Assert.Multiple(() =>
        {
            Assert.That(key1, Is.Not.EqualTo(key2));
            Assert.That(key2, Is.Not.EqualTo(key1));
        });
    }

    [Test]
    public void Create_DifferentDesignTime_ProduceDifferentKeys()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db = new CmsDbContext(CmsOptions());

        var key1 = factory.Create(db, false);
        var key2 = factory.Create(db, true);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void Create_NonCmsDbContext_ProducesKey()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var plain = new PlainDbContext(PlainOptions());

        var key = factory.Create(plain, true);

        Assert.That(key, Is.Not.Null);
    }

    [Test]
    public void Key_EqualsNull_ReturnsFalse()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db = new CmsDbContext(CmsOptions());

        var key = factory.Create(db, false);

        Assert.That(((object)key).Equals(null), Is.False);
    }

    [Test]
    public void Key_EqualsNonKeyObject_ReturnsFalse()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db = new CmsDbContext(CmsOptions());

        var key = factory.Create(db, false);

        Assert.That(((object)key).Equals("not a key"), Is.False);
    }

    [Test]
    public void Key_BoxedEquals_ReturnsTrue()
    {
        var factory = new CmsModelCacheKeyFactory();
        using var db1 = new CmsDbContext(CmsOptions());
        using var db2 = new CmsDbContext(CmsOptions());

        var key1 = factory.Create(db1, false);
        var key2 = factory.Create(db2, false);

        Assert.That(((object)key1).Equals((object)key2), Is.True);
    }

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext(DbContextOptions<PlainDbContext> options) : base(options) { }
    }
}
