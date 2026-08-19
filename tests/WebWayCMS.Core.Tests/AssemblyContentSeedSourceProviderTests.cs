using System.Reflection;

using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AssemblyContentSeedSourceProviderTests
{
    private static IOptions<ContentSeedOptions> OptionsOf() =>
        Options.Create(new ContentSeedOptions());

    private static IOptions<ContentSeedOptions> OptionsOf(string resourceSuffix) =>
        Options.Create(new ContentSeedOptions { ResourceSuffix = resourceSuffix });

    [Test]
    public void Constructor_NullAssemblies_Throws()
    {
        Assert.That(
            () => new AssemblyContentSeedSourceProvider(null!, OptionsOf()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullOptions_Throws()
    {
        Assert.That(
            () => new AssemblyContentSeedSourceProvider(Array.Empty<Assembly>(), null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetSources_AssemblyWithNoMatchingResources_ReturnsEmpty()
    {
        var provider = new AssemblyContentSeedSourceProvider(
            new[] { typeof(object).Assembly }, OptionsOf());

        Assert.That(provider.GetSources(), Is.Empty);
    }

    [Test]
    public void GetSources_EmptySuffix_ReturnsEmpty()
    {
        var provider = new AssemblyContentSeedSourceProvider(
            new[] { typeof(AssemblyContentSeedSourceProviderTests).Assembly }, OptionsOf(string.Empty));

        Assert.That(provider.GetSources(), Is.Empty);
    }

    [Test]
    public void GetSources_WhitespaceSuffix_ReturnsEmpty()
    {
        var provider = new AssemblyContentSeedSourceProvider(
            new[] { typeof(AssemblyContentSeedSourceProviderTests).Assembly }, OptionsOf("   "));

        Assert.That(provider.GetSources(), Is.Empty);
    }

    [Test]
    public void GetSources_AssemblyWithMatchingResource_ReadsIt()
    {
        var provider = new AssemblyContentSeedSourceProvider(
            new[] { typeof(AssemblyContentSeedSourceProviderTests).Assembly }, OptionsOf());

        var sources = provider.GetSources().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sources, Has.Count.EqualTo(1));
            Assert.That(sources[0].Name, Does.EndWith(".contentseed.json"));
            Assert.That(sources[0].Json, Does.Contain("\"items\""));
        });
    }

    [Test]
    public void GetSources_EnumerationFailure_IsSkipped()
    {
        var throwing = Substitute.For<Assembly>();
        throwing.GetManifestResourceNames().Returns(x => throw new InvalidOperationException("boom"));
        var provider = new AssemblyContentSeedSourceProvider(
            new[] { throwing, typeof(object).Assembly }, OptionsOf());

        Assert.That(provider.GetSources(), Is.Empty);
    }

    [Test]
    public void GetSources_NullStream_IsSkipped()
    {
        var asm = Substitute.For<Assembly>();
        asm.GetManifestResourceNames().Returns(new[] { "foo.contentseed.json" });
        asm.GetManifestResourceStream("foo.contentseed.json").Returns((Stream?)null);
        var provider = new AssemblyContentSeedSourceProvider(new[] { asm }, OptionsOf());

        Assert.That(provider.GetSources(), Is.Empty);
    }

    [Test]
    public void GetSources_SuffixIsCaseInsensitive()
    {
        var asm = Substitute.For<Assembly>();
        asm.GetManifestResourceNames().Returns(new[] { "foo.CONTENTSEED.JSON" });
        asm.GetManifestResourceStream("foo.CONTENTSEED.JSON")
            .Returns(new MemoryStream("{\"items\":[]}"u8.ToArray()));
        var provider = new AssemblyContentSeedSourceProvider(new[] { asm }, OptionsOf());

        var sources = provider.GetSources().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sources, Has.Count.EqualTo(1));
            Assert.That(sources[0].Json, Is.EqualTo("{\"items\":[]}"));
        });
    }
}
