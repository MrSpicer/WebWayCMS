using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class PageSetEqualityTests
{
    private static PageDTO Page(string title = "T", string? viewName = null, DateTime? start = null, DateTime? end = null, string field = "")
        => new()
        {
            VersionId = Guid.NewGuid(),
            ControllerName = "GenericPage",
            ViewName = viewName,
            ConfigurationJson = "{}",
            Version = new ContentVersion
            {
                NodeId = Guid.NewGuid(),
                Title = title,
                Slug = "t",
                PublishStartUtc = start,
                PublishEndUtc = end,
                CustomFields = field.Length == 0 ? [] : [new CustomField { FieldName = field, Value = "v" }]
            }
        };

    private static ContentZoneItemDTO Item(string? metadata = "{}", bool active = true, int ordinal = 1, Guid? zone = null, string props = "{}")
        => new()
        {
            VersionId = Guid.NewGuid(),
            ComponentName = "Text",
            ComponentPropertiesJson = props,
            MetadataJson = metadata,
            IsActive = active,
            Ordinal = ordinal,
            ContentZoneNodeId = zone ?? Guid.Empty,
            Version = new ContentVersion { NodeId = Guid.NewGuid() }
        };

    [Test]
    public void PagesEqual_IgnoresVersionIdentity_ButSeesEveryEditableField()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(PageSetEquality.PagesEqual(Page(), Page()), Is.True);
            Assert.That(PageSetEquality.PagesEqual(Page("A"), Page("B")), Is.False);
            Assert.That(PageSetEquality.PagesEqual(Page(viewName: "x"), Page()), Is.False);
            Assert.That(PageSetEquality.PagesEqual(Page(start: start, end: start), Page(start: start, end: start)), Is.True);
            Assert.That(PageSetEquality.PagesEqual(Page(start: start), Page()), Is.False);
            Assert.That(PageSetEquality.PagesEqual(Page(field: "f"), Page()), Is.False);
        });
    }

    [Test]
    public void ItemsEqual_ComparesContentPlacementAndActiveFlag()
    {
        var zone = Guid.NewGuid();
        Assert.Multiple(() =>
        {
            Assert.That(PageSetEquality.ItemsEqual(Item(zone: zone), Item(zone: zone)), Is.True);
            Assert.That(PageSetEquality.ItemsEqual(Item(metadata: null), Item(metadata: "{}")), Is.True, "null metadata is the empty document");
            Assert.That(PageSetEquality.ItemsEqual(Item(props: "{\"a\":1}"), Item()), Is.False);
            Assert.That(PageSetEquality.ItemsEqual(Item(active: false), Item()), Is.False);
            Assert.That(PageSetEquality.ItemsEqual(Item(zone: zone), Item()), Is.False);
            Assert.That(PageSetEquality.ItemsEqual(Item(ordinal: 2), Item()), Is.False);
            Assert.That(PageSetEquality.ItemContentEqual(Item(ordinal: 2, active: false), Item()), Is.True);
        });
    }

    [Test]
    public void StateToken_IsOrderIndependent_AndSeesEveryChange()
    {
        var page = Page();
        var a = Item();
        var b = Item(active: false);

        var token = PageSetEquality.StateToken(page, [a, b]);

        Assert.Multiple(() =>
        {
            Assert.That(PageSetEquality.StateToken(page, [b, a]), Is.EqualTo(token));
            Assert.That(PageSetEquality.StateToken(page, [a, b with { Ordinal = 9 }]), Is.Not.EqualTo(token));
            Assert.That(PageSetEquality.StateToken(page, [a]), Is.Not.EqualTo(token));
        });
    }
}
