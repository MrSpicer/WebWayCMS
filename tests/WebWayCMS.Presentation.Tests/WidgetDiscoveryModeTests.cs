using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.ViewComponents;
using WebWayCMS.ViewComponents.SlotDiscovery;

namespace WebWayCMS.Presentation.Tests;

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
