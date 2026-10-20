using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class WidgetViewResolverTests
{
    private IViewDiscoveryService _discovery = null!;
    private WidgetViewResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _discovery = Substitute.For<IViewDiscoveryService>();
        _resolver = new WidgetViewResolver(_discovery);
    }

    // A minimal concrete widget configuration for exercising the fallback chains.
    private sealed class TestWidgetConfiguration : WidgetConfiguration
    {
    }

    private static TestWidgetConfiguration Config(string? viewName, string? listViewName, WidgetMetadata? metadata = null)
        => new() { ViewName = viewName, ListViewName = listViewName, Metadata = metadata ?? new WidgetMetadata() };

    // NSubstitute: the out parameter is set inside the Returns callback via the args array
    // (index 3 = the 'out string? viewName' of TryResolveView).
    private void Miss(string component, WidgetRenderMode mode, string? variant)
        => _discovery.TryResolveView(component, mode, variant, out Arg.Any<string?>())
            .Returns(x => { x[3] = null; return false; });

    private void Hit(string component, WidgetRenderMode mode, string? variant, string view)
        => _discovery.TryResolveView(component, mode, variant, out Arg.Any<string?>())
            .Returns(x => { x[3] = view; return true; });

    [Test]
    public void Constructor_NullDiscovery_Throws()
        => Assert.That(() => new WidgetViewResolver(null!), Throws.ArgumentNullException);

    [Test]
    public void TryResolveView_DelegatesToDiscovery()
    {
        Hit("C", WidgetRenderMode.View, "x", "view.x");

        var ok = _resolver.TryResolveView("C", WidgetRenderMode.View, "x", out var view);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(view, Is.EqualTo("view.x"));
        });
        _discovery.Received(1).TryResolveView("C", WidgetRenderMode.View, "x", out _);
    }

    [Test]
    public void TryResolveView_Miss_ReturnsFalse()
    {
        Miss("C", WidgetRenderMode.View, "nope");

        var ok = _resolver.TryResolveView("C", WidgetRenderMode.View, "nope", out var view);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(view, Is.Null);
        });
    }

    // ─── Edit mode ────────────────────────────────────────────────────────────

    private static WidgetMetadata ListMetadata() => new() { DisplayMode = WidgetDisplayMode.List };

    [Test]
    public void Resolve_Edit_Single_MirrorsViewName()
    {
        Hit("C", WidgetRenderMode.Edit, "TwoColumnEqual", "edit.TwoColumnEqual");
        Hit("C", WidgetRenderMode.Edit, "default", "edit.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.Edit, Config("TwoColumnEqual", "dense"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("edit.TwoColumnEqual"));
        });
    }

    [Test]
    public void Resolve_Edit_List_MirrorsListViewName()
    {
        Hit("C", WidgetRenderMode.Edit, "TwoColumnEqual", "edit.TwoColumnEqual");
        Hit("C", WidgetRenderMode.Edit, "dense", "edit.dense");

        var result = _resolver.Resolve("C", WidgetRenderMode.Edit, Config("TwoColumnEqual", "dense", ListMetadata()));

        Assert.That(result.ViewName, Is.EqualTo("edit.dense"));
    }

    [Test]
    public void Resolve_Edit_NoMirroredView_FallsBackToEditDefault()
    {
        Miss("C", WidgetRenderMode.Edit, "v");
        Hit("C", WidgetRenderMode.Edit, "default", "edit.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.Edit, Config("v", null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("edit.default"));
        });
    }

    [Test]
    public void Resolve_Edit_NoEditViews_ReturnsEditCard()
    {
        Miss("C", WidgetRenderMode.Edit, "v");
        Miss("C", WidgetRenderMode.Edit, "default");

        var result = _resolver.Resolve("C", WidgetRenderMode.Edit, Config("v", null));

        Assert.That(result, Is.EqualTo(WidgetViewResolution.EditCard));
    }

    [Test]
    public void Resolve_Edit_NullConfig_NoEditViews_ReturnsEditCard()
    {
        Miss("C", WidgetRenderMode.Edit, null);
        Miss("C", WidgetRenderMode.Edit, "default");

        var result = _resolver.Resolve("C", WidgetRenderMode.Edit, null);

        Assert.That(result, Is.EqualTo(WidgetViewResolution.EditCard));
    }

    // ─── List mode ────────────────────────────────────────────────────────────

    [Test]
    public void Resolve_List_PickedListView_Wins()
    {
        Hit("C", WidgetRenderMode.List, "dense", "list.dense");

        var result = _resolver.Resolve("C", WidgetRenderMode.List, Config("v", "dense"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("list.dense"));
        });
    }

    [Test]
    public void Resolve_List_NoPickedView_FallsBackToListDefault()
    {
        Miss("C", WidgetRenderMode.List, null);
        Hit("C", WidgetRenderMode.List, "default", "list.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.List, Config("v", null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("list.default"));
        });
    }

    [Test]
    public void Resolve_List_NoListViews_FallsBackToViewMode()
    {
        Miss("C", WidgetRenderMode.List, null);
        Miss("C", WidgetRenderMode.List, "default");
        Hit("C", WidgetRenderMode.View, "v", "view.v");

        var result = _resolver.Resolve("C", WidgetRenderMode.List, Config("v", null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("view.v"));
        });
    }

    [Test]
    public void Resolve_List_NoListViews_NoPickedView_FallsBackToViewDefault()
    {
        Miss("C", WidgetRenderMode.List, null);
        Miss("C", WidgetRenderMode.List, "default");
        Miss("C", WidgetRenderMode.View, null);
        Hit("C", WidgetRenderMode.View, "default", "view.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.List, Config(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("view.default"));
        });
    }

    // ─── Admin / AdminList modes ──────────────────────────────────────────────

    [Test]
    public void Resolve_Admin_MirrorsViewName()
    {
        Hit("C", WidgetRenderMode.Admin, "nav", "admin.nav");

        var result = _resolver.Resolve("C", WidgetRenderMode.Admin, Config("nav", null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("admin.nav"));
        });
    }

    [Test]
    public void Resolve_Admin_NoMirroredView_FallsBackToAdminDefault_ThenViewMode()
    {
        Miss("C", WidgetRenderMode.Admin, "v");
        Hit("C", WidgetRenderMode.Admin, "default", "admin.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.Admin, Config("v", null));

        Assert.That(result.ViewName, Is.EqualTo("admin.default"));

        Miss("C", WidgetRenderMode.Admin, "default");
        Hit("C", WidgetRenderMode.View, "v", "view.v");

        var fallback = _resolver.Resolve("C", WidgetRenderMode.Admin, Config("v", null));

        Assert.That(fallback.ViewName, Is.EqualTo("view.v"));
    }

    [Test]
    public void Resolve_AdminList_MirrorsListViewName()
    {
        Hit("C", WidgetRenderMode.AdminList, "dense", "adminlist.dense");

        var result = _resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense"));

        Assert.That(result.ViewName, Is.EqualTo("adminlist.dense"));
    }

    [Test]
    public void Resolve_AdminList_FallsBackThroughAdminListDefault_Admin_List_View()
    {
        // adminlist.default
        Miss("C", WidgetRenderMode.AdminList, "dense");
        Hit("C", WidgetRenderMode.AdminList, "default", "adminlist.default");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")).ViewName,
            Is.EqualTo("adminlist.default"));

        // → admin chain (mirrored single variant)
        Miss("C", WidgetRenderMode.AdminList, "default");
        Hit("C", WidgetRenderMode.Admin, "v", "admin.v");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")).ViewName,
            Is.EqualTo("admin.v"));

        // → admin.default
        Miss("C", WidgetRenderMode.Admin, "v");
        Hit("C", WidgetRenderMode.Admin, "default", "admin.default");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")).ViewName,
            Is.EqualTo("admin.default"));

        // → list chain
        Miss("C", WidgetRenderMode.Admin, "default");
        Hit("C", WidgetRenderMode.List, "dense", "list.dense");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")).ViewName,
            Is.EqualTo("list.dense"));

        // → view chain
        Miss("C", WidgetRenderMode.List, "dense");
        Miss("C", WidgetRenderMode.List, "default");
        Hit("C", WidgetRenderMode.View, "v", "view.v");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")).ViewName,
            Is.EqualTo("view.v"));

        // → nothing
        Miss("C", WidgetRenderMode.View, "v");
        Miss("C", WidgetRenderMode.View, "default");
        Assert.That(_resolver.Resolve("C", WidgetRenderMode.AdminList, Config("v", "dense")),
            Is.EqualTo(WidgetViewResolution.Empty));
    }

    // ─── View mode ────────────────────────────────────────────────────────────

    [Test]
    public void Resolve_View_PickedView_Wins()
    {
        Hit("C", WidgetRenderMode.View, "v", "view.v");

        var result = _resolver.Resolve("C", WidgetRenderMode.View, Config("v", null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("view.v"));
        });
    }

    [Test]
    public void Resolve_View_NoPickedView_FallsBackToViewDefault()
    {
        Miss("C", WidgetRenderMode.View, null);
        Hit("C", WidgetRenderMode.View, "default", "view.default");

        var result = _resolver.Resolve("C", WidgetRenderMode.View, Config(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WidgetViewResolutionKind.View));
            Assert.That(result.ViewName, Is.EqualTo("view.default"));
        });
    }

    [Test]
    public void Resolve_View_NoViews_ReturnsEmpty()
    {
        Miss("C", WidgetRenderMode.View, null);
        Miss("C", WidgetRenderMode.View, "default");

        var result = _resolver.Resolve("C", WidgetRenderMode.View, null);

        Assert.That(result, Is.EqualTo(WidgetViewResolution.Empty));
    }

    [Test]
    public void ResolutionRecords_FactoriesAreEqual()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WidgetViewResolution.Of("view.x"), Is.EqualTo(new WidgetViewResolution(WidgetViewResolutionKind.View, "view.x")));
            Assert.That(WidgetViewResolution.EditCard.Kind, Is.EqualTo(WidgetViewResolutionKind.EditCard));
            Assert.That(WidgetViewResolution.Empty.Kind, Is.EqualTo(WidgetViewResolutionKind.Empty));
        });
    }
}
