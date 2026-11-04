using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

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

    private sealed class HiddenReference
    {
        [ContentRelation("faqtopics", ShowInParentAdmin = false)]
        public Guid Topic { get; set; }
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

    [Test]
    public void GetContainmentsTargeting_ListsTheChildTypes_ByKeyOrSingular()
    {
        var registry = Registry(
            Handler("faqs", new FixtureFaqUpsertViewModel()),
            Handler("faqtopics", new FixtureFaqTopicUpsertViewModel()),
            Handler("notes", new Detaching()));

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetContainmentsTargeting("FAQTOPICS").Select(d => (d.SourceContentType, d.Name)),
                Is.EqualTo(new[] { ("faqs", "FaqTopicId") }), "the faqs reference and the notes reference are not containments");
            Assert.That(registry.GetContainmentsTargeting("faqs"), Is.Empty);
        });
    }

    [TestCase(typeof(ManyContainment), "must be a single Guid")]
    [TestCase(typeof(OrderedReference), "sets Ordered")]
    [TestCase(typeof(ContainmentWithDeletePolicy), "sets OnTargetDelete")]
    [TestCase(typeof(HiddenReference), "turns off ShowInParentAdmin")]
    [TestCase(typeof(UnknownTarget), "not a registered content type")]
    [TestCase(typeof(Duplicate), "shares its name")]
    public void Validate_InvalidDeclaration_Throws(Type modelType, string message)
    {
        var registry = Registry(Handler("bad", Activator.CreateInstance(modelType)!), Handler("faqtopics", new FixtureFaqTopicUpsertViewModel()));

        Assert.That(() => registry.Validate(), Throws.InvalidOperationException.With.Message.Contains(message).And.Message.Contains("'bad'"));
    }
}
