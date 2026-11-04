using Microsoft.AspNetCore.Mvc.ViewComponents;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Layout;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class LayoutViewComponentTests
{
    [Test]
    public void Invoke_NullConfig_ResolvesDefaultView()
    {
        var component = new LayoutViewComponent();
        var widget = WidgetComponentHarness.Attach(component);
        widget.Hit("Layout", WidgetRenderMode.View, "default", "view.Default");

        var result = component.Invoke(null!);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
        });
    }

    [Test]
    public void Invoke_WithConfig_ResolvesPickedView()
    {
        var component = new LayoutViewComponent();
        var widget = WidgetComponentHarness.Attach(component);
        widget.Hit("Layout", WidgetRenderMode.View, "MyLayout", "view.MyLayout");

        var result = component.Invoke(new LayoutContentZoneConfiguration { ViewName = "MyLayout" });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.MyLayout"));
    }

    [Test]
    public void Invoke_EditModeContext_ResolvesEditView()
    {
        var component = new LayoutViewComponent();
        var widget = WidgetComponentHarness.Attach(component);
        widget.Hit("Layout", WidgetRenderMode.Edit, "default", "edit.default");

        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        var result = component.Invoke(new LayoutContentZoneConfiguration());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("edit.default"));
    }
}
