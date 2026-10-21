using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Services;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class WidgetViewComponentTests
{
    // Concrete widget: type name "TestWidgetViewComponent" -> ComponentName "TestWidget".
    private sealed class TestWidgetViewComponent : WidgetViewComponent
    {
        public string Name => ComponentName;

        public IViewComponentResult Render(WidgetConfiguration config, object? model = null)
            => ModeView(config, model);

        public WidgetRenderMode Mode(WidgetConfiguration? config) => RenderModeFor(config);

        public static WidgetDisplayMode Display(WidgetConfiguration? config) => DisplayModeFor(config);
    }

    // Type name without the suffix keeps its full name as ComponentName.
    private sealed class Widget : WidgetViewComponent
    {
        public string Name => ComponentName;

        public IViewComponentResult Render(WidgetConfiguration config)
            => ModeView(config, (object?)null);
    }

    // [ViewComponent(Name = ...)] overrides the type name, exactly as MVC names the component.
    [ViewComponent(Name = "Renamed")]
    private sealed class NamedWidgetViewComponent : WidgetViewComponent
    {
        public string Name => ComponentName;
    }

    private static TestWidgetViewComponent Component(out WidgetComponentHarness.Context widget)
    {
        var component = new TestWidgetViewComponent();
        widget = WidgetComponentHarness.Attach(component);
        return component;
    }

    [Test]
    public void ComponentName_HonoursViewComponentAttribute_ElseStripsSuffix_OrKeepsTheTypeName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new TestWidgetViewComponent().Name, Is.EqualTo("TestWidget"));
            Assert.That(new Widget().Name, Is.EqualTo("Widget"));
            Assert.That(new NamedWidgetViewComponent().Name, Is.EqualTo("Renamed"));
        });
    }

    private static LayoutConfig ListConfig()
        => new() { Metadata = new WidgetMetadata { DisplayMode = WidgetDisplayMode.List } };

    [Test]
    public void RenderModeFor_PublicPath_ViewOrListByDisplayMode()
    {
        var component = Component(out _);
        component.HttpContext.Request.Path = "/about";

        Assert.Multiple(() =>
        {
            Assert.That(component.Mode(null), Is.EqualTo(WidgetRenderMode.View));
            Assert.That(component.Mode(new LayoutConfig()), Is.EqualTo(WidgetRenderMode.View));
            Assert.That(component.Mode(ListConfig()), Is.EqualTo(WidgetRenderMode.List));
        });
    }

    [Test]
    public void RenderModeFor_AdminPath_AdminOrAdminListByDisplayMode()
    {
        var component = Component(out _);
        component.HttpContext.Request.Path = "/wadmin/pages";

        Assert.Multiple(() =>
        {
            Assert.That(component.Mode(new LayoutConfig()), Is.EqualTo(WidgetRenderMode.Admin));
            Assert.That(component.Mode(ListConfig()), Is.EqualTo(WidgetRenderMode.AdminList));
        });
    }

    [Test]
    public void RenderModeFor_AdminLookalikePath_IsPublic()
    {
        var component = Component(out _);
        component.HttpContext.Request.Path = "/wadministration";

        Assert.That(component.Mode(new LayoutConfig()), Is.EqualTo(WidgetRenderMode.View));
    }

    [Test]
    public void RenderModeFor_ZoneEditMode_WinsOverPathAndDisplayMode()
    {
        var component = Component(out _);
        component.HttpContext.Request.Path = "/wadmin/contentzones";
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        Assert.That(component.Mode(ListConfig()), Is.EqualTo(WidgetRenderMode.Edit));
    }

    [Test]
    public void RenderModeFor_ZoneViewMode_FallsThroughToPath()
    {
        var component = Component(out _);
        component.HttpContext.Request.Path = "/wadmin";
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.View;

        Assert.That(component.Mode(new LayoutConfig()), Is.EqualTo(WidgetRenderMode.Admin));
    }

    [Test]
    public void DisplayModeFor_DefaultsToSingle()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TestWidgetViewComponent.Display(null), Is.EqualTo(WidgetDisplayMode.Single));
            Assert.That(TestWidgetViewComponent.Display(new LayoutConfig()), Is.EqualTo(WidgetDisplayMode.Single));
            Assert.That(TestWidgetViewComponent.Display(ListConfig()), Is.EqualTo(WidgetDisplayMode.List));
        });
    }

    [Test]
    public void ModeView_ConfigWithZoneItem_OwnsNestedZones()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.View, "default", "view.default");
        var itemNodeId = Guid.NewGuid();

        component.Render(new LayoutConfig { ZoneItemNodeId = itemNodeId });

        Assert.That(component.ViewComponentContext.ViewData["ContentZone:ParentItemId"], Is.EqualTo(itemNodeId));
    }

    [Test]
    public void ModeView_ConfigWithoutZoneItem_LeavesParentItemAlone()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.View, "default", "view.default");

        component.Render(new LayoutConfig());

        Assert.That(component.ViewComponentContext.ViewData.ContainsKey("ContentZone:ParentItemId"), Is.False);
    }

    [Test]
    public void ModeView_ViewMode_ResolvesConfiguredViewWithModel()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.View, "custom", "view.custom");

        var result = component.Render(new LayoutConfig { ViewName = "custom" }, "the-model");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.custom"));
            Assert.That(ViewComponentHarness.Model(result), Is.EqualTo("the-model"));
        });
    }

    [Test]
    public void ModeView_EditMode_FromViewData_ResolvesEditView()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.Edit, "fancy", "edit.fancy");
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        var result = component.Render(new LayoutConfig { ViewName = "fancy" });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("edit.fancy"));
    }

    [Test]
    public void ModeView_EditMode_NoEditViews_RendersGenericEditCard()
    {
        var component = Component(out var widget);
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        // Unregistered widget -> null registration info model.
        var unregistered = component.Render(new LayoutConfig());
        Assert.Multiple(() =>
        {
            Assert.That(unregistered, Is.InstanceOf<ViewViewComponentResult>());
            Assert.That(ViewComponentHarness.ViewName(unregistered), Is.EqualTo("~/Views/Shared/_WidgetEditCard.cshtml"));
            Assert.That(ViewComponentHarness.Model(unregistered), Is.Null);
        });

        // Registered widget -> the registration info is passed to the card.
        var info = new WidgetRegistrationInfo { Name = "TestWidget", DisplayName = "Test Widget", IconClass = "fa-cube", Description = "desc" };
        widget.Registry.GetByName("TestWidget").Returns(info);
        var registered = component.Render(new LayoutConfig());
        Assert.That(ViewComponentHarness.Model(registered), Is.SameAs(info));
    }

    [Test]
    public void ModeView_ViewMode_NoViewViews_RendersEmptyContent()
    {
        var component = Component(out _);

        var result = component.Render(new LayoutConfig());

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public void ModeView_AdminPath_ResolvesAdminView()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.Admin, "default", "admin.default");
        component.HttpContext.Request.Path = "/wadmin";

        var result = component.Render(new LayoutConfig());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("admin.default"));
    }

    [Test]
    public void ModeView_ListDisplayMode_ResolvesListView()
    {
        var component = Component(out var widget);
        widget.Hit("TestWidget", WidgetRenderMode.List, "default", "list.default");

        var result = component.Render(ListConfig());

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("list.default"));
    }

    [Test]
    public void ModeView_NullConfig_DefaultsToViewMode()
    {
        // No-config widgets (e.g. the host's FaqCount) pass null; the mode falls back to view mode.
        var component = new Widget();
        WidgetComponentHarness.Attach(component);

        var result = component.Render(null!);

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public void ModeView_EditMode_NoRegistryInServices_EditCardModelIsNull()
    {
        // A provider without IWidgetRegistry: the generic edit card still renders, with a null model.
        var component = new TestWidgetViewComponent();
        var discovery = Substitute.For<IViewDiscoveryService>();
        ViewComponentHarness.Attach(component, services =>
        {
            services.AddSingleton<IWidgetViewResolver>(new WidgetViewResolver(discovery));
            services.AddSingleton<ICompositeViewEngine>(Substitute.For<ICompositeViewEngine>());
        });
        component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;

        var result = component.Render(new LayoutConfig());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("~/Views/Shared/_WidgetEditCard.cshtml"));
            Assert.That(ViewComponentHarness.Model(result), Is.Null);
        });
    }

    // Minimal concrete configuration for the component tests.
    private sealed class LayoutConfig : WidgetConfiguration
    {
    }
}
