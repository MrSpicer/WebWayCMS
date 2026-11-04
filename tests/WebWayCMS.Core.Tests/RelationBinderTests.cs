using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

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

    /// <summary>A type that nests under its own kind (like a page under a page).</summary>
    public sealed class Nested : BaseContentViewModel
    {
        [ContentRelation("nodes", Kind = ContentRelationKind.Containment)]
        public Guid? ParentId { get; set; }
    }

    [Test]
    public async Task ValidateAsync_ContainmentUnderItselfOrADescendant_IsRefused()
    {
        var self = Node("nodes");
        var child = Node("nodes");
        var grandchild = Node("nodes");
        _relations.GetCurrentDraftTargetAsync(grandchild, "ParentId", Arg.Any<CancellationToken>()).Returns(child);
        _relations.GetCurrentDraftTargetAsync(child, "ParentId", Arg.Any<CancellationToken>()).Returns(self);

        var underItself = await _binder.ValidateAsync(new Nested { NodeId = self, ParentId = self }, "Page");
        var underGrandchild = await _binder.ValidateAsync(new Nested { NodeId = self, ParentId = grandchild }, "Page");

        Assert.Multiple(() =>
        {
            Assert.That(underItself, Is.EqualTo(new AdminSaveResult(false,
                "This page can't be placed under itself or one of its descendants.", "ParentId")));
            Assert.That(underGrandchild!.ErrorField, Is.EqualTo("ParentId"));
        });
    }

    [Test]
    public async Task ValidateAsync_ContainmentElsewhere_OrOnCreate_IsValid_EvenOverAnExistingLoop()
    {
        var self = Node("nodes");
        var a = Node("nodes");
        var b = Node("nodes");
        // a and b already point at each other: the walk stops at the first repeat.
        _relations.GetCurrentDraftTargetAsync(a, "ParentId", Arg.Any<CancellationToken>()).Returns(b);
        _relations.GetCurrentDraftTargetAsync(b, "ParentId", Arg.Any<CancellationToken>()).Returns(a);

        Assert.Multiple(async () =>
        {
            Assert.That(await _binder.ValidateAsync(new Nested { NodeId = self, ParentId = a }, "Page"), Is.Null);
            Assert.That(await _binder.ValidateAsync(new Nested { NodeId = Guid.Empty, ParentId = a }, "Page"), Is.Null);
            Assert.That(await _binder.ValidateAsync(new Nested { ParentId = self }, "Page"), Is.Null);
        });
        await _relations.DidNotReceive().GetCurrentDraftTargetAsync(self, Arg.Any<string>(), Arg.Any<CancellationToken>());
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
