using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ZoneItemDeleterTests
{
    [Test]
    public async Task DeleteSelfAsync_DeletesTheItemThroughTheZoneService()
    {
        var zones = Substitute.For<IContentZoneService>();
        var item = Guid.NewGuid();
        zones.DeleteItemAsync(item, Arg.Any<CancellationToken>()).Returns(true);
        var deleter = new ZoneItemDeleter(zones);

        Assert.Multiple(async () =>
        {
            Assert.That(deleter.ContentTypeKey, Is.EqualTo("contentzoneitems"));
            Assert.That(await deleter.DeleteSelfAsync(item), Is.True);
            Assert.That(() => new ZoneItemDeleter(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task PageDelete_CascadesThroughZonesAndItems_DeepestFirst_InOneTransaction()
    {
        // page ─Owner─ zone ─Zone─ container ─Owner─ column ─Zone─ text
        Guid page = Guid.NewGuid(), zone = Guid.NewGuid(), container = Guid.NewGuid(), column = Guid.NewGuid(), text = Guid.NewGuid();
        var nodes = new Dictionary<Guid, RelationNode>
        {
            [page] = new(page, "pages", "Home"),
            [zone] = new(zone, "contentzones", "Main"),
            [container] = new(container, "contentzoneitems", "Layout"),
            [column] = new(column, "contentzones", "Left"),
            [text] = new(text, "contentzoneitems", "Text")
        };
        var children = new Dictionary<Guid, Guid>
        {
            [page] = zone, [zone] = container, [container] = column, [column] = text
        };
        var relations = Substitute.For<IContentRelations>();
        relations.GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(nodes);
        relations.GetLiveChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c =>
            (IReadOnlyList<RelationReferrer>)(children.TryGetValue(c.Arg<Guid>(), out var child)
                ? [new RelationReferrer(child, nodes[child].ContentTypeKey, nodes[child].Title,
                    nodes[child].ContentTypeKey == "contentzones" ? ZoneRelations.Owner : ZoneRelations.Zone,
                    RelationKind.Containment, true, true)]
                : []));
        relations.GetLiveReferrersAsync(Arg.Any<Guid>(), Arg.Any<RelationKind?>(), Arg.Any<CancellationToken>()).Returns([]);
        relations.GetLiveOwnedTargetsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        var deleted = new List<Guid>();
        var zoneService = Substitute.For<IContentZoneService>();
        zoneService.DeleteItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c => { deleted.Add(c.Arg<Guid>()); return true; });
        var zoneDeleter = Substitute.For<IContentDeleter>();
        zoneDeleter.ContentTypeKey.Returns("contentzones");
        zoneDeleter.DeleteSelfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c => { deleted.Add(c.Arg<Guid>()); return true; });
        var services = new ServiceCollection()
            .AddSingleton<IContentDeleter>(new ZoneItemDeleter(zoneService))
            .AddSingleton(zoneDeleter)
            .BuildServiceProvider();
        var transaction = TestSupport.PassThroughTransaction();
        var service = new ContentDeleteService(relations, transaction, Substitute.For<IChangeSetScope>(), services);

        var result = await service.DeleteAsync(page, (id, _) => { deleted.Add(id); return Task.FromResult(true); });

        Assert.Multiple(async () =>
        {
            // Zones and items are structure, not counted: a page delete reports its child pages.
            Assert.That(result, Is.EqualTo((true, (string?)null, 0)));
            Assert.That(deleted, Is.EqualTo(new[] { text, column, container, zone, page }));
            await transaction.Received(1).RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
        });
    }
}
