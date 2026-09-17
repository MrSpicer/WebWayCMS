using System.Text.Json.Nodes;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class SeedMediaResolverTests
{
    private IMediaLibrary _library = null!;
    private SeedMediaResolver _resolver = null!;
    private string _dir = null!;

    private const long MaxBytes = 1024 * 1024;

    [SetUp]
    public void SetUp()
    {
        _library = Substitute.For<IMediaLibrary>();
        _resolver = new SeedMediaResolver(_library, MaxBytes);
        _dir = Path.Combine(Path.GetTempPath(), "wwcms-seedmedia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A minimal but structurally valid 2x2 PNG header.</summary>
    private static byte[] Png(int width = 2, int height = 2)
    {
        var b = new byte[24];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(sig, b, sig.Length);
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[19] = (byte)width;
        b[23] = (byte)height;
        return b;
    }

    private string WritePng(string name, byte[]? content = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content ?? Png());
        return path;
    }

    private ContentSeedSource FileSource() =>
        new(Path.Combine(_dir, "seed.json"), "{}");

    private void LibraryAccepts(string hash = "deadbeef") =>
        _library.AddAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null, new MediaBlobDTO { Hash = hash }));

    [Test]
    public void Constructor_NullLibrary_Throws() =>
        Assert.That(() => new SeedMediaResolver(null!, MaxBytes), Throws.ArgumentNullException);

    [Test]
    public void CollectReferences_FindsTokensAcrossObjectsArraysAndNestedStrings()
    {
        var node = JsonNode.Parse("""
        {
          "a": "@media:one.png",
          "b": ["@media:two.png", 5, null],
          "c": { "d": "prefix @media:three.png suffix" },
          "e": "no token here"
        }
        """);

        var refs = SeedMediaResolver.CollectReferences(node);

        Assert.That(refs, Is.EquivalentTo(new[] { "one.png", "two.png", "three.png" }));
    }

    [Test]
    public void CollectReferences_NullNode_IsEmpty() =>
        Assert.That(SeedMediaResolver.CollectReferences(null), Is.Empty);

    [Test]
    public async Task SubstituteAsync_NoTokens_DoesNothing()
    {
        var overlay = (JsonObject)JsonNode.Parse("""{"title":"plain"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["title"]!.GetValue<string>(), Is.EqualTo("plain"));
        });
    }

    [Test]
    public void SubstituteAsync_NullArguments_Throw()
    {
        var overlay = new JsonObject();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await _resolver.SubstituteAsync(null!, FileSource()), Throws.ArgumentNullException);
            Assert.That(async () => await _resolver.SubstituteAsync(overlay, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task SubstituteAsync_ResolvesFileRelativeToSeedFile()
    {
        WritePng("logo.png");
        LibraryAccepts("hash123");
        var overlay = (JsonObject)JsonNode.Parse("""{"blobHash":"@media:logo.png"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["blobHash"]!.GetValue<string>(), Is.EqualTo("hash123"));
        });
    }

    [Test]
    public async Task SubstituteAsync_ReplacesTokenEmbeddedInASerializedJsonString()
    {
        WritePng("logo.png");
        LibraryAccepts("abc");
        var overlay = (JsonObject)JsonNode.Parse("""{"configurationJson":"{\"img\":\"@media:logo.png\"}"}""")!;

        await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.That(overlay["configurationJson"]!.GetValue<string>(), Does.Contain("abc"));
    }

    [Test]
    public async Task SubstituteAsync_MissingFile_IsReportedUnresolvedAndLeavesTokenIntact()
    {
        var overlay = (JsonObject)JsonNode.Parse("""{"blobHash":"@media:absent.png"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.EqualTo(new[] { "absent.png" }));
            Assert.That(overlay["blobHash"]!.GetValue<string>(), Is.EqualTo("@media:absent.png"));
        });
    }

    [Test]
    public async Task SubstituteAsync_FileFailingValidation_IsUnresolved()
    {
        WritePng("bad.png", "not an image at all"u8.ToArray());
        var overlay = (JsonObject)JsonNode.Parse("""{"blobHash":"@media:bad.png"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.That(unresolved, Is.EqualTo(new[] { "bad.png" }));
    }

    [Test]
    public async Task SubstituteAsync_MediaLibraryRejection_IsUnresolved()
    {
        WritePng("logo.png");
        _library.AddAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)"nope", (MediaBlobDTO?)null));
        var overlay = (JsonObject)JsonNode.Parse("""{"blobHash":"@media:logo.png"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.That(unresolved, Is.EqualTo(new[] { "logo.png" }));
    }

    [Test]
    public async Task SubstituteAsync_WalksArraysAndLeavesNonStringValuesAlone()
    {
        WritePng("logo.png");
        LibraryAccepts("h1");
        var overlay = (JsonObject)JsonNode.Parse("""
        {
          "items": ["@media:logo.png", 42, true, null],
          "count": 7,
          "nested": { "deep": ["@media:logo.png"] }
        }
        """)!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["items"]![0]!.GetValue<string>(), Is.EqualTo("h1"));
            Assert.That(overlay["items"]![1]!.GetValue<int>(), Is.EqualTo(42));
            Assert.That(overlay["items"]![2]!.GetValue<bool>(), Is.True);
            Assert.That(overlay["items"]![3], Is.Null);
            Assert.That(overlay["count"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(overlay["nested"]!["deep"]![0]!.GetValue<string>(), Is.EqualTo("h1"));
        });
    }

    [Test]
    public async Task SubstituteAsync_LeavesUnresolvedTokensInPlaceWhileReplacingResolvedOnes()
    {
        WritePng("good.png");
        LibraryAccepts("good-hash");
        var overlay = (JsonObject)JsonNode.Parse(
            """{"a":"@media:good.png","b":"@media:missing.png"}""")!;

        var unresolved = await _resolver.SubstituteAsync(overlay, FileSource());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.EqualTo(new[] { "missing.png" }));
            Assert.That(overlay["a"]!.GetValue<string>(), Is.EqualTo("good-hash"));
            Assert.That(overlay["b"]!.GetValue<string>(), Is.EqualTo("@media:missing.png"));
        });
    }

    [Test]
    public void ReadBytes_PathEscapingTheSeedDirectory_IsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "wwcms-outside-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(outside, Png());
        try
        {
            var bytes = SeedMediaResolver.ReadBytes("../" + Path.GetFileName(outside), FileSource());
            Assert.That(bytes, Is.Null);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Test]
    public void ReadBytes_SourceNameWithoutADirectory_ReturnsNull() =>
        Assert.That(SeedMediaResolver.ReadBytes("a.png", new ContentSeedSource("seed.json", "{}")), Is.Null);

    [Test]
    public void ReadBytes_EmbeddedResourceMatchedBySuffix()
    {
        var assembly = typeof(SeedMediaResolverTests).Assembly;
        var known = assembly.GetManifestResourceNames().FirstOrDefault();

        if (known == null)
            Assert.Ignore("The test assembly embeds no manifest resources.");

        var bytes = SeedMediaResolver.ReadBytes(known!, new ContentSeedSource("res", "{}", assembly));
        Assert.That(bytes, Is.Not.Null);
    }

    [Test]
    public void ReadBytes_EmbeddedResourceNotFound_ReturnsNull() =>
        Assert.That(
            SeedMediaResolver.ReadBytes("definitely-not-here.png",
                new ContentSeedSource("res", "{}", typeof(SeedMediaResolverTests).Assembly)),
            Is.Null);
}
