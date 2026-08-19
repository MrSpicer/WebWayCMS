using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class FileContentSeedSourceProviderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "csf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static IWebHostEnvironment Env(string contentRoot)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot);
        return env;
    }

    private static ContentSeedOptions SeedOptions(string? path = null, bool enabled = true) =>
        new() { Enabled = enabled, Path = path ?? "contentseed" };

    private static IOptions<ContentSeedOptions> OptionsOf(ContentSeedOptions options) =>
        Options.Create(options);

    private static List<ContentSeedSource> GetSources(FileContentSeedSourceProvider provider) =>
        provider.GetSources().ToList();

    [Test]
    public void Constructor_NullEnv_Throws()
    {
        Assert.That(
            () => new FileContentSeedSourceProvider(null!, OptionsOf(SeedOptions()), Array.Empty<string>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullOptions_Throws()
    {
        Assert.That(
            () => new FileContentSeedSourceProvider(Env(_root), null!, Array.Empty<string>()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullExplicitFiles_Throws()
    {
        Assert.That(
            () => new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions()), null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetSources_MissingDirectory_ReturnsEmpty()
    {
        var provider = new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions("nope")), Array.Empty<string>());

        Assert.That(GetSources(provider), Is.Empty);
    }

    [Test]
    public void GetSources_ReadsJsonFilesSorted_AndIgnoresOtherExtensions()
    {
        var dir = Path.Combine(_root, "contentseed");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "b.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(dir, "a.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not json");

        var provider = new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions()), Array.Empty<string>());

        var sources = GetSources(provider);

        Assert.Multiple(() =>
        {
            Assert.That(sources.Count, Is.EqualTo(2));
            Assert.That(sources[0].Name, Does.EndWith("a.json"));
            Assert.That(sources[1].Name, Does.EndWith("b.json"));
            Assert.That(sources[0].Json, Is.EqualTo("{\"items\":[]}"));
        });
    }

    [Test]
    public void GetSources_AppendsExplicitFiles_ResolvingRelativePathsAgainstContentRoot()
    {
        var dir = Path.Combine(_root, "contentseed");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.json"), "{\"items\":[]}");

        var explicitAbs = Path.Combine(_root, "explicit.json");
        File.WriteAllText(explicitAbs, "{\"items\":[]}");

        var provider = new FileContentSeedSourceProvider(
            Env(_root), OptionsOf(SeedOptions()), new[] { "explicit.json", explicitAbs });

        var sources = GetSources(provider);

        Assert.Multiple(() =>
        {
            Assert.That(sources.Count, Is.EqualTo(3));
            Assert.That(sources[1].Name, Does.EndWith("explicit.json"));
            Assert.That(sources[2].Name, Does.EndWith("explicit.json"));
        });
    }

    [Test]
    public void GetSources_BlankPath_ScansNothing()
    {
        File.WriteAllText(Path.Combine(_root, "root.json"), "{\"items\":[]}");

        var provider = new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions("   ")), Array.Empty<string>());

        Assert.That(GetSources(provider), Is.Empty);
    }

    [Test]
    public void GetSources_BlankPath_StillReturnsExplicitFiles()
    {
        var explicitFile = Path.Combine(_root, "explicit.json");
        File.WriteAllText(explicitFile, "{\"items\":[]}");

        var provider = new FileContentSeedSourceProvider(
            Env(_root), OptionsOf(SeedOptions("   ")), new[] { "explicit.json" });

        var sources = GetSources(provider);

        Assert.That(sources, Has.Count.EqualTo(1));
        Assert.That(sources[0].Name, Does.EndWith("explicit.json"));
    }

    [Test]
    public void GetSources_UnreadableFile_IsSkippedWithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file-mode permissions required to force a file read error.");
            return;
        }

        var dir = Path.Combine(_root, "contentseed");
        Directory.CreateDirectory(dir);
        var locked = Path.Combine(dir, "locked.json");
        File.WriteAllText(locked, "{\"items\":[]}");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var denied = false;
            try { File.ReadAllText(locked); }
            catch { denied = true; }
            if (!denied)
                Assert.Ignore("Process can read the file (likely running as root).");

            var provider = new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions()), Array.Empty<string>());

            Assert.That(GetSources(provider), Is.Empty);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public void GetSources_UnreadableDirectory_IsSkippedWithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file-mode permissions required to force a directory read error.");
            return;
        }

        var locked = Path.Combine(_root, "contentseed");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var denied = false;
            try { Directory.GetFiles(locked); }
            catch { denied = true; }
            if (!denied)
                Assert.Ignore("Process can read the directory (likely running as root).");

            var provider = new FileContentSeedSourceProvider(Env(_root), OptionsOf(SeedOptions()), Array.Empty<string>());

            Assert.That(GetSources(provider), Is.Empty);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
