using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using NUnit.Framework;

using WebWayCMS.Attributes;
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

[TestFixture]
public class WidgetDiscoveryModeTests
{
    private sealed class ProbeViewComponent : WidgetViewComponent
    {
        public WidgetRenderMode Mode(WidgetConfiguration? config) => RenderModeFor(config);
    }

    private sealed class ProbeConfig : WidgetConfiguration
    {
    }

    [Test]
    public void RenderModeFor_UnderDiscovery_UsesThePublicVariant_EvenInTheZoneEditorOnAnAdminPath()
    {
        var component = new ProbeViewComponent();
        WidgetComponentHarness.Attach(component);
        component.HttpContext.Request.Path = "/wadmin/contentzones/page/x";
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;
        component.ViewComponentContext.ViewData[SlotDiscoveryRecorder.ViewDataKey] = new SlotDiscoveryRecorder(Guid.NewGuid());
        var list = new ProbeConfig { Metadata = new WidgetMetadata { DisplayMode = WidgetDisplayMode.List } };

        Assert.Multiple(() =>
        {
            Assert.That(component.Mode(new ProbeConfig()), Is.EqualTo(WidgetRenderMode.View));
            Assert.That(component.Mode(list), Is.EqualTo(WidgetRenderMode.List));
        });
    }
}

[TestFixture]
public class ViewComponentSlotDiscoveryTests
{
    private IViewComponentHelper _helper = null!;
    private ServiceProvider _services = null!;
    private IHttpContextAccessor _accessor = null!;
    private ViewContext? _viewContext;
    private readonly Guid _item = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _helper = Substitute.For<IViewComponentHelper, IViewContextAware>();
        ((IViewContextAware)_helper).When(h => h.Contextualize(Arg.Any<ViewContext>())).Do(ci => _viewContext = ci.Arg<ViewContext>());
        var tempData = Substitute.For<ITempDataDictionaryFactory>();
        tempData.GetTempData(Arg.Any<HttpContext>()).Returns(Substitute.For<ITempDataDictionary>());
        _services = new ServiceCollection()
            .AddSingleton(_helper)
            .AddSingleton(tempData)
            .BuildServiceProvider();
        _accessor = Substitute.For<IHttpContextAccessor>();
        // No request by default (a substitute would otherwise hand out an auto-mocked HttpContext).
        _accessor.HttpContext.Returns((HttpContext?)null);
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    /// <summary>The component "renders" by recording slots through the recorder its ViewData carries.</summary>
    private void RendersSlots(params string[] slots)
        => _helper.InvokeAsync("Layout", Arg.Any<object>()).Returns(_ =>
        {
            var recorder = (SlotDiscoveryRecorder)_viewContext!.ViewData[SlotDiscoveryRecorder.ViewDataKey]!;
            foreach (var slot in slots)
                recorder.Record(slot);
            return Task.FromResult<IHtmlContent>(new HtmlString("<div>ignored</div>"));
        });

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ViewComponentSlotDiscovery(null!, _services), Throws.ArgumentNullException);
            Assert.That(() => new ViewComponentSlotDiscovery(_accessor, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task DiscoverSlotsAsync_InTheRequest_ReturnsTheRecordedSlots_InOrderWithoutDuplicates()
    {
        var request = new DefaultHttpContext { RequestServices = _services };
        _accessor.HttpContext.Returns(request);
        RendersSlots("Left", "Right", "Left");
        var config = new object();

        var slots = await new ViewComponentSlotDiscovery(_accessor, _services).DiscoverSlotsAsync(_item, "Layout", config);

        Assert.Multiple(async () =>
        {
            Assert.That(slots, Is.EqualTo(new[] { "Left", "Right" }));
            Assert.That(_viewContext!.HttpContext, Is.SameAs(request));
            Assert.That(_viewContext.ViewData["ContentZone:ParentItemId"], Is.EqualTo(_item));
            Assert.That(_viewContext.Writer, Is.SameAs(TextWriter.Null));
            // The context's view is a placeholder that never renders anything.
            Assert.That(_viewContext.View.Path, Is.Empty);
            await _viewContext.View.RenderAsync(_viewContext);
            await _helper.Received(1).InvokeAsync("Layout", config);
        });
    }

    [Test]
    public async Task DiscoverSlotsAsync_OutsideARequest_UsesAStandInContext()
    {
        RendersSlots("Main");

        var slots = await new ViewComponentSlotDiscovery(_accessor, _services).DiscoverSlotsAsync(_item, "Layout", new object());

        Assert.Multiple(() =>
        {
            Assert.That(slots, Is.EqualTo(new[] { "Main" }));
            Assert.That(_viewContext!.HttpContext.RequestServices, Is.SameAs(_services));
        });
    }

    [Test]
    public async Task DiscoverSlotsAsync_RenderFails_ReturnsNull()
    {
        _helper.InvokeAsync("Layout", Arg.Any<object>()).ThrowsAsync(new InvalidOperationException("no view"));

        var slots = await new ViewComponentSlotDiscovery(_accessor, _services).DiscoverSlotsAsync(_item, "Layout", new object());

        Assert.Multiple(async () =>
        {
            Assert.That(slots, Is.Null);
            await _helper.Received(1).InvokeAsync("Layout", Arg.Any<object>());
        });
    }

    [Test]
    public void DiscoverSlotsAsync_Cancelled_Throws()
    {
        _helper.InvokeAsync("Layout", Arg.Any<object>()).ThrowsAsync(new OperationCanceledException());
        var discovery = new ViewComponentSlotDiscovery(_accessor, _services);

        Assert.Multiple(() =>
        {
            Assert.That(() => discovery.DiscoverSlotsAsync(_item, "Layout", new object()), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => discovery.DiscoverSlotsAsync(_item, "Layout", new object(), new CancellationToken(canceled: true)),
                Throws.InstanceOf<OperationCanceledException>());
        });
    }
}
