using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ImageViewComponentTests
{
    private WebWayCMS.Models.Image.IImageModel _model = null!;
    private ImageViewComponent _component = null!;
    private WidgetComponentHarness.Context _widget = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<WebWayCMS.Models.Image.IImageModel>();
        _component = new ImageViewComponent(_model);
        _widget = WidgetComponentHarness.Attach(_component);
        _widget.Hit("Image", WidgetRenderMode.View, "default", "view.Default");
        _widget.Hit("Image", WidgetRenderMode.View, "Hero", "view.Hero");
    }

    private static WebWayCMS.Models.Image.ImageViewModel Vm() => new()
    {
        BlobHash = "hash",
        AltText = "alt",
        Caption = "cap"
    };

    private Task<IViewComponentResult> Invoke(WebWayCMS.Models.Image.ImageContentZoneConfiguration? config)
        => _component.InvokeAsync(config);

    [Test]
    public void Constructor_NullModel_Throws() =>
        Assert.That(() => new ImageViewComponent(null!), Throws.ArgumentNullException);

    [Test]
    public async Task InvokeAsync_NullConfig_RendersNothing()
    {
        var result = await Invoke(null);
        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task InvokeAsync_NoImageSelected_RendersNothing()
    {
        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration());
        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task InvokeAsync_EmptyGuid_RendersNothing()
    {
        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration { ImageNodeId = Guid.Empty });
        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task InvokeAsync_UnknownImage_RendersNothing()
    {
        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((WebWayCMS.Models.Image.ImageViewModel?)null);

        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration { ImageNodeId = id });
        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task InvokeAsync_KnownImage_UsesDefaultViewAndAppliesConfig()
    {
        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>()).Returns(Vm());

        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration
        {
            ImageNodeId = id,
            CssClass = "rounded",
            ShowCaption = true
        });

        var model = (WebWayCMS.Models.Image.ImageViewModel)ViewComponentHarness.Model(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
            Assert.That(model.CssClass, Is.EqualTo("rounded"));
            Assert.That(model.ShowCaption, Is.True);
        });
    }

    [Test]
    public async Task InvokeAsync_BlankViewName_FallsBackToDefault()
    {
        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>()).Returns(Vm());

        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration
        {
            ImageNodeId = id,
            ViewName = "   "
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
    }

    [Test]
    public async Task InvokeAsync_NamedView_IsHonoured()
    {
        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>()).Returns(Vm());

        var result = await Invoke(new WebWayCMS.Models.Image.ImageContentZoneConfiguration
        {
            ImageNodeId = id,
            ViewName = "Hero"
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Hero"));
    }
}
