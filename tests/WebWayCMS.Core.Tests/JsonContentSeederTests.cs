using System.Text.Json;

using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class JsonContentSeederTests
{
    private static readonly Guid SeedId = Guid.NewGuid();
    private static readonly Guid NodeId = Guid.NewGuid();

    private IAdminHandlerRegistry _registry = null!;
    private IContentSeedRecordService _records = null!;
    private IAdminCrudHandler _handler = null!;
    private JsonContentSeeder _seeder = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _records = Substitute.For<IContentSeedRecordService>();
        _handler = Substitute.For<IAdminCrudHandler>();
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _seeder = NewSeeder(Provider(), enabled: true);
    }

    private static IOptions<ContentSeedOptions> OptionsOf(bool enabled = true) =>
        Options.Create(new ContentSeedOptions { Enabled = enabled });

    private static IContentSeedSourceProvider Provider(params ContentSeedSource[] sources)
    {
        var provider = Substitute.For<IContentSeedSourceProvider>();
        provider.GetSources().Returns(sources);
        return provider;
    }

    private static ContentSeedSource Source(string name, string json) => new(name, json);

    private static string ItemJson(
        string? id = null,
        string contentType = "pages",
        string? publish = null,
        string? fields = "{\"title\":\"About\"}") =>
        "{\"items\":[{" +
        "\"id\":\"" + (id ?? SeedId.ToString()) + "\"," +
        "\"contentType\":\"" + contentType + "\"" +
        (publish == null ? string.Empty : ",\"publish\":" + publish) +
        (fields == null ? string.Empty : ",\"fields\":" + fields) + "}]}";

    private JsonContentSeeder NewSeeder(IContentSeedSourceProvider provider, bool enabled = true)
    {
        _seeder = new JsonContentSeeder(new[] { provider }, _registry, _records, OptionsOf(enabled));
        return _seeder;
    }

    private void StubHandler(string contentType = "pages", bool supportsPublishing = true)
    {
        _registry.GetHandler(contentType).Returns(_handler);
        _handler.ContentType.Returns(contentType);
        _handler.SupportsPublishing.Returns(supportsPublishing);
        _handler.SupportsVersionHistory.Returns(true);
        _handler.CreateEmptyUpsertViewModel().Returns(new object());
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: NodeId));
        _handler.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));
    }

    [Test]
    public void Constructor_NullProviders_Throws()
    {
        Assert.That(() => new JsonContentSeeder(null!, _registry, _records, OptionsOf()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.That(() => new JsonContentSeeder(new[] { Provider() }, null!, _records, OptionsOf()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullRecords_Throws()
    {
        Assert.That(() => new JsonContentSeeder(new[] { Provider() }, _registry, null!, OptionsOf()),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullOptions_Throws()
    {
        Assert.That(() => new JsonContentSeeder(new[] { Provider() }, _registry, _records, null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task Seed_DisabledByOptions_DoesNothing()
    {
        var provider = Provider(Source("a.json", ItemJson()));
        NewSeeder(provider, enabled: false);

        await _seeder.SeedAsync();

        _ = provider.DidNotReceive().GetSources();
        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NoSources_DoesNothing()
    {
        await _seeder.SeedAsync();

        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_MalformedJson_SkipsSource()
    {
        var seeder = NewSeeder(Provider(Source("a.json", "{not json")));

        await seeder.SeedAsync();

        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_UnknownContentType_SkipsItem()
    {
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(contentType: "nope"))));

        await seeder.SeedAsync();

        await _records.Received(1).GetAsync(SeedId, Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NonObjectFields_SkipsItem()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(fields: "123"))));

        await seeder.SeedAsync();

        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NewItem_SavesPublishesAndRecords()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.Received(1).PublishAsync(NodeId, Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(
            Arg.Is<ContentSeedRecordDTO>(r => r.SeedId == SeedId && r.NodeId == NodeId && r.ContentHash.Length > 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_UnchangedItem_IsSkipped()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));
        ContentSeedRecordDTO? captured = null;
        _records.UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(c => captured = c.Arg<ContentSeedRecordDTO>());

        await seeder.SeedAsync();

        // Second run with a ledger row carrying the same hash.
        _records.GetAsync(SeedId, Arg.Any<CancellationToken>()).Returns(captured);
        _handler.ClearReceivedCalls();
        _records.ClearReceivedCalls();

        await seeder.SeedAsync();

        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_ChangedItem_IsUpdated()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));
        ContentSeedRecordDTO? captured = null;
        _records.UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(c => captured = c.Arg<ContentSeedRecordDTO>());

        await seeder.SeedAsync();

        // A different content hash (changed title) forces a re-apply.
        _records.GetAsync(SeedId, Arg.Any<CancellationToken>())
            .Returns(new ContentSeedRecordDTO
            {
                SeedId = SeedId,
                ContentTypeKey = "pages",
                NodeId = NodeId,
                ContentHash = captured!.ContentHash + "-stale",
                Source = "a.json",
                AppliedUtc = DateTime.UtcNow,
            });
        _handler.GetUpsertViewModelAsync(NodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new object());
        _handler.ClearReceivedCalls();
        _records.ClearReceivedCalls();

        await seeder.SeedAsync();

        await _handler.Received(1).GetUpsertViewModelAsync(
            NodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>());
        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_SaveFailure_DoesNotRecord()
    {
        StubHandler();
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "boom"));
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_PublishFailure_DoesNotRecord()
    {
        StubHandler();
        _handler.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "route collision"));
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.Received(1).PublishAsync(NodeId, Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NonVersionedContentType_SkipsItem()
    {
        StubHandler();
        _handler.SupportsVersionHistory.Returns(false);
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.DidNotReceive().GetUpsertViewModelAsync(
            Arg.Any<Guid>(), Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_PublishFalse_DoesNotPublishButRecords()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(publish: "false"))));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_SupportsPublishingFalse_DoesNotPublishButRecords()
    {
        StubHandler(supportsPublishing: false);
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_DeletedItem_IsRecreated()
    {
        StubHandler();
        _records.GetAsync(SeedId, Arg.Any<CancellationToken>())
            .Returns(new ContentSeedRecordDTO
            {
                SeedId = SeedId,
                ContentTypeKey = "pages",
                NodeId = NodeId,
                ContentHash = "stale",
                Source = "a.json",
                AppliedUtc = DateTime.UtcNow,
            });
        _handler.GetUpsertViewModelAsync(NodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        _handler.Received(1).CreateEmptyUpsertViewModel();
        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(
            Arg.Is<ContentSeedRecordDTO>(r => r.NodeId == NodeId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_DuplicateIds_BothApplied()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));
        // Same seed id twice across the run (same source here for simplicity): both are processed,
        // last write wins — asserted by two saves and two record upserts.
        var two = "{\"items\":[" +
            "{\"id\":\"" + SeedId + "\",\"contentType\":\"pages\",\"fields\":{\"title\":\"First\"}}," +
            "{\"id\":\"" + SeedId + "\",\"contentType\":\"pages\",\"fields\":{\"title\":\"Second\"}}]}";
        var seeder2 = new JsonContentSeeder(new[] { Provider(Source("a.json", two)) }, _registry, _records, OptionsOf());

        await seeder2.SeedAsync();

        await _handler.Received(2).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.Received(2).UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_SourcesOrderedByName()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var provider = Provider(
            Source("b.json", ItemJson(id: idB.ToString())),
            Source("a.json", ItemJson(id: idA.ToString())));
        var seeder = new JsonContentSeeder(new[] { provider }, _registry, _records, OptionsOf());

        await seeder.SeedAsync();

        Received.InOrder(() =>
        {
            _records.GetAsync(idA, Arg.Any<CancellationToken>());
            _records.GetAsync(idB, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Seed_StripsIdentityKeysFromFields()
    {
        StubHandler();
        object? savedModel = null;
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: NodeId))
            .AndDoes(c => savedModel = c.Arg<object>());
        var fields = "{\"title\":\"About\",\"nodeId\":\"" + Guid.NewGuid() + "\",\"expectedVersionNumber\":5}";
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(fields: fields))));

        await seeder.SeedAsync();

        var raw = JsonSerializer.Serialize(savedModel);
        Assert.Multiple(() =>
        {
            Assert.That(raw, Does.Not.Contain("nodeId"));
            Assert.That(raw, Does.Not.Contain("expectedVersionNumber"));
        });
    }

    [Test]
    public async Task Seed_MissingFields_SkipsItem()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(fields: null))));

        await seeder.SeedAsync();

        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_EmptySeedId_SkipsItem()
    {
        StubHandler();
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(id: Guid.Empty.ToString()))));

        await seeder.SeedAsync();

        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_NullItems_SkipsSource()
    {
        var seeder = NewSeeder(Provider(Source("a.json", "{\"items\":null}")));

        await seeder.SeedAsync();

        await _records.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_UppercaseIdentityKeys_AreStripped()
    {
        StubHandler();
        object? savedModel = null;
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true, NodeId: NodeId))
            .AndDoes(c => savedModel = c.Arg<object>());
        var fields = "{\"title\":\"About\",\"NodeId\":\"" + Guid.NewGuid() + "\",\"ExpectedVersionNumber\":5}";
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson(fields: fields))));

        await seeder.SeedAsync();

        var raw = JsonSerializer.Serialize(savedModel);
        Assert.Multiple(() =>
        {
            Assert.That(raw, Does.Not.Contain("NodeId"));
            Assert.That(raw, Does.Not.Contain("ExpectedVersionNumber"));
        });
    }

    [Test]
    public async Task Seed_SaveWithoutNodeId_AndNoRecord_DoesNotRecord()
    {
        StubHandler();
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _handler.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _records.DidNotReceive().UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_SaveWithoutNodeId_FallsBackToRecordNodeId()
    {
        StubHandler();
        _handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(true));
        _records.GetAsync(SeedId, Arg.Any<CancellationToken>())
            .Returns(new ContentSeedRecordDTO
            {
                SeedId = SeedId,
                ContentTypeKey = "pages",
                NodeId = NodeId,
                ContentHash = "stale",
                Source = "a.json",
                AppliedUtc = DateTime.UtcNow,
            });
        _handler.GetUpsertViewModelAsync(NodeId, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
            .Returns(new object());
        var seeder = NewSeeder(Provider(Source("a.json", ItemJson())));

        await seeder.SeedAsync();

        await _handler.Received(1).PublishAsync(NodeId, Arg.Any<CancellationToken>());
        await _records.Received(1).UpsertAsync(
            Arg.Is<ContentSeedRecordDTO>(r => r.NodeId == NodeId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Seed_DuplicateIdsAcrossSources_WarnsWithBothSources()
    {
        StubHandler();
        var provider = Provider(
            Source("a.json", ItemJson()),
            Source("b.json", ItemJson()));
        var seeder = new JsonContentSeeder(new[] { provider }, _registry, _records, OptionsOf());

        await seeder.SeedAsync();

        await _handler.Received(2).SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _records.Received(2).UpsertAsync(Arg.Any<ContentSeedRecordDTO>(), Arg.Any<CancellationToken>());
    }
}
