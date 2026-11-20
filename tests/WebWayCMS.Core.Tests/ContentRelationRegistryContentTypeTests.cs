using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>With a content type registry, a relation target must also be registered with a content store.</summary>
[TestFixture]
public class ContentRelationRegistryContentTypeTests
{
    private sealed class TopicReference
    {
        [ContentRelation("faqtopic")]
        public Guid Topic { get; set; }
    }

    private sealed class TopicsReference
    {
        [ContentRelation("faqtopics")]
        public Guid Topic { get; set; }
    }

    private sealed class NoUpsertRelations;

    private static IAdminCrudHandler Handler(string contentType, object upsertModel)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.CreateEmptyUpsertViewModel().Returns(upsertModel);
        return handler;
    }

    private static ContentRelationRegistry Registry(IContentTypeRegistry contentTypes, object referrer)
        => new(
            new HandlerRegistryStub([Handler("notes", referrer), Handler("faqtopics", new NoUpsertRelations())]),
            contentTypes: contentTypes);

    [Test]
    public void Validate_TargetRegisteredWithAStore_ByKeyOrSingular_Passes()
    {
        var contentTypes = new ContentTypeRegistry([new ContentTypeDescriptor("faqtopics", typeof(PageDTO))]);

        Assert.Multiple(() =>
        {
            Assert.That(() => Registry(contentTypes, new TopicsReference()).Validate(), Throws.Nothing);
            Assert.That(() => Registry(contentTypes, new TopicReference()).Validate(), Throws.Nothing);
        });
    }

    [Test]
    public void Validate_TargetWithAHandlerButNoStore_Throws()
    {
        var contentTypes = new ContentTypeRegistry([new ContentTypeDescriptor("notes", typeof(PageDTO))]);

        Assert.That(() => Registry(contentTypes, new TopicsReference()).Validate(),
            Throws.InvalidOperationException.With.Message.Contains("not registered with a content store"));
    }
}
