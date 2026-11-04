using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.CMSRoute;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class RouteNavigationViewComponentTests
{
    private ICMSRouteRegistry _routeRegistry = null!;
    private RouteNavigationViewComponent _component = null!;
    private WidgetComponentHarness.Context _widget = null!;

    [SetUp]
    public void SetUp()
    {
        _routeRegistry = Substitute.For<ICMSRouteRegistry>();
        _component = new RouteNavigationViewComponent(_routeRegistry);
        _widget = WidgetComponentHarness.Attach(_component);
        _widget.Hit("RouteNavigation", WidgetRenderMode.View, "default", "view.Default");
        _widget.Hit("RouteNavigation", WidgetRenderMode.View, "Menu", "view.Menu");
        _widget.Hit("RouteNavigation", WidgetRenderMode.Admin, "default", "admin.default");
    }

    private void Routes(params CMSRouteDTO[] routes)
        => _routeRegistry.GetActiveRoutes().Returns(routes.ToList());

    // Named by default — an unnamed route is filtered out, so tests that care about
    // filtering/nesting need a name. Use Unnamed(...) to exercise the skip behaviour.
    private static CMSRouteDTO Route(string pattern, bool reserved = false, string? navigationName = null) => new()
    {
        Pattern = pattern,
        NavigationName = navigationName ?? pattern,
        IsReserved = reserved
    };

    private static CMSRouteDTO Unnamed(string pattern, string? navigationName = null) => new()
    {
        Pattern = pattern,
        NavigationName = navigationName
    };

    private RouteNavigationViewModel Invoke(RouteNavigationConfiguration? config = null)
        => (RouteNavigationViewModel)ViewComponentHarness.Model(_component.Invoke(config))!;

    [Test]
    public void Constructor_Null_Throws()
        => Assert.That(() => new RouteNavigationViewComponent(null!), Throws.ArgumentNullException);

    [Test]
    public void Invoke_NullConfig_ExcludesParameterizedPatterns_AndKeepsRegistryOrder()
    {
        Routes(
            Route("/home"),
            Route("/blog/{slug}"),
            Route("/about"),
            Route("/articles/{id:int}"));

        var result = _component.Invoke();
        var vm = (RouteNavigationViewModel)ViewComponentHarness.Model(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/home", "/about" }));
            Assert.That(vm.Items.Select(i => i.Title), Is.EqualTo(new[] { "/home", "/about" }));
        });
    }

    [Test]
    public void Invoke_EmptyActiveRoutes_RendersEmptyList()
    {
        Routes();

        Assert.That(Invoke().Items, Is.Empty);
    }

    [Test]
    public void Invoke_ExcludesReservedRoutes_ByDefault()
    {
        Routes(Route("/home"), Route("/blocked", reserved: true));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/home" }));
    }

    [Test]
    public void Invoke_IncludeReserved_KeepsReservedRoutes()
    {
        Routes(Route("/home"), Route("/blocked", reserved: true));

        var vm = Invoke(new RouteNavigationConfiguration { IncludeReserved = true });

        Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/home", "/blocked" }));
    }

    [Test]
    public void Invoke_ExcludesAdminRoutes_ByDefault()
    {
        Routes(Route("/wadmin/page"), Route("/public"));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/public" }));
    }

    [Test]
    public void Invoke_PublicRouteSharingTheAdminPrefix_StaysInThePublicNav()
    {
        // The admin partition must land on a segment boundary: '/wadmin-guide' is a public page.
        Routes(Route("/wadmin-guide"), Route("/wadministration"), Route("/wadmin"));

        Assert.That(Invoke().Items.Select(i => i.Path),
            Is.EqualTo(new[] { "/wadmin-guide", "/wadministration" }));
    }

    [Test]
    public void Invoke_AdminRoutes_ExcludesPublicRoutesSharingTheAdminPrefix()
    {
        Routes(Route("/wadmin-guide"), Route("/wadmin"), Route("/wadmin/pages"));

        var vm = Invoke(new RouteNavigationConfiguration { AdminRoutes = true });

        Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/wadmin" }));
    }

    [Test]
    public void Invoke_AdminRoutes_IncludesOnlyAdminRoutes_AndCustomView()
    {
        Routes(Route("/wadmin/page"), Route("/public"));

        var result = _component.Invoke(new RouteNavigationConfiguration { AdminRoutes = true, ViewName = "Menu" });
        var vm = (RouteNavigationViewModel)ViewComponentHarness.Model(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Menu"));
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/wadmin/page" }));
        });
    }

    [Test]
    public void Invoke_BlankViewName_FallsBackToDefaultView()
    {
        Routes(Route("/home"));

        var result = _component.Invoke(new RouteNavigationConfiguration { ViewName = "   " });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
    }

    [Test]
    public void Invoke_NestsDescendantsUnderTheirAncestorPattern()
    {
        Routes(Route("/blog"), Route("/blog/news"), Route("/blog/news/2026"));

        var vm = Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/blog" }));
            Assert.That(vm.Items[0].Children.Single().Path, Is.EqualTo("/blog/news"));
            Assert.That(vm.Items[0].Children[0].Children.Single().Path, Is.EqualTo("/blog/news/2026"));
        });
    }

    [Test]
    public void Invoke_MissingIntermediateRoute_NestsUnderNearestSurvivingAncestor()
    {
        Routes(Route("/blog"), Route("/blog/news/2026"));

        var vm = Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/blog" }));
            Assert.That(vm.Items[0].Children.Single().Path, Is.EqualTo("/blog/news/2026"));
        });
    }

    [Test]
    public void Invoke_OrphanWithNoAncestorRoute_StaysAtRoot()
    {
        Routes(Route("/blog/news"));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/blog/news" }));
    }

    [Test]
    public void Invoke_SiteRoot_IsASiblingAndAdoptsNothing()
    {
        Routes(Route("/"), Route("/about"));

        var vm = Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/", "/about" }));
            Assert.That(vm.Items[0].Children, Is.Empty);
        });
    }

    [Test]
    public void Invoke_DuplicatePatterns_AreCollapsedToOneItem()
    {
        Routes(Route("/home"), Route("/home"));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/home" }));
    }

    [Test]
    public void Invoke_UsesNavigationNameAsLinkText()
    {
        Routes(Route("/about-us", navigationName: "About Us"));

        var vm = Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Single().Title, Is.EqualTo("About Us"));
            Assert.That(vm.Items.Single().Path, Is.EqualTo("/about-us"));
        });
    }

    [Test]
    public void Invoke_RoutesWithNoNavigationName_AreOmitted()
    {
        Routes(Route("/home"), Unnamed("/hidden"));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/home" }));
    }

    [Test]
    public void Invoke_WhitespaceNavigationName_IsTreatedAsUnnamed()
    {
        Routes(Route("/home"), Unnamed("/blank", "   "));

        Assert.That(Invoke().Items.Select(i => i.Path), Is.EqualTo(new[] { "/home" }));
    }

    [Test]
    public void Invoke_NamedChildOfUnnamedParent_RisesToTheRoot()
    {
        Routes(Unnamed("/blog"), Route("/blog/news", navigationName: "News"));

        var vm = Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/blog/news" }));
            Assert.That(vm.Items.Single().Title, Is.EqualTo("News"));
        });
    }
}
