using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentBlockViewComponentTests
{
    private IContentBlockModel _model = null!;
    private ContentBlockViewComponent _component = null!;
    private WidgetComponentHarness.Context _widget = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentBlockModel>();
        _component = new ContentBlockViewComponent(_model);
        _widget = WidgetComponentHarness.Attach(_component);
    }

    [Test]
    public async Task NullConfigOrEmptyId_RendersEmptyContent()
    {
        Assert.That(await _component.InvokeAsync(null!), Is.InstanceOf<ContentViewComponentResult>());
        Assert.That(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = Guid.Empty }),
            Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task ValidId_RendersFoundOrFallbackViewModel()
    {
        _widget.Hit("ContentBlock", WidgetRenderMode.View, "default", "view.Default");

        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>()).Returns(new ContentBlockViewModel(), (ContentBlockViewModel?)null);

        var result = await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id });
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<ContentBlockViewModel>());
        });
        // Second call returns null -> falls back to a new view model carrying the node id.
        var fallback = (ContentBlockViewModel)ViewComponentHarness.Model(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id }))!;
        Assert.That(fallback.NodeId, Is.EqualTo(id));
    }
}
