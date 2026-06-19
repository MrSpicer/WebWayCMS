using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Presentation.Components;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class CmsPageTitleTests
{
	[Test]
	public void ForPage_NullPage_ReturnsFallback()
		=> Assert.That(CmsPageTitle.ForPage(null), Is.EqualTo("Page - WebWayCMS"));

	[Test]
	public void ForPage_NullContentMeta_ReturnsFallback()
		=> Assert.That(CmsPageTitle.ForPage(new PageDTO { ContentMeta = null! }), Is.EqualTo("Page - WebWayCMS"));

	[Test]
	public void ForPage_WhitespaceTitle_ReturnsFallback()
		=> Assert.That(CmsPageTitle.ForPage(new PageDTO { ContentMeta = new ContentDTO { Title = "   " } }), Is.EqualTo("Page - WebWayCMS"));

	[Test]
	public void ForPage_WithTitle_AppendsSuffix()
		=> Assert.That(CmsPageTitle.ForPage(new PageDTO { ContentMeta = new ContentDTO { Title = "Home" } }), Is.EqualTo("Home - WebWayCMS"));
}

[TestFixture]
public class ContentZoneWidgetRegistryTests
{
	[Test]
	public void Constructor_NullMap_Throws()
		=> Assert.That(() => new ContentZoneWidgetRegistry(null!), Throws.ArgumentNullException);

	[Test]
	public void Resolve_KnownAndUnknownNames()
	{
		var registry = new ContentZoneWidgetRegistry(new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
		{
			["ContentBlock"] = typeof(WebWayCMS.Presentation.Components.Widgets.ContentBlockWidget),
		});

		Assert.Multiple(() =>
		{
			Assert.That(registry.Resolve("ContentBlock"), Is.EqualTo(typeof(WebWayCMS.Presentation.Components.Widgets.ContentBlockWidget)));
			Assert.That(registry.Resolve("NotMigrated"), Is.Null);
		});
	}
}

[TestFixture]
public class ResultActionResultTests
{
	[Test]
	public void Constructor_NullResult_Throws()
		=> Assert.That(() => new ResultActionResult(null!), Throws.ArgumentNullException);

	[Test]
	public async Task ExecuteResultAsync_ExecutesWrappedResult()
	{
		var inner = Substitute.For<IResult>();
		var http = new DefaultHttpContext();
		var actionResult = new ResultActionResult(inner);

		Assert.That(actionResult.Result, Is.SameAs(inner));

		await actionResult.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));

		await inner.Received(1).ExecuteAsync(http);
	}
}

[TestFixture]
public class CmsPageRendererTests
{
	[Test]
	public void Constructor_NullAccessor_Throws()
		=> Assert.That(() => new CmsPageRenderer(null!), Throws.ArgumentNullException);

	[Test]
	public void RenderPage_NoHttpContext_PassesNullSubRoute()
	{
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns((HttpContext?)null);
		var renderer = new CmsPageRenderer(accessor);
		var page = new PageDTO { ContentMeta = new ContentDTO { Title = "Home" } };
		var config = new WebWayCMS.Controllers.GenericPageConfiguration();

		var result = (ResultActionResult)renderer.RenderPage(page, config);
		var component = (RazorComponentResult<CmsPageHost>)result.Result;

		Assert.Multiple(() =>
		{
			Assert.That(component.Parameters!["Page"], Is.SameAs(page));
			Assert.That(component.Parameters!["Config"], Is.SameAs(config));
			Assert.That(component.Parameters!["SubRoute"], Is.Null);
		});
	}

	[Test]
	public void RenderPage_WithSubRoute_ForwardsIt()
	{
		var http = new DefaultHttpContext();
		http.Items["CMS:SubRoute"] = "my-slug";
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(http);
		var renderer = new CmsPageRenderer(accessor);

		var result = (ResultActionResult)renderer.RenderPage(null, new WebWayCMS.Controllers.GenericPageConfiguration());
		var component = (RazorComponentResult<CmsPageHost>)result.Result;

		Assert.That(component.Parameters!["SubRoute"], Is.EqualTo("my-slug"));
	}
}

[TestFixture]
public class ContentZoneResolverTests
{
	private IContentZoneModel _model = null!;
	private ContentZoneResolver _resolver = null!;

	[SetUp]
	public void SetUp()
	{
		_model = Substitute.For<IContentZoneModel>();
		_resolver = new ContentZoneResolver(_model);
	}

	[Test]
	public void Constructor_NullModel_Throws()
		=> Assert.That(() => new ContentZoneResolver(null!), Throws.ArgumentNullException);

	[Test]
	public async Task ByZoneId_Found_MapsRawNameFromVm()
	{
		var id = Guid.NewGuid();
		_model.GetViewModelByIdAsync(id, Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = id, Name = "Zone-1" });

		var vm = await _resolver.ResolveAsync(zoneName: null, isGlobal: false, zoneId: id, page: null, parentZoneId: null);

		Assert.Multiple(() =>
		{
			Assert.That(vm.RawZoneName, Is.EqualTo("Zone-1"));
			Assert.That(vm.ParentPageMasterId, Is.Null);
		});
	}

	[Test]
	public async Task ByZoneId_NotFound_ReturnsEmpty()
	{
		_model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel?)null);

		var vm = await _resolver.ResolveAsync(null, false, Guid.NewGuid(), null, null);

		Assert.Multiple(() =>
		{
			Assert.That(vm.ZoneObjects, Is.Empty);
			Assert.That(vm.Name, Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public async Task WhitespaceZoneName_ReturnsEmpty()
	{
		var vm = await _resolver.ResolveAsync("   ", false, null, null, null);
		Assert.That(vm.ZoneObjects, Is.Empty);
	}

	[Test]
	public async Task PageSlot_TopLevel_UsesPageMaster()
	{
		var master = Guid.NewGuid();
		var page = new PageDTO { ContentMeta = new ContentDTO { MasterId = master } };
		_model.GetOrCreateViewModelByPageSlotAsync(master, "Main", Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.NewGuid(), Name = "Main" });

		var vm = await _resolver.ResolveAsync("Main", false, null, page, parentZoneId: null);

		Assert.Multiple(() =>
		{
			Assert.That(vm.RawZoneName, Is.EqualTo("Main"));
			Assert.That(vm.ParentPageMasterId, Is.EqualTo(master));
		});
	}

	[Test]
	public async Task PageSlot_Nested_UsesParentZone()
	{
		var parent = Guid.NewGuid();
		var page = new PageDTO { ContentMeta = new ContentDTO { MasterId = Guid.NewGuid() } };
		_model.GetOrCreateViewModelByZoneSlotAsync(parent, "Side", Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.NewGuid(), Name = "Side" });

		var vm = await _resolver.ResolveAsync("Side", false, null, page, parentZoneId: parent);

		await _model.Received(1).GetOrCreateViewModelByZoneSlotAsync(parent, "Side", Arg.Any<CancellationToken>());
		Assert.That(vm.RawZoneName, Is.EqualTo("Side"));
	}

	[Test]
	public async Task Global_WhenIsGlobalFlag_UsesNameLookup()
	{
		_model.GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.NewGuid(), Name = "Footer" });

		var vm = await _resolver.ResolveAsync("Footer", isGlobal: true, null, page: new PageDTO(), parentZoneId: null);

		await _model.Received(1).GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>());
		Assert.That(vm.RawZoneName, Is.EqualTo("Footer"));
	}

	[Test]
	public async Task Global_WhenNoPage_UsesNameLookup()
	{
		_model.GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.NewGuid(), Name = "Footer" });

		var vm = await _resolver.ResolveAsync("Footer", isGlobal: false, null, page: null, parentZoneId: null);

		await _model.Received(1).GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>());
		Assert.That(vm.ParentPageMasterId, Is.Null);
	}
}
