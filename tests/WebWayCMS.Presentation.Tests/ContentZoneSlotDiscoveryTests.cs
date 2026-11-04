using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.ViewComponents;
using WebWayCMS.ViewComponents.SlotDiscovery;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentZoneSlotDiscoveryTests
{
    private IContentZoneModel _model = null!;
    private ContentZoneViewComponent _component = null!;
    private DefaultHttpContext _http = null!;
    private readonly Guid _owner = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentZoneModel>();
        _component = new ContentZoneViewComponent(_model, Substitute.For<IWidgetRegistry>());
        _http = ViewComponentHarness.Attach(_component);
    }

    private SlotDiscoveryRecorder Discover(Guid? parentItemId)
    {
        var recorder = new SlotDiscoveryRecorder(_owner);
        _component.ViewComponentContext.ViewData[SlotDiscoveryRecorder.ViewDataKey] = recorder;
        _component.ViewComponentContext.ViewData["ContentZone:ParentItemId"] = parentItemId;
        return recorder;
    }

    [Test]
    public async Task Discovery_RecordsTheContainersOwnZones_AndRendersNothing()
    {
        var recorder = Discover(_owner);

        var left = await _component.InvokeAsync(zoneName: "Left");
        await _component.InvokeAsync(zoneName: "Right", editMode: true);
        await _component.InvokeAsync(zoneName: "Left");

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.InstanceOf<ContentViewComponentResult>());
            Assert.That(recorder.Slots, Is.EqualTo(new[] { "Left", "Right" }));
            Assert.That(recorder.ItemNodeId, Is.EqualTo(_owner));
            Assert.That(_model.ReceivedCalls(), Is.Empty, "discovery neither loads nor creates zones");
        });
    }

    [Test]
    public async Task Discovery_IgnoresGlobalDirectBlankAndForeignZones()
    {
        var recorder = Discover(_owner);
        await _component.InvokeAsync(zoneName: "Footer", IsGlobal: true);
        await _component.InvokeAsync(zoneName: "Main", zoneId: Guid.NewGuid());
        await _component.InvokeAsync(zoneName: " ");
        _component.ViewComponentContext.ViewData["ContentZone:ParentItemId"] = Guid.NewGuid();
        await _component.InvokeAsync(zoneName: "Nested");
        _component.ViewComponentContext.ViewData.Remove("ContentZone:ParentItemId");
        await _component.InvokeAsync(zoneName: "Orphan");

        Assert.Multiple(() =>
        {
            Assert.That(recorder.Slots, Is.Empty);
            Assert.That(_model.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task EditMode_LogsTheZoneAndItsWidgets_ForTheUnplacedPanel()
    {
        var zoneId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(new ContentZoneViewModel
        {
            Id = zoneId,
            Name = "Main",
            ZoneObjects = [new ContentZoneObject { Id = itemId }]
        });
        var renderedZone = new UnplacedZoneCandidate(zoneId, itemId, "rendered");
        var underRenderedItem = new UnplacedZoneCandidate(Guid.NewGuid(), itemId, "stranded");

        await _component.InvokeAsync(zoneId: zoneId, editMode: true);

        var next = ZoneEditRenderLog.For(_http).NextUnplaced([renderedZone, underRenderedItem], new HashSet<Guid>());
        Assert.That(next, Is.EqualTo(new[] { underRenderedItem }));
    }
}
