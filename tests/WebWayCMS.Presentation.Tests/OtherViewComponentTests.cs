using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.CMSRoute;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Layout;
using WebWayCMS.Models.Page;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentBlockViewComponentTests
{
    private IContentBlockModel _model = null!;
    private ContentBlockViewComponent _component = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentBlockModel>();
        _component = new ContentBlockViewComponent(_model);
        ViewComponentHarness.Attach(_component);
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
        var id = Guid.NewGuid();
        _model.GetViewModelByNodeIdAsync(id, Arg.Any<CancellationToken>()).Returns(new ContentBlockViewModel(), (ContentBlockViewModel?)null);

        Assert.That(ViewComponentHarness.Model(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id })),
            Is.InstanceOf<ContentBlockViewModel>());
        // Second call returns null -> falls back to a new view model carrying the node id.
        var fallback = (ContentBlockViewModel)ViewComponentHarness.Model(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id }))!;
        Assert.That(fallback.NodeId, Is.EqualTo(id));
    }
}

[TestFixture]
public class ContentZoneViewComponentTests
{
    private IContentZoneModel _model = null!;
    private IWidgetRegistry _registry = null!;
    private ContentZoneViewComponent _component = null!;
    private Microsoft.AspNetCore.Http.DefaultHttpContext _http = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentZoneModel>();
        _registry = Substitute.For<IWidgetRegistry>();
        _component = new ContentZoneViewComponent(_model, _registry);
        _http = ViewComponentHarness.Attach(_component);
    }

    private static ContentZoneViewModel ZoneVm(bool withItems = false, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Z",
        ZoneObjects = withItems ? new List<ContentZoneObject> { new() } : new List<ContentZoneObject>()
    };

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneViewComponent(null!, _registry), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneViewComponent(_model, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ZoneId_DirectLookup_RendersZone()
    {
        var zoneId = Guid.NewGuid();
        _model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneId: zoneId);

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task WhitespaceZoneName_RendersEmpty()
    {
        Assert.That(await _component.InvokeAsync(zoneName: "  "), Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task PageScoped_TopLevelSlot_GetsOrCreatesByPageSlot()
    {
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } }
        };
        _model.GetOrCreateViewModelByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Main");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task PageScoped_NestedSlot_GetsOrCreatesByZoneSlot()
    {
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } }
        };
        _component.ViewComponentContext.ViewData["ContentZone:ParentZoneId"] = Guid.NewGuid();
        _model.GetOrCreateViewModelByZoneSlotAsync(Arg.Any<Guid>(), "Sub", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Sub");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task NestedSlot_WithoutPageData_GetsOrCreatesByZoneSlot()
    {
        _component.ViewComponentContext.ViewData["ContentZone:ParentZoneId"] = Guid.NewGuid();
        _model.GetOrCreateViewModelByZoneSlotAsync(Arg.Any<Guid>(), "Sub", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Sub");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task InheritedEditMode_FromViewData_RendersEditView()
    {
        _component.ViewComponentContext.ViewData["ContentZone:EditMode"] = true;
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm());
        _registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<WidgetRegistrationInfo>>());

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Edit"));
            Assert.That(_component.ViewComponentContext.ViewData.ContainsKey("ComponentsByCategory"), Is.True);
        });
    }

    [Test]
    public async Task GlobalZone_GetsOrCreatesByName()
    {
        _model.GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Footer", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task NullViewModel_BuildsEmptyAndRendersEmptyContent()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel)null!);

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task EditMode_RendersEditViewAndSetsComponents()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm());
        _registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<WidgetRegistrationInfo>>());

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true, editMode: true);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Edit"));
            Assert.That(_component.ViewComponentContext.ViewData.ContainsKey("ComponentsByCategory"), Is.True);
        });
    }

    [Test]
    public async Task NoItems_NotEditMode_RendersEmpty()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: false));

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }
}

[TestFixture]
public class LayoutViewComponentTests
{
    [Test]
    public void Invoke_NullConfig_RendersDefaultView()
    {
        var component = new LayoutViewComponent();
        ViewComponentHarness.Attach(component);

        Assert.That(component.Invoke(null!), Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public void Invoke_WithConfig_RendersNamedView()
    {
        var component = new LayoutViewComponent();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new LayoutContentZoneConfiguration { ViewName = "MyLayout" });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("MyLayout"));
    }
}

[TestFixture]
public class PageViewComponentTests
{
    private IPageModel _model = null!;
    private PageViewComponent _component = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IPageModel>();
        _component = new PageViewComponent(_model);
        ViewComponentHarness.Attach(_component);
    }

    [Test]
    public void Constructor_Null_Throws()
        => Assert.That(() => new PageViewComponent(null!), Throws.ArgumentNullException);

    private static PageTreeNode Node(string route, bool published = true, bool hidden = false, params PageTreeNode[] children) => new()
    {
        Path = route,
        Title = route,
        PageNodeId = Guid.NewGuid(),
        IsPublished = published,
        IsHidden = hidden,
        Children = children.ToList()
    };

    [Test]
    public async Task DefaultView_FiltersDraftHiddenAndAdminRoutes_Recursively()
    {
        _model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(new PageIndexViewModel
        {
            Pages = new List<PageTreeNode>
            {
                Node("/a", children: Node("/a/b")),
                Node("/draft", published: false),
                Node("/hidden", hidden: true),
                Node("/wadmin/x"),
                new() { Path = "/intermediate", PageNodeId = null } // no PageNodeId -> filtered out
			}
        });

        var result = await _component.InvokeAsync();
        var vm = (PageNavigationViewModel)ViewComponentHarness.Model(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Default"));
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/a" }));
            Assert.That(vm.Items[0].Children.Single().Path, Is.EqualTo("/a/b"));
        });
    }

    [Test]
    public async Task AdminPagesMode_IncludesOnlyAdminRoutes_AndCustomView()
    {
        _model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(new PageIndexViewModel
        {
            Pages = new List<PageTreeNode> { Node("/wadmin/x"), Node("/public") }
        });

        var result = await _component.InvokeAsync(new PageContentZoneConfiguration { AdminPages = true, ViewName = "Menu" });
        var vm = (PageNavigationViewModel)ViewComponentHarness.Model(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Menu"));
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/wadmin/x" }));
        });
    }

    [Test]
    public async Task ShowDraftAndHidden_IncludesThem()
    {
        _model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(new PageIndexViewModel
        {
            Pages = new List<PageTreeNode> { Node("/draft", published: false), Node("/hidden", hidden: true) }
        });

        var result = await _component.InvokeAsync(new PageContentZoneConfiguration { ShowDraftPages = true, ShowHiddenPages = true });
        var vm = (PageNavigationViewModel)ViewComponentHarness.Model(result)!;

        Assert.That(vm.Items, Has.Count.EqualTo(2));
    }
}

[TestFixture]
public class RouteNavigationViewComponentTests
{
    private ICMSRouteRegistry _routeRegistry = null!;
    private RouteNavigationViewComponent _component = null!;

    [SetUp]
    public void SetUp()
    {
        _routeRegistry = Substitute.For<ICMSRouteRegistry>();
        _component = new RouteNavigationViewComponent(_routeRegistry);
        ViewComponentHarness.Attach(_component);
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Default"));
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Menu"));
            Assert.That(vm.Items.Select(i => i.Path), Is.EqualTo(new[] { "/wadmin/page" }));
        });
    }

    [Test]
    public void Invoke_BlankViewName_FallsBackToDefaultView()
    {
        Routes(Route("/home"));

        var result = _component.Invoke(new RouteNavigationConfiguration { ViewName = "   " });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Default"));
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
