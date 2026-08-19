using System.Text.Json.Nodes;

using NUnit.Framework;

using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class SeedReferenceResolverTests
{
    private static readonly Guid SeedA = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid SeedB = Guid.Parse("22222222-0000-4000-8000-000000000002");
    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [Test]
    public void CollectReferences_WholeValueToken_ReturnsId()
    {
        var node = JsonNode.Parse("{\"featuredFaqId\":\"@seed:11111111-0000-4000-8000-000000000001\"}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.EquivalentTo(new[] { SeedA }));
    }

    [Test]
    public void CollectReferences_TokenInEmbeddedJsonString_ReturnsId()
    {
        var node = JsonNode.Parse(
            "{\"configurationJson\":\"{\\\"featuredFaqId\\\":\\\"@seed:11111111-0000-4000-8000-000000000001\\\"}\"}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.EquivalentTo(new[] { SeedA }));
    }

    [Test]
    public void CollectReferences_TokenInNestedObjectAndArrayElement_ReturnsAll()
    {
        var node = JsonNode.Parse(
            "{\"a\":{\"b\":\"@seed:11111111-0000-4000-8000-000000000001\"}," +
            "\"c\":[\"@seed:22222222-0000-4000-8000-000000000002\"]}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.EquivalentTo(new[] { SeedA, SeedB }));
    }

    [Test]
    public void CollectReferences_SeveralTokensInOneString_ReturnsDistinct()
    {
        var node = JsonNode.Parse(
            "{\"s\":\"@seed:11111111-0000-4000-8000-000000000001 and @seed:22222222-0000-4000-8000-000000000002\"}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.EquivalentTo(new[] { SeedA, SeedB }));
    }

    [Test]
    public void CollectReferences_NoToken_ReturnsEmpty()
    {
        var node = JsonNode.Parse("{\"title\":\"About\",\"n\":5,\"b\":true,\"x\":null}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void CollectReferences_NonStringValues_AreIgnored()
    {
        var node = JsonNode.Parse("{\"n\":123,\"b\":false,\"x\":null}");

        var ids = SeedReferenceResolver.CollectReferences(node);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void CollectReferences_NullNode_ReturnsEmpty()
    {
        Assert.That(SeedReferenceResolver.CollectReferences(null), Is.Empty);
    }

    [Test]
    public void Substitute_WholeValueToken_ReplacesWithNodeId()
    {
        var overlay = (JsonObject)JsonNode.Parse(
            "{\"featuredFaqId\":\"@seed:11111111-0000-4000-8000-000000000001\"}")!;

        var unresolved = SeedReferenceResolver.Substitute(
            overlay, new Dictionary<Guid, Guid> { [SeedA] = NodeA });

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["featuredFaqId"]!.GetValue<string>(), Is.EqualTo(NodeA.ToString()));
        });
    }

    [Test]
    public void Substitute_TokenInEmbeddedJsonString_ReplacesInPlace()
    {
        var overlay = (JsonObject)JsonNode.Parse(
            "{\"configurationJson\":\"{\\\"featuredFaqId\\\":\\\"@seed:11111111-0000-4000-8000-000000000001\\\"}\"}")!;

        var unresolved = SeedReferenceResolver.Substitute(
            overlay, new Dictionary<Guid, Guid> { [SeedA] = NodeA });

        var config = overlay["configurationJson"]!.GetValue<string>();
        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(config, Does.Contain(NodeA.ToString()));
            Assert.That(config, Does.Not.Contain("@seed:"));
        });
    }

    [Test]
    public void Substitute_TokenInNestedObjectAndArrayElement_ReplacesBoth()
    {
        var overlay = (JsonObject)JsonNode.Parse(
            "{\"a\":{\"b\":\"@seed:11111111-0000-4000-8000-000000000001\"}," +
            "\"c\":[\"@seed:22222222-0000-4000-8000-000000000002\"]}")!;

        var unresolved = SeedReferenceResolver.Substitute(
            overlay, new Dictionary<Guid, Guid>
            {
                [SeedA] = NodeA,
                [SeedB] = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"),
            });

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["a"]!["b"]!.GetValue<string>(), Is.EqualTo(NodeA.ToString()));
            Assert.That(overlay["c"]![0]!.GetValue<string>(), Is.EqualTo("bbbbbbbb-0000-4000-8000-000000000002"));
        });
    }

    [Test]
    public void Substitute_SeveralTokensInOneString_ReplacesAll()
    {
        var overlay = (JsonObject)JsonNode.Parse(
            "{\"s\":\"@seed:11111111-0000-4000-8000-000000000001 and @seed:22222222-0000-4000-8000-000000000002\"}")!;

        var unresolved = SeedReferenceResolver.Substitute(
            overlay, new Dictionary<Guid, Guid>
            {
                [SeedA] = NodeA,
                [SeedB] = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"),
            });

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["s"]!.GetValue<string>(), Is.EqualTo($"{NodeA} and bbbbbbbb-0000-4000-8000-000000000002"));
        });
    }

    [Test]
    public void Substitute_NoToken_StringPassesThrough()
    {
        var overlay = (JsonObject)JsonNode.Parse("{\"title\":\"About\"}")!;

        var unresolved = SeedReferenceResolver.Substitute(overlay, new Dictionary<Guid, Guid>());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["title"]!.GetValue<string>(), Is.EqualTo("About"));
        });
    }

    [Test]
    public void Substitute_NonStringValues_AreUntouched()
    {
        var overlay = (JsonObject)JsonNode.Parse("{\"n\":5,\"b\":true}")!;

        var unresolved = SeedReferenceResolver.Substitute(overlay, new Dictionary<Guid, Guid>());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["n"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(overlay["b"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public void Substitute_NullValue_IsPreserved()
    {
        var overlay = (JsonObject)JsonNode.Parse("{\"title\":\"About\",\"x\":null}")!;

        var unresolved = SeedReferenceResolver.Substitute(overlay, new Dictionary<Guid, Guid>());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.Empty);
            Assert.That(overlay["title"]!.GetValue<string>(), Is.EqualTo("About"));
            Assert.That(overlay["x"], Is.Null);
        });
    }

    [Test]
    public void Substitute_UnresolvedId_IsReportedAndLeftInPlace()
    {
        var overlay = (JsonObject)JsonNode.Parse(
            "{\"featuredFaqId\":\"@seed:11111111-0000-4000-8000-000000000001\"}")!;

        var unresolved = SeedReferenceResolver.Substitute(overlay, new Dictionary<Guid, Guid>());

        Assert.Multiple(() =>
        {
            Assert.That(unresolved, Is.EquivalentTo(new[] { SeedA }));
            Assert.That(overlay["featuredFaqId"]!.GetValue<string>(), Is.EqualTo("@seed:" + SeedA));
        });
    }
}
