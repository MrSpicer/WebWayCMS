using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class WebWayCmsBuilderTests
{
    private const string ConnectionString = "Host=localhost;Database=test;Username=u;Password=p";

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            })
            .Build();

    private static (WebWayCmsBuilder Builder, ServiceCollection Services) NewBuilder()
    {
        var services = new ServiceCollection();
        return (new WebWayCmsBuilder(services, Config()), services);
    }

    [Test]
    public void Ctor_NullServices_Throws()
    {
        Assert.That(() => new WebWayCmsBuilder(null!, Config()), Throws.ArgumentNullException);
    }

    [Test]
    public void Ctor_NullConfiguration_Throws()
    {
        Assert.That(() => new WebWayCmsBuilder(new ServiceCollection(), null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Services_ReturnsInjectedCollection()
    {
        var (builder, services) = NewBuilder();

        Assert.That(builder.Services, Is.SameAs(services));
    }

    [Test]
    public void AddApplicationAssembly_Null_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.That(() => builder.AddApplicationAssembly(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AddApplicationAssembly_RegistersModelExtensionAndCatalog()
    {
        var (builder, services) = NewBuilder();
        var assembly = typeof(TestDto).Assembly;

        var result = builder.AddApplicationAssembly(assembly);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(builder));
            Assert.That(builder.Assemblies, Is.EqualTo(new[] { assembly }));
            Assert.That(
                services.Any(d => d.ServiceType == typeof(ICmsModelExtension)
                    && d.ImplementationInstance is AssemblyModelExtension),
                Is.True);
        });
    }

    [Test]
    public void AddApplicationAssembly_PreservesOrder()
    {
        var (builder, _) = NewBuilder();
        var first = typeof(TestDto).Assembly;
        var second = typeof(CmsDbContext).Assembly;

        builder.AddApplicationAssembly(first);
        builder.AddApplicationAssembly(second);

        Assert.That(builder.Assemblies, Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void AddApplicationAssembly_DuplicateAssembly_IsIgnored()
    {
        var (builder, services) = NewBuilder();
        var assembly = typeof(TestDto).Assembly;

        builder.AddApplicationAssembly(assembly);
        builder.AddApplicationAssembly(assembly);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Assemblies, Is.EqualTo(new[] { assembly }));
            Assert.That(
                services.Count(d => d.ServiceType == typeof(ICmsModelExtension)
                    && d.ImplementationInstance is AssemblyModelExtension),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void AddModelConfigurationGeneric_RegistersExtensionType()
    {
        var (builder, services) = NewBuilder();

        builder.AddModelConfiguration<TestModelExtension>();

        Assert.That(
            services.Any(d => d.ServiceType == typeof(ICmsModelExtension)
                && d.ImplementationType == typeof(TestModelExtension)),
            Is.True);
    }

    [Test]
    public void AddModelConfiguration_Null_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.That(() => builder.AddModelConfiguration((ICmsModelExtension)null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AddModelConfiguration_RegistersInstance()
    {
        var (builder, services) = NewBuilder();
        var extension = new TestModelExtension();

        builder.AddModelConfiguration(extension);

        Assert.That(
            services.Any(d => d.ServiceType == typeof(ICmsModelExtension)
                && ReferenceEquals(d.ImplementationInstance, extension)),
            Is.True);
    }

    [Test]
    public void ConfigureModel_Null_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.That(() => builder.ConfigureModel(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void ConfigureModel_RegistersDelegateExtension()
    {
        var (builder, services) = NewBuilder();

        builder.ConfigureModel(_ => { });

        Assert.That(
            services.Any(d => d.ServiceType == typeof(ICmsModelExtension)
                && d.ImplementationInstance is DelegateModelExtension),
            Is.True);
    }

    [Test]
    public void AddContentType_NullKey_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.AddContentType<TestDto>(null!), Throws.ArgumentNullException);
            Assert.That(() => builder.AddContentType<TestDto>(string.Empty), Throws.ArgumentException);
            Assert.That(() => builder.AddContentType<TestDto>("   "), Throws.ArgumentException);
        });
    }

    [Test]
    public void AddContentType_RegistersContentStore()
    {
        var (builder, services) = NewBuilder();

        builder.AddContentType<TestDto>("testdtos");

        Assert.That(services.Any(d => d.ServiceType == typeof(IContentStore<TestDto>)), Is.True);
    }

    [Test]
    public void AddMappingProfile_Null_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.That(() => builder.AddMappingProfile(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AddMappingProfile_AddsToCatalog()
    {
        var (builder, _) = NewBuilder();
        var profile = new TestProfile();

        builder.AddMappingProfile(profile);

        Assert.That(builder.Profiles, Is.EqualTo(new Profile[] { profile }));
    }

    [Test]
    public void AddMigrationsContext_NullHistoryTable_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.AddMigrationsContext<TestMigrationsContext>(null!), Throws.ArgumentNullException);
            Assert.That(() => builder.AddMigrationsContext<TestMigrationsContext>(string.Empty), Throws.ArgumentException);
            Assert.That(() => builder.AddMigrationsContext<TestMigrationsContext>("   "), Throws.ArgumentException);
        });
    }

    [Test]
    public void AddMigrationsContext_MissingConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new WebWayCmsBuilder(services, config);

        Assert.That(
            () => builder.AddMigrationsContext<TestMigrationsContext>("__EFMigrationsHistory_Test"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void AddMigrationsContext_RegistersContextAndCatalog()
    {
        var (builder, services) = NewBuilder();

        builder.AddMigrationsContext<TestMigrationsContext>("__EFMigrationsHistory_Test");

        Assert.Multiple(() =>
        {
            Assert.That(builder.MigrationContextTypes, Is.EqualTo(new[] { typeof(TestMigrationsContext) }));
            Assert.That(services.Any(d => d.ServiceType == typeof(TestMigrationsContext)), Is.True);
        });
    }

    [Test]
    public void RegisterCatalogs_RegistersBothSingletons()
    {
        var (builder, services) = NewBuilder();
        builder.AddApplicationAssembly(typeof(TestDto).Assembly);
        builder.AddMigrationsContext<TestMigrationsContext>("__EFMigrationsHistory_Test");

        builder.RegisterCatalogs();

        Assert.Multiple(() =>
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(CmsAssemblyCatalog)
                && d.ImplementationInstance is CmsAssemblyCatalog), Is.True);
            Assert.That(services.Any(d => d.ServiceType == typeof(CmsMigrationsContextCatalog)
                && d.ImplementationInstance is CmsMigrationsContextCatalog), Is.True);
        });
    }

    [Test]
    public void AddContentSeedFile_NullOrWhitespace_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.AddContentSeedFile(null!), Throws.ArgumentNullException);
            Assert.That(() => builder.AddContentSeedFile(string.Empty), Throws.ArgumentException);
            Assert.That(() => builder.AddContentSeedFile("   "), Throws.ArgumentException);
        });
    }

    [Test]
    public void AddContentSeedFile_CollectsPath()
    {
        var (builder, _) = NewBuilder();

        var result = builder.AddContentSeedFile("contentseed/site.json");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(builder));
            Assert.That(builder.ContentSeedFiles, Is.EqualTo(new[] { "contentseed/site.json" }));
        });
    }

    [Test]
    public void AddContentSeedAssembly_Null_Throws()
    {
        var (builder, _) = NewBuilder();

        Assert.That(() => builder.AddContentSeedAssembly(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void AddContentSeedAssembly_CollectsAndDeduplicates()
    {
        var (builder, _) = NewBuilder();
        var assembly = typeof(TestDto).Assembly;

        builder.AddContentSeedAssembly(assembly);
        builder.AddContentSeedAssembly(assembly);

        Assert.That(builder.ContentSeedAssemblies, Is.EqualTo(new[] { assembly }));
    }

    [Test]
    public void RegisterCatalogs_RegistersContentSeedCatalog()
    {
        var (builder, services) = NewBuilder();
        var assembly = typeof(TestDto).Assembly;
        builder.AddContentSeedFile("contentseed/site.json");
        builder.AddContentSeedAssembly(assembly);

        builder.RegisterCatalogs();

        var descriptor = services.Single(d => d.ServiceType == typeof(CmsContentSeedCatalog)
            && d.ImplementationInstance is CmsContentSeedCatalog);
        var catalog = (CmsContentSeedCatalog)descriptor.ImplementationInstance!;

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Files, Is.EqualTo(new[] { "contentseed/site.json" }));
            Assert.That(catalog.Assemblies, Is.EqualTo(new[] { assembly }));
        });
    }

    private sealed class TestDto : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
        public int Value { get; set; }
    }

    private sealed class TestViewModel
    {
        public int Value { get; set; }
    }

    private sealed class TestProfile : Profile
    {
        public TestProfile()
        {
            CreateMap<TestDto, TestViewModel>(s => new TestViewModel { Value = s.Value });
        }
    }

    private sealed class TestModelExtension : ICmsModelExtension
    {
        public void Configure(ModelBuilder modelBuilder) { }
    }

    private sealed class TestMigrationsContext : CmsExtensionDbContext<TestMigrationsContext>
    {
        public TestMigrationsContext(
            DbContextOptions<TestMigrationsContext> options,
            IEnumerable<ICmsModelExtension> modelExtensions)
            : base(options, modelExtensions)
        {
        }
    }
}
