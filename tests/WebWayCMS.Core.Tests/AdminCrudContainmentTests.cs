using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// <see cref="Models.Shared.AdminCrudModel{TDto}"/>'s generic containment surface (children, reorder, move,
/// restore against an expected version), on the host-style FAQ → topic fixture (an ordered containment).
/// </summary>
[TestFixture]
public class AdminCrudContainmentTests
{
    private const string Topic = nameof(FixtureFaqUpsertViewModel.FaqTopicId);

    private IContentStore<FixtureFaqDTO> _store = null!;
    private IChangeSetScope _scope = null!;
    private FixtureFaqModel _model = null!;
    private readonly Guid _topic = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<FixtureFaqDTO>>();
        _scope = Substitute.For<IChangeSetScope>();
        var mapper = new MapperConfiguration(c => c.AddProfile(new FixtureFaqProfile())).CreateMapper();
        _model = new FixtureFaqModel(_store, mapper, _scope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());
        _store.SaveDraftAsync(Arg.Any<FixtureFaqDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));
    }

    private FixtureFaqDTO Faq(string title, int ordinal, int version = 1, Guid? topic = null)
    {
        var faq = new FixtureFaqDTO
        {
            VersionId = Guid.NewGuid(),
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = title,
                VersionNumber = version,
                State = ContentVersionState.Draft,
                IsCurrentDraft = true
            }
        };
        faq.Version.NodeId = faq.Version.Node.Id;
        faq.Version.Relations.Add(new ContentRelation
        {
            Name = Topic, TargetNodeId = topic ?? _topic, Ordinal = ordinal, Kind = RelationKind.Containment, Qualifier = "q"
        });
        return faq;
    }

    private void Children(params FixtureFaqDTO[] faqs)
        => _store.FindCurrentDraftsRelatedAsync(Topic, _topic, Arg.Any<CancellationToken>()).Returns([.. faqs]);

    [Test]
    public async Task GetRelatedChildren_ListsTheCurrentDraftsWithTheirPositions_OnlyForAContainment()
    {
        var first = Faq("First", 0);
        var second = Faq("Second", 1);
        Children(first, second);

        var children = await _model.GetRelatedChildrenAsync(Topic, _topic);
        var notAContainment = await _model.GetRelatedChildrenAsync(nameof(FixtureFaqUpsertViewModel.RelatedFaqIds), _topic);

        Assert.Multiple(() =>
        {
            Assert.That(children, Is.EqualTo(new[]
            {
                new RelatedChild(first.Version.Node.Id, "First", ContentVersionState.Draft, 0),
                new RelatedChild(second.Version.Node.Id, "Second", ContentVersionState.Draft, 1)
            }));
            Assert.That(notAContainment, Is.Empty);
        });
    }

    [Test]
    public async Task ReorderRelated_SavesOnlyTheChildrenThatMoved_WithTheirNewOrdinal()
    {
        var a = Faq("A", 0, version: 3);
        var b = Faq("B", 1, version: 5);
        var c = Faq("C", 2, version: 7);
        Children(a, b, c);
        var saved = new List<(Guid Node, int Ordinal, string? Qualifier, int? Expected)>();
        _store.SaveDraftAsync(Arg.Do<FixtureFaqDTO>(d => { }), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var dto = ci.Arg<FixtureFaqDTO>();
                saved.Add((dto.Version.Node.Id, dto.Version.OrdinalOf(Topic), dto.Version.QualifierOf(Topic), ci.ArgAt<int?>(1)));
                return new ContentWriteResult(true);
            });

        var result = await _model.ReorderRelatedAsync(Topic, _topic, [b.Version.Node.Id, a.Version.Node.Id, c.Version.Node.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(saved, Is.EqualTo(new[]
            {
                (b.Version.Node.Id, 0, (string?)"q", (int?)5),
                (a.Version.Node.Id, 1, (string?)"q", (int?)3)
            }));
        });
        _scope.Received(1).UseChangeNote("Position changed");
    }

    [Test]
    public async Task ReorderRelated_RefusesAnIncompleteOrRepeatedOrder_AndAnUnorderedRelation()
    {
        var a = Faq("A", 0);
        var b = Faq("B", 1);
        Children(a, b);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.ReorderRelatedAsync(Topic, _topic, [a.Version.Node.Id]),
                Is.EqualTo((false, (string?)Models.Shared.AdminCrudModel<FixtureFaqDTO>.ReorderSetMessage)));
            Assert.That(await _model.ReorderRelatedAsync(Topic, _topic, [a.Version.Node.Id, a.Version.Node.Id]),
                Is.EqualTo((false, (string?)Models.Shared.AdminCrudModel<FixtureFaqDTO>.ReorderSetMessage)));
            Assert.That(await _model.ReorderRelatedAsync(Topic, _topic, [a.Version.Node.Id, Guid.NewGuid()]),
                Is.EqualTo((false, (string?)Models.Shared.AdminCrudModel<FixtureFaqDTO>.ReorderSetMessage)));
            Assert.That(await _model.ReorderRelatedAsync(nameof(FixtureFaqUpsertViewModel.RelatedFaqIds), _topic, []),
                Is.EqualTo((false, (string?)IAdminCrudHandler.ReorderNotSupportedMessage)));
            Assert.That(() => _model.ReorderRelatedAsync(Topic, _topic, null!), Throws.ArgumentNullException);
        });
        await _store.DidNotReceiveWithAnyArgs().SaveDraftAsync(default!, default, default);
    }

    [Test]
    public async Task ReorderRelated_AFailedSave_StopsWithItsReason()
    {
        var a = Faq("A", 0);
        var b = Faq("B", 1);
        Children(a, b);
        _store.SaveDraftAsync(Arg.Any<FixtureFaqDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "stale"));

        var result = await _model.ReorderRelatedAsync(Topic, _topic, [b.Version.Node.Id, a.Version.Node.Id]);

        Assert.That(result, Is.EqualTo((false, (string?)"stale")));
        await _store.Received(1).SaveDraftAsync(Arg.Any<FixtureFaqDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MoveRelated_SavesTheChildUnderTheNewParent_ThenPlacesIt()
    {
        var newTopic = Guid.NewGuid();
        var moving = Faq("Moving", 0, version: 4);
        _store.GetCurrentDraftAsync(moving.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(moving);
        var sibling = Faq("Sibling", 0, topic: newTopic);
        var movedDraft = Faq("Moving", 1, topic: newTopic);
        movedDraft.Version.Node = moving.Version.Node;
        _store.FindCurrentDraftsRelatedAsync(Topic, newTopic, Arg.Any<CancellationToken>()).Returns([sibling, movedDraft]);
        var saved = new List<FixtureFaqDTO>();
        _store.SaveDraftAsync(Arg.Do<FixtureFaqDTO>(saved.Add), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: moving.Version.Node.Id));

        var result = await _model.MoveRelatedAsync(Topic, moving.Version.Node.Id, newTopic, 0, 4);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((true, (string?)null)));
            Assert.That(saved[0].Version.TargetOf(Topic), Is.EqualTo(newTopic), "the move is an ordinary save of the child");
            Assert.That(saved.Skip(1).Select(d => (d.Version.Node.Id, d.Version.OrdinalOf(Topic))),
                Is.EqualTo(new[] { (moving.Version.Node.Id, 0), (sibling.Version.Node.Id, 1) }), "then the new siblings are reordered");
        });
        _scope.Received(1).UseChangeNote("Moved");
    }

    [Test]
    public async Task MoveRelated_WithoutAPosition_OnlySaves()
    {
        var moving = Faq("Moving", 0);
        _store.GetCurrentDraftAsync(moving.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(moving);

        var result = await _model.MoveRelatedAsync(Topic, moving.Version.Node.Id, Guid.NewGuid(), null, null);

        Assert.That(result, Is.EqualTo((true, (string?)null)));
        await _store.Received(1).SaveDraftAsync(Arg.Any<FixtureFaqDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().FindCurrentDraftsRelatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MoveRelated_Refusals()
    {
        var moving = Faq("Moving", 0, version: 4);
        _store.GetCurrentDraftAsync(moving.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(moving);

        var notAContainment = await _model.MoveRelatedAsync(nameof(FixtureFaqUpsertViewModel.RelatedFaqIds), moving.Version.Node.Id, _topic, null, null);
        var missing = await _model.MoveRelatedAsync(Topic, Guid.NewGuid(), _topic, null, null);
        var stale = await _model.MoveRelatedAsync(Topic, moving.Version.Node.Id, _topic, null, 3);
        _store.SaveDraftAsync(Arg.Any<FixtureFaqDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "nope"));
        var failed = await _model.MoveRelatedAsync(Topic, moving.Version.Node.Id, _topic, 0, 4);

        Assert.Multiple(() =>
        {
            Assert.That(notAContainment, Is.EqualTo((false, IAdminCrudHandler.MoveNotSupportedMessage)));
            Assert.That(missing.ErrorMessage, Does.StartWith("No faq found"));
            Assert.That(stale, Is.EqualTo((false, (string?)ContentStore<FixtureFaqDTO>.StaleVersionMessage)));
            Assert.That(failed, Is.EqualTo((false, (string?)"nope")));
        });
    }

    [Test]
    public async Task RestoreVersion_WithAnExpectedVersion_IsRefusedWhenStaleOrMissing()
    {
        var historical = Faq("Old", 0, version: 1);
        var current = Faq("New", 0, version: 3);
        current.Version.Node = historical.Version.Node;
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        _store.GetCurrentDraftAsync(historical.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(current);
        _store.RestoreAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        var stale = await _model.RestoreVersionAsync(historical.VersionId, 2);
        var missing = await _model.RestoreVersionAsync(Guid.NewGuid(), 2);
        var current3 = await _model.RestoreVersionAsync(historical.VersionId, 3);
        var unchecked_ = await _model.RestoreVersionAsync(historical.VersionId, (int?)null);

        Assert.Multiple(() =>
        {
            Assert.That(stale.ErrorMessage, Is.EqualTo(ContentStore<FixtureFaqDTO>.StaleVersionMessage));
            Assert.That(missing.ErrorMessage, Is.EqualTo(IAdminCrudHandler.VersionNotFoundMessage));
            Assert.That(current3.Success, Is.True);
            Assert.That(unchecked_.Success, Is.True);
        });
        await _store.Received(2).RestoreAsync(historical.VersionId, Arg.Any<CancellationToken>());
    }

    [Test]
    public void RelationPreselect_FillsRelationPropertiesNamedByTheQuery()
    {
        var topic = Guid.NewGuid();
        var faq1 = Guid.NewGuid();
        var faq2 = Guid.NewGuid();
        var vm = new FixtureFaqUpsertViewModel { RelatedFaqIds = [Guid.NewGuid()] };
        var query = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["faqtopicid"] = topic.ToString(),
            ["RelatedFaqIds"] = new[] { $"{faq1}, {faq2}", faq1.ToString() },
            ["Answer"] = "ignored",
        });
        var untouched = new FixtureFaqUpsertViewModel { FaqTopicId = topic };

        RelationPreselect.Apply(vm, query);
        RelationPreselect.Apply(untouched, new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["FaqTopicId"] = "not-a-guid" }));
        RelationPreselect.Apply(new FixtureFaqTopicUpsertViewModel(), query);

        Assert.Multiple(() =>
        {
            Assert.That(vm.FaqTopicId, Is.EqualTo(topic));
            Assert.That(vm.RelatedFaqIds, Is.EqualTo(new[] { faq1, faq2 }));
            Assert.That(vm.Answer, Is.Empty);
            Assert.That(untouched.FaqTopicId, Is.EqualTo(topic));
            Assert.That(() => RelationPreselect.Apply(null!, query), Throws.ArgumentNullException);
            Assert.That(() => RelationPreselect.Apply(vm, null!), Throws.ArgumentNullException);
        });
    }
}
