using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentDeleteServiceTests
{
    private IContentRelations _relations = null!;
    private ICmsTransaction _transaction = null!;
    private IChangeSetScope _scope = null!;
    private ServiceCollection _services = null!;
    private Dictionary<Guid, RelationNode> _nodes = null!;
    private Dictionary<Guid, List<RelationReferrer>> _referrers = null!;
    private Dictionary<Guid, List<Guid>> _owned = null!;
    private List<Guid> _deleted = null!;

    [SetUp]
    public void SetUp()
    {
        _relations = Substitute.For<IContentRelations>();
        _transaction = Substitute.For<ICmsTransaction>();
        _scope = Substitute.For<IChangeSetScope>();
        _services = new ServiceCollection();
        _nodes = new();
        _referrers = new();
        _owned = new();
        _deleted = new();

        _relations.GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => _nodes);
        _relations.GetLiveReferrersAsync(Arg.Any<Guid>(), Arg.Any<RelationKind?>(), Arg.Any<CancellationToken>())
            .Returns(c => (IReadOnlyList<RelationReferrer>)_referrers.GetValueOrDefault(c.Arg<Guid>(), []));
        _relations.GetLiveChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => (IReadOnlyList<RelationReferrer>)_referrers.GetValueOrDefault(c.Arg<Guid>(), [])
                .Where(r => r.Kind == RelationKind.Containment).ToList());
        _relations.GetLiveOwnedTargetsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => (IReadOnlyList<Guid>)_owned.GetValueOrDefault(c.Arg<Guid>(), []));
        _transaction.RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));
    }

    private ContentDeleteService Service() => new(_relations, _transaction, _scope, _services.BuildServiceProvider());

    private Guid Node(string type, string title)
    {
        var id = Guid.NewGuid();
        _nodes[id] = new RelationNode(id, type, title);
        return id;
    }

    private void Child(Guid parent, Guid child)
        => Refer(parent, child, "Parent", RelationKind.Containment);

    private void Refer(Guid target, Guid source, string name, RelationKind kind, bool onDraft = true)
    {
        var node = _nodes.GetValueOrDefault(source);
        (_referrers.TryGetValue(target, out var list) ? list : _referrers[target] = [])
            .Add(new RelationReferrer(source, node?.ContentTypeKey ?? "unknown", node?.Title ?? "?", name, kind, onDraft, !onDraft));
    }

    private Func<Guid, CancellationToken, Task<bool>> Self(bool result = true)
        => (id, _) => { _deleted.Add(id); return Task.FromResult(result); };

    private void AddDeleter(string key, bool result = true)
    {
        var deleter = Substitute.For<IContentDeleter>();
        deleter.ContentTypeKey.Returns(key);
        deleter.DeleteSelfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => { _deleted.Add(c.Arg<Guid>()); return result; });
        _services.AddSingleton(deleter);
    }

    [Test]
    public void Constructor_And_NullDeleteSelf_Throw()
    {
        var sp = _services.BuildServiceProvider();
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentDeleteService(null!, _transaction, _scope, sp), Throws.ArgumentNullException);
            Assert.That(() => new ContentDeleteService(_relations, null!, _scope, sp), Throws.ArgumentNullException);
            Assert.That(() => new ContentDeleteService(_relations, _transaction, null!, sp), Throws.ArgumentNullException);
            Assert.That(() => new ContentDeleteService(_relations, _transaction, _scope, null!), Throws.ArgumentNullException);
            Assert.That(async () => await Service().DeleteAsync(Guid.NewGuid(), null!), Throws.ArgumentNullException);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DeleteAsync_LoneNode_RunsItsOwnDelete_WithoutATransaction(bool outcome)
    {
        var node = Node("pages", "Home");

        var result = await Service().DeleteAsync(node, Self(outcome));

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((outcome, (string?)null, 0)));
            Assert.That(_deleted, Is.EqualTo(new[] { node }));
            await _transaction.DidNotReceive().RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task DeleteAsync_Containment_DeletesDescendantsDeepestFirst_AndOwnedTargets_InOneTransaction()
    {
        var topic = Node("faqtopics", "Topic");
        var faqA = Node("faqs", "A");
        var faqB = Node("faqs", "B");
        var note = Node("notes", "Note under A");
        var route = Node("routes", "Owned route");
        Child(topic, faqA);
        Child(topic, faqB);
        Child(faqA, note);
        Child(faqB, faqA); // a second path to A must not list it twice
        _owned[topic] = [route, route];
        AddDeleter("faqs");
        AddDeleter("routes");
        var handlers = Substitute.For<IAdminHandlerRegistry>();
        var noteHandler = Substitute.For<IAdminCrudHandler, IContentDeleter>();
        ((IContentDeleter)noteHandler).ContentTypeKey.Returns("notes");
        ((IContentDeleter)noteHandler).DeleteSelfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => { _deleted.Add(c.Arg<Guid>()); return true; });
        handlers.All.Returns([noteHandler, Substitute.For<IAdminCrudHandler>()]);
        _services.AddSingleton(handlers);

        var result = await Service().DeleteAsync(topic, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null, 3)), "the owned route is not a descendant");
            Assert.That(_deleted.Last(), Is.EqualTo(topic));
            Assert.That(_deleted.IndexOf(note), Is.LessThan(_deleted.IndexOf(faqA)));
            Assert.That(_deleted, Is.EquivalentTo(new[] { topic, faqA, faqB, note, route }));
            await _transaction.Received(1).RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task DeleteAsync_RestrictedOrOwnedReferenceFromOutside_BlocksTheDelete()
    {
        var image = Node("images", "Logo");
        var page = Node("pages", "Home");
        var block = Node("contentblocks", "Footer");
        Refer(image, page, "config.ImageId", RelationKind.Reference);
        Refer(image, block, "Logo", RelationKind.Owned, onDraft: false);

        var result = await Service().DeleteAsync(image, Self());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("'Logo' is used by 2 item(s): 'Home', 'Footer'."));
            Assert.That(_deleted, Is.Empty);
        });
    }

    [Test]
    public async Task DeleteAsync_ReferenceFromInsideTheDeletionSet_DoesNotBlock()
    {
        var topic = Node("faqtopics", "Topic");
        var faq = Node("faqs", "A");
        Child(topic, faq);
        Refer(topic, faq, "Featured", RelationKind.Reference);
        AddDeleter("faqs");

        Assert.That((await Service().DeleteAsync(topic, Self())).Success, Is.True);
    }

    [Test]
    public async Task DeleteAsync_UnknownTargetTitle_FallsBackToItsId()
    {
        var target = Guid.NewGuid();
        Refer(target, Node("pages", "Home"), "Ref", RelationKind.Reference);

        var result = await Service().DeleteAsync(target, Self());

        Assert.That(result.ErrorMessage, Does.StartWith($"'{target}' is used by 1 item(s)"));
    }

    private IAdminCrudHandler DetachSetup(out Guid target, out Guid referrer, out FixtureFaqUpsertViewModel model, bool onDraft = true)
    {
        target = Node("faqs", "Old answer");
        referrer = Node("faqs", "Linking");
        var topic = Guid.NewGuid();
        Refer(target, referrer, nameof(FixtureFaqUpsertViewModel.RelatedFaqIds), RelationKind.Reference, onDraft);
        model = new FixtureFaqUpsertViewModel { Title = "Linking", FaqTopicId = topic, RelatedFaqIds = [target, Guid.NewGuid()] };

        // The registry reads the declaration from the empty model: here RelatedFaqIds is declared Detach.
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("faqs");
        handler.CreateEmptyUpsertViewModel().Returns(new DetachingFaqs());
        var vm = model;
        handler.GetUpsertViewModelAsync(referrer, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns(vm);
        handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
        var topicHandler = Substitute.For<IAdminCrudHandler>();
        topicHandler.ContentType.Returns("faqtopics");
        topicHandler.CreateEmptyUpsertViewModel().Returns(new FixtureFaqTopicUpsertViewModel());

        var registry = new HandlerRegistryStub([handler, topicHandler]);
        _services.AddSingleton<IAdminHandlerRegistry>(registry);
        _services.AddSingleton(new ContentRelationRegistry(registry));
        return handler;
    }

    /// <summary>The fixture FAQ's RelatedFaqIds, but declared Detach.</summary>
    private sealed class DetachingFaqs
    {
        [ContentRelation("faqs", OnTargetDelete = RelationDeletePolicy.Detach)]
        public List<Guid> RelatedFaqIds { get; set; } = [];
    }

    [Test]
    public async Task DeleteAsync_DetachedReference_SavesTheReferrerWithoutIt_ThenDeletes()
    {
        var handler = DetachSetup(out var target, out var referrer, out var model);
        var kept = model.RelatedFaqIds[1];

        var result = await Service().DeleteAsync(target, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null, 0)));
            Assert.That(model.RelatedFaqIds, Is.EqualTo(new[] { kept }));
            Assert.That(_deleted, Is.EqualTo(new[] { target }));
            await handler.Received(1).SaveUpsertAsync(model, Arg.Any<CancellationToken>());
            _scope.Received(1).UseChangeNote("Removed reference to Old answer");
        });
    }

    [Test]
    public async Task DeleteAsync_DetachedReferenceOnlyOnThePublishedVersion_SavesNothing()
    {
        var handler = DetachSetup(out var target, out _, out _, onDraft: false);

        var result = await Service().DeleteAsync(target, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            await handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task DeleteAsync_DetachSaveFails_RollsBackWithItsReason()
    {
        var handler = DetachSetup(out var target, out _, out _);
        handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "stale"), new AdminSaveResult(false));

        var withReason = await Service().DeleteAsync(target, Self());
        var withoutReason = await Service().DeleteAsync(target, Self());

        Assert.Multiple(() =>
        {
            Assert.That(withReason, Is.EqualTo((false, (string?)"stale", 0)));
            Assert.That(withoutReason.ErrorMessage, Is.EqualTo("'Linking' could not be saved."));
            Assert.That(_deleted, Is.Empty);
        });
    }

    [Test]
    public async Task DeleteAsync_DetachReferrerCannotBeLoaded_Fails()
    {
        var handler = DetachSetup(out var target, out var referrer, out _);
        handler.GetUpsertViewModelAsync(referrer, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns((object?)null);

        var result = await Service().DeleteAsync(target, Self());

        Assert.That(result.ErrorMessage, Is.EqualTo("'Linking' could not be updated to drop its reference to 'Old answer'."));
    }

    [Test]
    public async Task DeleteAsync_DetachReferrerHasNoHandler_Fails()
    {
        DetachSetup(out var target, out var referrer, out _);
        _services.AddSingleton<IAdminHandlerRegistry>(new HandlerRegistryStub([]));

        var result = await Service().DeleteAsync(target, Self());

        Assert.That(result.ErrorMessage, Does.StartWith("'Linking' could not be updated"));
    }

    // ─── config relation detach (phase 3a, fix 2) ─────────────────────────────

    /// <summary>A widget / page configuration whose FAQ reference is detached when the FAQ is deleted.</summary>
    public sealed class DetachingConfig
    {
        [ContentRelation("faqs", OnTargetDelete = RelationDeletePolicy.Detach)]
        public List<Guid> FaqIds { get; set; } = [];
    }

    private const string ConfigFaqs = "config.FaqIds";

    /// <summary>Declares <see cref="DetachingConfig"/> on zone items and pages; returns the page handler.</summary>
    private IAdminCrudHandler ConfigSetup()
    {
        var faqs = Substitute.For<IAdminCrudHandler>();
        faqs.ContentType.Returns("faqs");
        faqs.CreateEmptyUpsertViewModel().Returns(new FixtureFaqTopicUpsertViewModel());
        var pages = Substitute.For<IAdminCrudHandler>();
        pages.ContentType.Returns("pages");
        pages.CreateEmptyUpsertViewModel().Returns(new WebWayCMS.Models.Page.PageUpsertViewModel());
        pages.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));

        var widgets = Substitute.For<ContentZones.IWidgetRegistry>();
        widgets.GetAllComponents().Returns([new ContentZones.WidgetRegistrationInfo
            { Name = "FaqWidget", ConfigurationTypeName = typeof(DetachingConfig).AssemblyQualifiedName }]);
        var controllers = Substitute.For<Pages.IPageControllerRegistry>();
        controllers.GetAllControllers().Returns([new Pages.PageControllerInfo
            { Name = "FaqPage", ConfigurationType = typeof(DetachingConfig) }]);

        var routes = Substitute.For<IAdminCrudHandler>();
        routes.ContentType.Returns("routes");
        routes.CreateEmptyUpsertViewModel().Returns(new FixtureFaqTopicUpsertViewModel());

        var registry = new HandlerRegistryStub([faqs, pages, routes]);
        _services.AddSingleton<IAdminHandlerRegistry>(registry);
        _services.AddSingleton(new ContentRelationRegistry(registry, widgets, controllers));
        return pages;
    }

    private ContentZoneItemDTO ZoneItem(Guid nodeId, params Guid[] faqs)
    {
        var item = new ContentZoneItemDTO
        {
            ComponentName = "FaqWidget",
            ComponentPropertiesJson = "{\"title\":\"x\"}",
            MetadataJson = "{}",
            IsActive = true,
            Version = new ContentVersion { Node = new ContentNode { Id = nodeId }, VersionNumber = 4 }
        };
        item.Version.Relations = faqs.Select((f, i) => new ContentRelation
            { Name = ConfigFaqs, TargetNodeId = f, Ordinal = i, Kind = RelationKind.Reference }).ToList();
        return item;
    }

    [Test]
    public async Task DeleteAsync_DetachedWidgetConfigRelation_SavesTheZoneItemWithoutIt_ThenDeletes()
    {
        ConfigSetup();
        var target = Node("faqs", "Old answer");
        var item = Node(ContentRelationRegistry.ContentZoneItemContentType, "FaqWidget");
        var kept = Guid.NewGuid();
        Refer(target, item, ConfigFaqs, RelationKind.Reference);
        var zones = Substitute.For<IContentZoneService>();
        zones.GetItemByNodeIdAsync(item, Arg.Any<CancellationToken>()).Returns(ZoneItem(item, target, kept));
        ContentZoneItemDTO? saved = null;
        zones.UpdateItemAsync(Arg.Do<ContentZoneItemDTO>(i => saved = i), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(true);
        _services.AddSingleton(zones);

        var result = await Service().DeleteAsync(target, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null, 0)));
            Assert.That(_deleted, Is.EqualTo(new[] { target }));
            Assert.That(saved!.Version.TargetsOf(ConfigFaqs), Is.EqualTo(new[] { kept }));
            Assert.That(saved.ComponentPropertiesJson, Is.EqualTo("{\"title\":\"x\"}"), "content untouched");
            await zones.Received(1).UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), 4);
        });
    }

    [Test]
    public async Task DeleteAsync_WidgetConfigDetach_ItemMissingOrSaveFails_RollsBack()
    {
        ConfigSetup();
        var target = Node("faqs", "Old answer");
        var item = Node(ContentRelationRegistry.ContentZoneItemContentType, "FaqWidget");
        Refer(target, item, ConfigFaqs, RelationKind.Reference);
        var zones = Substitute.For<IContentZoneService>();
        zones.GetItemByNodeIdAsync(item, Arg.Any<CancellationToken>()).Returns((ContentZoneItemDTO?)null, ZoneItem(item, target));
        zones.UpdateItemAsync(Arg.Any<ContentZoneItemDTO>(), Arg.Any<CancellationToken>(), Arg.Any<int?>()).Returns(false);
        _services.AddSingleton(zones);

        var missing = await Service().DeleteAsync(target, Self());
        var failed = await Service().DeleteAsync(target, Self());

        Assert.Multiple(() =>
        {
            Assert.That(missing.ErrorMessage, Is.EqualTo("'FaqWidget' could not be updated to drop its reference to 'Old answer'."));
            Assert.That(failed.ErrorMessage, Is.EqualTo("'FaqWidget' could not be saved."));
            Assert.That(_deleted, Is.Empty);
        });
    }

    [Test]
    public async Task DeleteAsync_WidgetConfigDetach_NoZoneService_Fails()
    {
        ConfigSetup();
        var target = Node("faqs", "Old answer");
        var item = Node(ContentRelationRegistry.ContentZoneItemContentType, "FaqWidget");
        Refer(target, item, ConfigFaqs, RelationKind.Reference);

        var result = await Service().DeleteAsync(target, Self());

        Assert.That(result.ErrorMessage, Does.StartWith("'FaqWidget' could not be updated"));
    }

    [Test]
    public async Task DeleteAsync_DetachedPageConfigRelation_SavesThePageWithoutIt_ThenDeletes()
    {
        var pages = ConfigSetup();
        var target = Node("faqs", "Old answer");
        var page = Node("pages", "Help");
        var kept = Guid.NewGuid();
        Refer(target, page, ConfigFaqs, RelationKind.Reference);
        var model = new WebWayCMS.Models.Page.PageUpsertViewModel
        {
            ControllerName = "FaqPage",
            ConfigurationJson = $"{{\"heading\":\"Hi\",\"faqIds\":[\"{target}\",\"{kept}\"]}}"
        };
        pages.GetUpsertViewModelAsync(page, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns(model);

        var result = await Service().DeleteAsync(target, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(model.ConfigurationJson, Does.Contain(kept.ToString()).And.Not.Contain(target.ToString()));
            Assert.That(model.ConfigurationJson, Does.Contain("\"heading\":\"Hi\""));
            await pages.Received(1).SaveUpsertAsync(model, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task DeleteAsync_PageConfigDetach_PageCannotBeLoaded_Fails()
    {
        var pages = ConfigSetup();
        var target = Node("faqs", "Old answer");
        var page = Node("pages", "Help");
        Refer(target, page, ConfigFaqs, RelationKind.Reference);
        pages.GetUpsertViewModelAsync(page, Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>()).Returns((object?)null);

        var result = await Service().DeleteAsync(target, Self());

        Assert.That(result.ErrorMessage, Is.EqualTo("'Help' could not be updated to drop its reference to 'Old answer'."));
    }

    [Test]
    public async Task DeleteAsync_DescendantWithoutADeleter_FailsBeforeWriting()
    {
        var topic = Node("faqtopics", "Topic");
        var faq = Node("faqs", "A");
        var ghost = Guid.NewGuid();
        Child(topic, faq);

        var noDeleter = await Service().DeleteAsync(topic, Self());
        _referrers.Clear();
        Child(topic, ghost);
        var unknownNode = await Service().DeleteAsync(topic, Self());

        Assert.Multiple(async () =>
        {
            Assert.That(noDeleter.ErrorMessage, Is.EqualTo("No deleter registered for 'faqs'."));
            Assert.That(unknownNode.ErrorMessage, Is.EqualTo($"No deleter registered for '{ghost}'."));
            Assert.That(_deleted, Is.Empty);
            await _transaction.DidNotReceive().RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DeleteAsync_AFailedDelete_RollsTheWholeDeleteBack(bool childFails)
    {
        var topic = Node("faqtopics", "Topic");
        var faq = Node("faqs", "A");
        Child(topic, faq);
        AddDeleter("faqs", result: !childFails);

        var result = await Service().DeleteAsync(topic, Self(result: childFails));

        Assert.That(result, Is.EqualTo((false, (string?)$"'{(childFails ? "A" : "Topic")}' could not be deleted.", 0)));
    }
}
