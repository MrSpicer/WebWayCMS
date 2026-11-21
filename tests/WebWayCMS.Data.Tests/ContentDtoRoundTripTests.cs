using System.Reflection;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// Every shipped <see cref="IVersionedContent"/> DTO must survive the content cache's JSON
/// round-trip (the default System.Text.Json serializer, exactly as <c>HybridContentReadCache</c>
/// stores it): the DTO graph is a tree (Version → Node, Relations, CustomFields) with no
/// back-references, and the in-memory-only members are excluded with [JsonIgnore].
/// </summary>
[TestFixture]
public class ContentDtoRoundTripTests
{
    private HybridCache _cache = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        _cache = _services.GetRequiredService<HybridCache>();
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    private static readonly Type[] ShippedDtos =
    {
        typeof(PageDTO),
        typeof(ArticleDTO),
        typeof(ArticleListDTO),
        typeof(ContentBlockDTO),
        typeof(ImageDTO),
        typeof(ContentZoneDTO),
        typeof(ContentZoneItemDTO),
        typeof(RouteDTO),
        typeof(WidgetRegistrationDTO),
        typeof(PageControllerRegistrationDTO),
        typeof(ContentTypeRegistrationDTO),
    };

    [Test]
    public async Task EveryShippedDto_RoundTrips()
    {
        foreach (var dtoType in ShippedDtos)
        {
            var method = typeof(ContentDtoRoundTripTests)
                .GetMethod(nameof(RoundTrips), BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.MakeGenericMethod(dtoType).Invoke(this, null)!;
        }
    }

    private async Task RoundTrips<T>() where T : class, IVersionedContent, new()
    {
        var original = CreatePopulated<T>();
        var key = $"roundtrip:{typeof(T).Name}";

        var cached = await _cache.GetOrCreateAsync(key, _ => new ValueTask<T?>(original), null, new[] { "t" }, default);
        var hit = await _cache.GetOrCreateAsync(key, _ => new ValueTask<T?>(CreatePopulated<T>()), null, new[] { "t" }, default);

        Assert.Multiple(() =>
        {
            Assert.That(cached, Is.Not.Null, $"first read of {typeof(T).Name}");
            Assert.That(cached!.VersionId, Is.EqualTo(original.VersionId));
            Assert.That(cached.Version.Title, Is.EqualTo(original.Version.Title));
            Assert.That(cached.Version.State, Is.EqualTo(original.Version.State));
            Assert.That(cached.Version.Node!.ContentTypeKey, Is.EqualTo("testtype"));
            Assert.That(cached.Version.Relations, Has.Count.EqualTo(1));
            Assert.That(cached.Version.Relations[0].Name, Is.EqualTo("TestRelation"));
            Assert.That(cached.Version.CustomFields, Has.Count.EqualTo(1));
            Assert.That(cached.Version.CustomFields[0].Value, Is.EqualTo("v"));
            // GetOrCreateAsync deserializes on read: the hit is a copy, not the original
            // instance — which is exactly what proves the value survived the round-trip.
            Assert.That(hit, Is.Not.SameAs(original), $"second read of {typeof(T).Name} must be a deserialized copy");
            Assert.That(hit!.VersionId, Is.EqualTo(original.VersionId));
            Assert.That(hit.Version.Relations[0].TargetNodeId, Is.EqualTo(original.Version.Relations[0].TargetNodeId));
        });
    }

    private static T CreatePopulated<T>() where T : class, IVersionedContent, new()
    {
        var version = new ContentVersion
        {
            Id = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            VersionNumber = 1,
            State = ContentVersionState.Published,
            IsCurrentDraft = true,
            Title = "t",
            Slug = "s",
            ChangeNote = "n",
            Node = new ContentNode
            {
                Id = Guid.NewGuid(),
                ContentTypeKey = "testtype",
                CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            CustomFields = { new CustomField { FieldName = "f", TypeName = "string", Value = "v" } },
            Relations =
            {
                new ContentRelation
                {
                    Id = Guid.NewGuid(),
                    SourceVersionId = Guid.NewGuid(),
                    TargetNodeId = Guid.NewGuid(),
                    Name = "TestRelation",
                    Kind = RelationKind.Containment,
                },
            },
        };
        return new T { VersionId = version.Id, Version = version };
    }
}
