using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Page;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class PageViewComponentTests
{
    private IPageModel _model = null!;
    private PageViewComponent _component = null!;
    private WidgetComponentHarness.Context _widget = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IPageModel>();
        _component = new PageViewComponent(_model);
        _widget = WidgetComponentHarness.Attach(_component);
        _widget.Hit("Page", WidgetRenderMode.View, "default", "view.Default");
        _widget.Hit("Page", WidgetRenderMode.View, "Menu", "view.Menu");
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Default"));
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("view.Menu"));
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
