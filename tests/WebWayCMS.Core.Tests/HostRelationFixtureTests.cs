using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

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
