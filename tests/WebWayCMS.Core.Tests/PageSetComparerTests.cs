using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageSetComparerTests
{
    private static readonly Guid Zone = Guid.NewGuid();

    private static ContentZoneItemDTO Item(Guid node, Guid? zone = null, int ordinal = 1, string props = "{}", bool active = true)
        => new ContentZoneItemDTO
        {
            VersionId = Guid.NewGuid(),
            ComponentName = "Text",
            ComponentPropertiesJson = props,
            IsActive = active,
            Version = new ContentVersion { NodeId = node }
        }.In(zone ?? Zone, ordinal);

    private static PageDTO Page(string title) => new() { ControllerName = "GenericPage", Version = new ContentVersion { Title = title } };

    [Test]
    public void Compare_ClassifiesEveryItem()
    {
        Guid same = Guid.NewGuid(), edited = Guid.NewGuid(), rezoned = Guid.NewGuid(), reordered = Guid.NewGuid(),
            added = Guid.NewGuid(), removed = Guid.NewGuid(), deactivated = Guid.NewGuid();
        var from = new PageSetState(Page("T"),
            [Item(same), Item(edited), Item(rezoned), Item(reordered), Item(removed), Item(deactivated)]);
        var to = new PageSetState(Page("T"),
            [Item(same), Item(edited, props: "{\"x\":1}"), Item(rezoned, zone: Guid.NewGuid()), Item(reordered, ordinal: 5),
             Item(added), Item(deactivated, active: false)]);

        var diff = PageSetComparer.Compare(from, to);

        var changes = diff.Items.ToDictionary(d => d.Item.Version.NodeId, d => d.Change);
        Assert.Multiple(() =>
        {
            Assert.That(diff.PageChanged, Is.False);
            Assert.That(changes[same], Is.EqualTo(PageSetChange.Unchanged));
            Assert.That(changes[edited], Is.EqualTo(PageSetChange.Changed));
            Assert.That(changes[rezoned], Is.EqualTo(PageSetChange.Moved));
            Assert.That(changes[reordered], Is.EqualTo(PageSetChange.Moved));
            Assert.That(changes[added], Is.EqualTo(PageSetChange.Added));
            Assert.That(changes[removed], Is.EqualTo(PageSetChange.Removed));
            Assert.That(changes[deactivated], Is.EqualTo(PageSetChange.Removed), "an inactive item is not on the page");
        });
    }

    [Test]
    public void Compare_NoBaseline_EverythingAddedAndNoPageChange()
    {
        var diff = PageSetComparer.Compare(null, new PageSetState(Page("T"), [Item(Guid.NewGuid())]));

        Assert.Multiple(() =>
        {
            Assert.That(diff.PageChanged, Is.False);
            Assert.That(diff.Items.Single().Change, Is.EqualTo(PageSetChange.Added));
        });
    }

    [Test]
    public void Compare_PageFieldsDiffer_ReportsAPageChange()
    {
        var diff = PageSetComparer.Compare(new PageSetState(Page("A"), []), new PageSetState(Page("B"), []));

        Assert.That(diff.PageChanged, Is.True);
    }
}
