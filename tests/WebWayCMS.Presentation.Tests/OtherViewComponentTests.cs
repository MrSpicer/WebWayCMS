using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
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
		_model.GetViewModelByMasterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new ContentBlockViewModel(), (ContentBlockViewModel?)null);

		Assert.That(ViewComponentHarness.Model(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id })),
			Is.InstanceOf<ContentBlockViewModel>());
		// Second call returns null -> falls back to a new view model carrying the id.
		var fallback = (ContentBlockViewModel)ViewComponentHarness.Model(await _component.InvokeAsync(new ContentBlockContentZoneConfiguration { ContentBlockID = id }))!;
		Assert.That(fallback.Id, Is.EqualTo(id));
	}
}

[TestFixture]
public class ContentZoneViewComponentTests
{
	private IContentZoneModel _model = null!;
	private IContentZoneComponentRegistry _registry = null!;
	private ContentZoneViewComponent _component = null!;
	private Microsoft.AspNetCore.Http.DefaultHttpContext _http = null!;

	[SetUp]
	public void SetUp()
	{
		_model = Substitute.For<IContentZoneModel>();
		_registry = Substitute.For<IContentZoneComponentRegistry>();
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
		_http.Items["CMS:PageData"] = new PageDTO { ContentMeta = new ContentDTO { MasterId = Guid.NewGuid() } };
		_model.GetOrCreateViewModelByPageSlotAsync(Arg.Any<Guid>(), "Main", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

		var result = await _component.InvokeAsync(zoneName: "Main");

		Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
	}

	[Test]
	public async Task PageScoped_NestedSlot_GetsOrCreatesByZoneSlot()
	{
		_http.Items["CMS:PageData"] = new PageDTO { ContentMeta = new ContentDTO { MasterId = Guid.NewGuid() } };
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
		_registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<ContentZoneComponentInfo>>());

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
		_model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel?)null);

		var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

		Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
	}

	[Test]
	public async Task EditMode_RendersEditViewAndSetsComponents()
	{
		_model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm());
		_registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<ContentZoneComponentInfo>>());

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
		Route = route,
		Title = route,
		PageId = Guid.NewGuid(),
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
				Node("/admin/x"),
				new() { Route = "/intermediate", PageId = null } // no PageId -> filtered out
			}
		});

		var result = await _component.InvokeAsync();
		var vm = (PageNavigationViewModel)ViewComponentHarness.Model(result)!;

		Assert.Multiple(() =>
		{
			Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Default"));
			Assert.That(vm.Items.Select(i => i.Route), Is.EqualTo(new[] { "/a" }));
			Assert.That(vm.Items[0].Children.Single().Route, Is.EqualTo("/a/b"));
		});
	}

	[Test]
	public async Task AdminPagesMode_IncludesOnlyAdminRoutes_AndCustomView()
	{
		_model.GetPageIndexAsync(Arg.Any<CancellationToken>()).Returns(new PageIndexViewModel
		{
			Pages = new List<PageTreeNode> { Node("/admin/x"), Node("/public") }
		});

		var result = await _component.InvokeAsync(new PageContentZoneConfiguration { AdminPages = true, ViewName = "Menu" });
		var vm = (PageNavigationViewModel)ViewComponentHarness.Model(result)!;

		Assert.Multiple(() =>
		{
			Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Menu"));
			Assert.That(vm.Items.Select(i => i.Route), Is.EqualTo(new[] { "/admin/x" }));
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
