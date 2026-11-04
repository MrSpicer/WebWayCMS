using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using NUnit.Framework;

using WebWayCMS.ViewComponents.SlotDiscovery;

namespace WebWayCMS.Presentation.Tests;

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
