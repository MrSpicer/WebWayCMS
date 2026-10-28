using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>An in-memory <see cref="IAdminHandlerRegistry"/> (the real one lives in the Admin project).</summary>
internal sealed class HandlerRegistryStub(IEnumerable<IAdminCrudHandler> handlers) : IAdminHandlerRegistry
{
    private readonly List<IAdminCrudHandler> _handlers = handlers.ToList();

    public void Add(IAdminCrudHandler handler) => _handlers.Add(handler);

    public IAdminCrudHandler? GetHandler(string contentType)
        => _handlers.FirstOrDefault(h => string.Equals(h.ContentType, contentType, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<IAdminCrudHandler> All => _handlers;
}

// ─── a host-style content type pair: Faq → FaqTopic, declared with one attribute ───

public sealed class FixtureFaqDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
}

public sealed class FixtureFaqUpsertViewModel : BaseContentViewModel
{
    [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment, Required = true)]
    [FormProperty(Label = "Topic", Order = 3)]
    public Guid FaqTopicId { get; init; }

    [ContentRelation("faqs")]
    public List<Guid> RelatedFaqIds { get; init; } = [];

    [FormProperty(Label = "Answer", Order = 4)]
    public string Answer { get; init; } = string.Empty;
}

public sealed class FixtureFaqTopicUpsertViewModel : BaseContentViewModel;

public sealed class FixtureFaqProfile : Profile
{
    public FixtureFaqProfile()
    {
        // No relation code: the mapper's relation step moves FaqTopicId / RelatedFaqIds.
        CreateMap<FixtureFaqUpsertViewModel, FixtureFaqDTO>(s => new FixtureFaqDTO
        {
            Answer = s.Answer,
            Version = new ContentVersion { Title = s.Title, Node = new ContentNode { Id = s.NodeId ?? Guid.Empty } }
        });
        CreateMap<FixtureFaqDTO, FixtureFaqUpsertViewModel>(s => new FixtureFaqUpsertViewModel
        {
            NodeId = s.Version.NodeId,
            Title = s.Version.Title,
            Answer = s.Answer
        });
    }
}

/// <summary>A host-style model: nothing relation-specific beyond the constructor parameters.</summary>
public sealed class FixtureFaqModel : AdminCrudModel<FixtureFaqDTO>
{
    private readonly IContentStore<FixtureFaqDTO> _store;
    private readonly IMapper _mapper;

    public FixtureFaqModel(IContentStore<FixtureFaqDTO> store, IMapper mapper, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store;
        _mapper = mapper;
    }

    protected override IContentStore<FixtureFaqDTO> Store => _store;
    protected override string VersionHistoryContentType => "faqs";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/faqs";
    protected override Task<List<FixtureFaqDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "faqs";
    public override string DisplayName => "FAQ";
    public override string IndexViewPath => "i";
    public override string UpsertViewPath => "u";

    public override Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => id is { } nodeId && await _store.GetCurrentDraftAsync(nodeId, ct) is { } dto
            ? _mapper.Map<FixtureFaqUpsertViewModel>(dto)
            : null;

    public override object CreateEmptyUpsertViewModel() => new FixtureFaqUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (FixtureFaqUpsertViewModel)model;
        var result = await _store.SaveDraftAsync(_mapper.Map<FixtureFaqDTO>(vm), vm.ExpectedVersionNumber, ct);
        return new AdminSaveResult(result.Success, result.ErrorMessage, NodeId: result.NodeId);
    }

    protected override Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default) => _store.DeleteAsync(id, softDelete: false, ct);

    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
}

[TestFixture]
public class RelationMappingAndKindTests
{
    [Test]
    public void RelationKindMap_SharesEveryValueByName()
    {
        Assert.Multiple(() =>
        {
            foreach (var kind in Enum.GetValues<ContentRelationKind>())
            {
                Assert.That(kind.ToData().ToString(), Is.EqualTo(kind.ToString()));
                Assert.That(kind.ToData().ToDeclaration(), Is.EqualTo(kind));
            }
        });
    }

    [Test]
    public void Mapper_WritesRelationsFromAViewModel_AndFillsThemBack()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile(new FixtureFaqProfile())).CreateMapper();
        var topic = Guid.NewGuid();
        var related = Guid.NewGuid();

        var dto = mapper.Map<FixtureFaqDTO>(new FixtureFaqUpsertViewModel { Title = "Q", FaqTopicId = topic, RelatedFaqIds = [related] });
        var cleared = mapper.Map<FixtureFaqDTO>(new FixtureFaqUpsertViewModel { Title = "Q" });
        dto.Version.Relations = dto.Version.EffectiveRelations().ToList();
        dto.Version.RelationWrites = new Dictionary<string, IReadOnlyList<RelationWrite>>();
        var back = mapper.Map<FixtureFaqUpsertViewModel>(dto);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.TargetOf(nameof(FixtureFaqUpsertViewModel.FaqTopicId)), Is.EqualTo(topic));
            Assert.That(dto.Version.EffectiveRelations().Single(r => r.Name == "FaqTopicId").Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(dto.Version.TargetsOf(nameof(FixtureFaqUpsertViewModel.RelatedFaqIds)), Is.EqualTo(new[] { related }));
            Assert.That(cleared.Version.RelationWrites.Keys, Is.EquivalentTo(new[] { "FaqTopicId", "RelatedFaqIds" }), "an empty property still writes (clears) its name");
            Assert.That(cleared.Version.RelationWrites.Values, Is.All.Empty);
            Assert.That(back.FaqTopicId, Is.EqualTo(topic));
            Assert.That(back.RelatedFaqIds, Is.EqualTo(new[] { related }));
        });
    }

    [Test]
    public void Mapper_LeavesTypesWithoutRelationsAlone()
    {
        var mapper = TestSupport.CreateMapper();
        var dto = mapper.Map<ContentBlockDTO>(new Models.ContentBlock.ContentBlockUpsertViewModel { Title = "T", Content = "c" });

        Assert.That(dto.Version.RelationWrites, Is.Empty);
    }
}

[TestFixture]
public class RelationBinderTests
{
    private IContentRelations _relations = null!;
    private Dictionary<Guid, RelationNode> _nodes = null!;
    private RelationBinder _binder = null!;

    private sealed class Unlabelled
    {
        [ContentRelation("faqs", Required = true)]
        public Guid RelatedFaq { get; set; }
    }

    [SetUp]
    public void SetUp()
    {
        _relations = Substitute.For<IContentRelations>();
        _nodes = new Dictionary<Guid, RelationNode>();
        _relations.GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => _nodes);
        _binder = new RelationBinder(_relations);
    }

    private Guid Node(string type)
    {
        var id = Guid.NewGuid();
        _nodes[id] = new RelationNode(id, type, "t");
        return id;
    }

    [Test]
    public void Constructor_And_NullModel_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new RelationBinder(null!), Throws.ArgumentNullException);
            Assert.That(async () => await _binder.ValidateAsync(null!, "x"), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ValidateAsync_ModelWithoutRelations_IsValidWithoutAQuery()
    {
        Assert.That(await _binder.ValidateAsync(new FixtureFaqTopicUpsertViewModel(), "Topic"), Is.Null);
        await _relations.DidNotReceive().GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ValidateAsync_ValidTargets_ArePlainOrPluralTypeKeys()
    {
        var model = new FixtureFaqUpsertViewModel { FaqTopicId = Node("faqtopics"), RelatedFaqIds = [Node("FAQS")] };

        Assert.That(await _binder.ValidateAsync(model, "FAQ"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_MissingOrUnknownContainment_AsksToChooseTheParent()
    {
        var missing = await _binder.ValidateAsync(new FixtureFaqUpsertViewModel(), "FAQ");
        var unknown = await _binder.ValidateAsync(new FixtureFaqUpsertViewModel { FaqTopicId = Guid.NewGuid() }, "FAQ");
        var wrongType = await _binder.ValidateAsync(new FixtureFaqUpsertViewModel { FaqTopicId = Node("pages") }, "FAQ");

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.EqualTo(new AdminSaveResult(false, "Choose the topic this faq belongs to.", "FaqTopicId")));
            Assert.That(unknown!.ErrorField, Is.EqualTo("FaqTopicId"));
            Assert.That(wrongType!.ErrorField, Is.EqualTo("FaqTopicId"));
        });
    }

    [Test]
    public async Task ValidateAsync_References_ReportRequiredOrGone_WithTheLabel()
    {
        var gone = await _binder.ValidateAsync(new FixtureFaqUpsertViewModel { FaqTopicId = Node("faqtopics"), RelatedFaqIds = [Guid.NewGuid()] }, "FAQ");
        var required = await _binder.ValidateAsync(new Unlabelled(), "FAQ");

        Assert.Multiple(() =>
        {
            Assert.That(gone, Is.EqualTo(new AdminSaveResult(false, "Related Faq Ids: the selected item no longer exists.", "RelatedFaqIds")));
            Assert.That(required, Is.EqualTo(new AdminSaveResult(false, "Related Faq is required.", "RelatedFaq")));
        });
    }
}

[TestFixture]
public class ContentRelationRegistryTests
{
    private static IAdminCrudHandler Handler(string contentType, object upsertModel)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.CreateEmptyUpsertViewModel().Returns(upsertModel);
        return handler;
    }

    private static ContentRelationRegistry Registry(params IAdminCrudHandler[] handlers)
        => new(new HandlerRegistryStub(handlers));

    private sealed class ManyContainment
    {
        [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment)]
        public List<Guid> Parents { get; set; } = [];
    }

    private sealed class OrderedReference
    {
        [ContentRelation("faqtopics", Ordered = true)]
        public Guid Topic { get; set; }
    }

    private sealed class ContainmentWithDeletePolicy
    {
        [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment, OnTargetDelete = RelationDeletePolicy.Detach)]
        public Guid Topic { get; set; }
    }

    private sealed class UnknownTarget
    {
        [ContentRelation("nothing")]
        public Guid Thing { get; set; }
    }

    private class Base
    {
        [ContentRelation("faqtopics")]
        public Guid Topic { get; set; }
    }

    private sealed class Duplicate : Base
    {
        [ContentRelation("faqtopics")]
        public new Guid? Topic { get; set; }
    }

    /// <summary>Targets the singular key, which resolves to the plural handler.</summary>
    private sealed class Detaching
    {
        [ContentRelation("faqtopic", OnTargetDelete = RelationDeletePolicy.Detach)]
        public Guid Topic { get; set; }
    }

    [Test]
    public void Constructor_Null_Throws()
        => Assert.That(() => new ContentRelationRegistry(null!), Throws.ArgumentNullException);

    [Test]
    public void Get_Find_GetPolicy_ReadTheDeclarations()
    {
        var registry = Registry(
            Handler("faqs", new FixtureFaqUpsertViewModel()),
            Handler("faqtopics", new FixtureFaqTopicUpsertViewModel()),
            Handler("notes", new Detaching()));

        registry.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Get("FAQS").Select(d => d.Name), Is.EqualTo(new[] { "FaqTopicId", "RelatedFaqIds" }));
            Assert.That(registry.Get("faqs")[0].SourceContentType, Is.EqualTo("faqs"));
            Assert.That(registry.Get("faqs")[0].ModelType, Is.EqualTo(typeof(FixtureFaqUpsertViewModel)));
            Assert.That(registry.Get("unknown"), Is.Empty);
            Assert.That(registry.Find("faqs", "RelatedFaqIds")!.Attribute.TargetContentType, Is.EqualTo("faqs"));
            Assert.That(registry.Find("faqs", "Nope"), Is.Null);
            Assert.That(registry.GetPolicy("faqs", "RelatedFaqIds"), Is.EqualTo(RelationDeletePolicy.Restrict));
            Assert.That(registry.GetPolicy("notes", "Topic"), Is.EqualTo(RelationDeletePolicy.Detach));
            Assert.That(registry.GetPolicy("unknown", "Topic"), Is.EqualTo(RelationDeletePolicy.Restrict));
        });
    }

    [TestCase(typeof(ManyContainment), "must be a single Guid")]
    [TestCase(typeof(OrderedReference), "sets Ordered")]
    [TestCase(typeof(ContainmentWithDeletePolicy), "sets OnTargetDelete")]
    [TestCase(typeof(UnknownTarget), "not a registered content type")]
    [TestCase(typeof(Duplicate), "shares its name")]
    public void Validate_InvalidDeclaration_Throws(Type modelType, string message)
    {
        var registry = Registry(Handler("bad", Activator.CreateInstance(modelType)!), Handler("faqtopics", new FixtureFaqTopicUpsertViewModel()));

        Assert.That(() => registry.Validate(), Throws.InvalidOperationException.With.Message.Contains(message).And.Message.Contains("'bad'"));
    }
}

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

[TestFixture]
public class HostRelationFixtureTests
{
    /// <summary>
    /// A host type needs only the attribute: the registry validates it, the binder enforces it on save, the
    /// mapper stores it on the version, and deleting the topic deletes its FAQs.
    /// </summary>
    [Test]
    public async Task FaqToFaqTopic_NeedsOnlyTheAttribute()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile(new FixtureFaqProfile())).CreateMapper();
        var store = Substitute.For<IContentStore<FixtureFaqDTO>>();
        FixtureFaqDTO? saved = null;
        store.SaveDraftAsync(Arg.Do<FixtureFaqDTO>(d => saved = d), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));
        store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        var topic = Guid.NewGuid();
        var faqNode = Guid.NewGuid();
        var relations = Substitute.For<IContentRelations>();
        relations.GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, RelationNode>
        {
            [topic] = new(topic, "faqtopics", "Billing"),
            [faqNode] = new(faqNode, "faqs", "How do I pay?")
        });
        relations.GetLiveChildrenAsync(topic, Arg.Any<CancellationToken>())
            .Returns([new RelationReferrer(faqNode, "faqs", "How do I pay?", "FaqTopicId", RelationKind.Containment, true, true)]);
        relations.GetLiveReferrersAsync(Arg.Any<Guid>(), Arg.Any<RelationKind?>(), Arg.Any<CancellationToken>()).Returns([]);
        relations.GetLiveOwnedTargetsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        var transaction = Substitute.For<ICmsTransaction>();
        transaction.RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));

        // The delete service resolves handlers when a delete runs, so the registry can be filled after.
        var registry = new HandlerRegistryStub([TopicHandler()]);
        var sp = new ServiceCollection().AddSingleton<IAdminHandlerRegistry>(registry).BuildServiceProvider();
        var deletes = new ContentDeleteService(relations, transaction, Substitute.For<IChangeSetScope>(), sp);
        var faqs = new FixtureFaqModel(store, mapper, Substitute.For<IChangeSetScope>(), new RelationBinder(relations), deletes);
        registry.Add(faqs);
        var topicDeleted = false;

        new ContentRelationRegistry(registry).Validate();
        var rejected = await faqs.SaveUpsertAsync(new FixtureFaqUpsertViewModel { Title = "How do I pay?" });
        var accepted = await faqs.SaveUpsertAsync(new FixtureFaqUpsertViewModel { Title = "How do I pay?", FaqTopicId = topic });
        var deleted = await deletes.DeleteAsync(topic, (_, _) => Task.FromResult(topicDeleted = true));

        Assert.Multiple(async () =>
        {
            Assert.That(rejected.ErrorField, Is.EqualTo("FaqTopicId"));
            Assert.That(accepted.Success, Is.True);
            Assert.That(saved!.Version.TargetOf("FaqTopicId"), Is.EqualTo(topic));
            Assert.That(deleted, Is.EqualTo((true, (string?)null, 1)));
            Assert.That(topicDeleted, Is.True);
            await store.Received(1).DeleteAsync(faqNode, false, Arg.Any<CancellationToken>());
            Assert.That(((IContentDeleter)faqs).ContentTypeKey, Is.EqualTo("faqs"));
        });
    }

    private static IAdminCrudHandler TopicHandler()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("faqtopics");
        handler.CreateEmptyUpsertViewModel().Returns(new FixtureFaqTopicUpsertViewModel());
        return handler;
    }
}

[TestFixture]
public class RelationDeleteSurfaceTests
{
    [Test]
    public async Task AdminCrudHandler_TryDeleteAsync_DefaultsToDeleteAsync()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        Assert.That(await handler.TryDeleteAsync(Guid.NewGuid()), Is.EqualTo((true, (string?)null, 0)));
    }

    [Test]
    public async Task AdminCrudModel_RelationBinderRejection_StopsTheSave()
    {
        var binder = Substitute.For<IRelationBinder>();
        binder.ValidateAsync(Arg.Any<object>(), "Minimal Crud", Arg.Any<CancellationToken>())
            .Returns(new AdminSaveResult(false, "Choose the list.", "ListId"));
        var model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(),
            binder, TestSupport.PassThroughDeletes());

        var result = await model.SaveUpsertAsync(new Models.ContentBlock.ContentBlockUpsertViewModel { Title = "T", Content = "c" });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new AdminSaveResult(false, "Choose the list.", "ListId")));
            Assert.That(model.SaveUpsertCoreCalled, Is.False);
        });
    }

    [Test]
    public async Task AdminCrudModel_IsTheDeleterForItsContentType()
    {
        IContentDeleter model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(),
            TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        Assert.Multiple(async () =>
        {
            Assert.That(model.ContentTypeKey, Is.EqualTo("minimal-crud"));
            Assert.That(await model.DeleteSelfAsync(Guid.NewGuid()), Is.True);
        });
    }

    [Test]
    public async Task AdminContentController_Delete_ReportsTheRefusalOrTheCascade()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("ct");
        handler.WriteRoles.Returns((string[]?)null);
        handler.TryDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)"'Logo' is used by 1 item(s): 'Home'.", 0), (false, (string?)null, 0), (true, (string?)null, 2), (true, (string?)null, 0));
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler("ct").Returns(handler);
        var controller = new Controllers.Admin.AdminContentController(registry, Substitute.For<ContentZones.IWidgetRegistry>());
        new MvcHarness().Configure(controller, ["Admin"]);

        await controller.Delete("ct", Guid.NewGuid(), default);
        var refused = controller.TempData["Error"];
        controller.TempData.Clear();
        await controller.Delete("ct", Guid.NewGuid(), default);
        var failed = controller.TempData["Error"];
        controller.TempData.Clear();
        await controller.Delete("ct", Guid.NewGuid(), default);
        var cascaded = controller.TempData["Success"];
        controller.TempData.Clear();
        var plain = await controller.Delete("ct", Guid.NewGuid(), default);

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.EqualTo("'Logo' is used by 1 item(s): 'Home'."));
            Assert.That(failed, Is.EqualTo("The item could not be deleted."));
            Assert.That(cascaded, Is.EqualTo("Deleted, together with the 2 item(s) it contained."));
            Assert.That(controller.TempData.Count, Is.Zero);
            Assert.That(plain, Is.InstanceOf<Microsoft.AspNetCore.Mvc.RedirectToActionResult>());
        });
    }
}
