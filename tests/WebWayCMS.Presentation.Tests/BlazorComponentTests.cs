using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Presentation.Components;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentBlockWidgetTests
{
	private static IContentBlockModel ModelReturning(Guid id, string? content)
	{
		var model = Substitute.For<IContentBlockModel>();
		model.GetViewModelByMasterIdAsync(id, Arg.Any<CancellationToken>())
			.Returns(content is null ? null : new ContentBlockViewModel { Content = content });
		return model;
	}

	[Test]
	public async Task NullConfig_RendersNothing()
	{
		var html = await BlazorRenderHarness.RenderAsync<ContentBlockWidget>(
			configureServices: s => s.AddSingleton(Substitute.For<IContentBlockModel>()));
		Assert.That(html.Trim(), Is.Empty);
	}

	[Test]
	public async Task EmptyContentBlockId_RendersNothing()
	{
		var html = await BlazorRenderHarness.RenderAsync<ContentBlockWidget>(
			new Dictionary<string, object?> { ["Config"] = new ContentBlockContentZoneConfiguration { ContentBlockID = Guid.Empty } },
			s => s.AddSingleton(Substitute.For<IContentBlockModel>()));
		Assert.That(html.Trim(), Is.Empty);
	}

	[Test]
	public async Task ValidBlock_RendersRawContent()
	{
		var id = Guid.NewGuid();
		var html = await BlazorRenderHarness.RenderAsync<ContentBlockWidget>(
			new Dictionary<string, object?> { ["Config"] = new ContentBlockContentZoneConfiguration { ContentBlockID = id } },
			s => s.AddSingleton(ModelReturning(id, "<p>hello block</p>")));
		Assert.That(html, Does.Contain("<p>hello block</p>"));
	}

	[Test]
	public async Task ValidBlock_ModelReturnsNull_RendersNothing()
	{
		var id = Guid.NewGuid();
		var html = await BlazorRenderHarness.RenderAsync<ContentBlockWidget>(
			new Dictionary<string, object?> { ["Config"] = new ContentBlockContentZoneConfiguration { ContentBlockID = id } },
			s => s.AddSingleton(ModelReturning(id, null)));
		Assert.That(html.Trim(), Is.Empty);
	}
}

[TestFixture]
public class ContentZoneComponentTests
{
	private static IContentZoneWidgetRegistry RealRegistry() =>
		new ContentZoneWidgetRegistry(new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
		{
			["ContentBlock"] = typeof(ContentBlockWidget),
		});

	private static IContentZoneViewRegistry NoViews()
	{
		var v = Substitute.For<IContentZoneViewRegistry>();
		v.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns((Type?)null);
		return v;
	}

	private sealed class MarkerZoneView : Microsoft.AspNetCore.Components.ComponentBase
	{
		[Microsoft.AspNetCore.Components.Parameter] public object? Config { get; set; }

		protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
			=> builder.AddMarkupContent(0, "<span class=\"zone-view\">VIEW</span>");
	}

	[Test]
	public async Task WithItems_RendersMappedWidget_AndSkipsUnmapped()
	{
		var blockId = Guid.NewGuid();
		var resolver = Substitute.For<IContentZoneResolver>();
		resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<PageDTO?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel
			{
				Id = Guid.NewGuid(),
				Name = "Main",
				ZoneObjects = new List<ContentZoneObject>
				{
					new() { Ordinal = 1, ComponentName = "ContentBlock", ComponentProperties = new ContentBlockContentZoneConfiguration { ContentBlockID = blockId } },
					new() { Ordinal = 0, ComponentName = "Article", ComponentProperties = new object() },
				},
			});

		var model = Substitute.For<IContentBlockModel>();
		model.GetViewModelByMasterIdAsync(blockId, Arg.Any<CancellationToken>())
			.Returns(new ContentBlockViewModel { Content = "<p>zone block</p>" });

		var html = await BlazorRenderHarness.RenderAsync<ContentZone>(
			new Dictionary<string, object?> { ["ZoneName"] = "Main" },
			s =>
			{
				s.AddSingleton(resolver);
				s.AddSingleton(RealRegistry());
				s.AddSingleton(NoViews());
				s.AddSingleton(model);
			});

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("data-zone-name=\"Main\""));
			Assert.That(html, Does.Contain("<p>zone block</p>"));
			Assert.That(html, Does.Contain("data-ordinal=\"0\""));
		});
	}

	[Test]
	public async Task NoItems_RendersNothing()
	{
		var resolver = Substitute.For<IContentZoneResolver>();
		resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<PageDTO?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.Empty, ZoneObjects = new List<ContentZoneObject>() });

		var html = await BlazorRenderHarness.RenderAsync<ContentZone>(
			new Dictionary<string, object?> { ["ZoneName"] = "Main" },
			s =>
			{
				s.AddSingleton(resolver);
				s.AddSingleton(RealRegistry());
				s.AddSingleton(NoViews());
				s.AddSingleton(Substitute.For<IContentBlockModel>());
			});

		Assert.That(html.Trim(), Is.Empty);
	}

	private static IContentZoneResolver ResolverWith(ContentZoneObject item)
	{
		var resolver = Substitute.For<IContentZoneResolver>();
		resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<PageDTO?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.NewGuid(), Name = "Main", ZoneObjects = new List<ContentZoneObject> { item } });
		return resolver;
	}

	[Test]
	public async Task ItemWithViewName_RendersHostViewInsteadOfWidget()
	{
		var item = new ContentZoneObject { Ordinal = 0, ComponentName = "ContentBlock", ViewName = "Card", ComponentProperties = new object() };
		var views = Substitute.For<IContentZoneViewRegistry>();
		views.Resolve("ContentBlock", "Card").Returns(typeof(MarkerZoneView));

		var html = await BlazorRenderHarness.RenderAsync<ContentZone>(
			new Dictionary<string, object?> { ["ZoneName"] = "Main" },
			s =>
			{
				s.AddSingleton(ResolverWith(item));
				s.AddSingleton(RealRegistry());
				s.AddSingleton(views);
				s.AddSingleton(Substitute.For<IContentBlockModel>());
			});

		Assert.That(html, Does.Contain("class=\"zone-view\""));
	}

	[Test]
	public async Task ItemWithUnregisteredViewName_FallsBackToWidget()
	{
		var blockId = Guid.NewGuid();
		var item = new ContentZoneObject
		{
			Ordinal = 0,
			ComponentName = "ContentBlock",
			ViewName = "Missing",
			ComponentProperties = new ContentBlockContentZoneConfiguration { ContentBlockID = blockId },
		};
		var views = Substitute.For<IContentZoneViewRegistry>();
		views.Resolve("ContentBlock", "Missing").Returns((Type?)null);

		var model = Substitute.For<IContentBlockModel>();
		model.GetViewModelByMasterIdAsync(blockId, Arg.Any<CancellationToken>())
			.Returns(new ContentBlockViewModel { Content = "<p>fallback block</p>" });

		var html = await BlazorRenderHarness.RenderAsync<ContentZone>(
			new Dictionary<string, object?> { ["ZoneName"] = "Main" },
			s =>
			{
				s.AddSingleton(ResolverWith(item));
				s.AddSingleton(RealRegistry());
				s.AddSingleton(views);
				s.AddSingleton(model);
			});

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("<p>fallback block</p>"));
			Assert.That(html, Does.Not.Contain("zone-view"));
		});
	}
}

[TestFixture]
public class CmsPageHostTests
{
	private static Action<IServiceCollection> WithEmptyZone()
	{
		var resolver = Substitute.For<IContentZoneResolver>();
		resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<PageDTO?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
			.Returns(new ContentZoneViewModel { Id = Guid.Empty, ZoneObjects = new List<ContentZoneObject>() });

		var chrome = Substitute.For<ICmsChromeRegistry>();
		chrome.ChromeType.Returns((Type?)null);

		var pageViews = Substitute.For<ICmsPageViewRegistry>();
		pageViews.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns((Type?)null);

		var zoneViews = Substitute.For<IContentZoneViewRegistry>();
		zoneViews.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns((Type?)null);

		return s =>
		{
			s.AddSingleton(resolver);
			s.AddSingleton<IContentZoneWidgetRegistry>(new ContentZoneWidgetRegistry(new Dictionary<string, Type>()));
			s.AddSingleton(Substitute.For<IContentBlockModel>());
			s.AddSingleton(chrome);
			s.AddSingleton(pageViews);
			s.AddSingleton(zoneViews);
		};
	}

	[Test]
	public async Task GenericConfig_EmitsDocumentWithTitleMetaStyleScript()
	{
		var page = new PageDTO { ContentMeta = new ContentDTO { Title = "Home" } };
		var config = new WebWayCMS.Controllers.GenericPageConfiguration
		{
			Meta = "<meta name=\"description\" content=\"hi\">",
			Style = ".a{color:red}",
			Script = "console.log('x')",
		};

		var html = await BlazorRenderHarness.RenderAsync<CmsPageHost>(
			new Dictionary<string, object?> { ["Page"] = page, ["Config"] = config, ["SubRoute"] = null },
			WithEmptyZone());

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("<title>Home - WebWayCMS</title>"));
			Assert.That(html, Does.Contain("name=\"description\""));
			Assert.That(html, Does.Contain("<style>.a{color:red}</style>"));
			Assert.That(html, Does.Contain("console.log('x')"));
			Assert.That(html, Does.Contain("_framework/blazor.web.js"));
		});
	}

	[Test]
	public async Task NonGenericConfig_EmitsFallbackTitle_NoStyleOrScript()
	{
		var html = await BlazorRenderHarness.RenderAsync<CmsPageHost>(
			new Dictionary<string, object?> { ["Page"] = null, ["Config"] = new object(), ["SubRoute"] = null },
			WithEmptyZone());

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("<title>Page - WebWayCMS</title>"));
			Assert.That(html, Does.Not.Contain("<style>"));
		});
	}

	private sealed class MarkerPageView : Microsoft.AspNetCore.Components.ComponentBase
	{
		protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
			=> builder.AddMarkupContent(0, "<div id=\"host-page-view\">VIEW</div>");
	}

	[Test]
	public async Task PageWithRegisteredView_RendersViewAsBody()
	{
		var page = new PageDTO
		{
			ContentMeta = new ContentDTO { Title = "Home" },
			ControllerName = "Generic",
			ViewName = "Wide",
		};

		var configure = WithEmptyZone();
		var html = await BlazorRenderHarness.RenderAsync<CmsPageHost>(
			new Dictionary<string, object?> { ["Page"] = page, ["Config"] = new WebWayCMS.Controllers.GenericPageConfiguration(), ["SubRoute"] = null },
			s =>
			{
				configure(s);
				// Last registration wins: a page-view registry that resolves the host view component.
				var pageViews = Substitute.For<ICmsPageViewRegistry>();
				pageViews.Resolve("Generic", "Wide").Returns(typeof(MarkerPageView));
				s.AddSingleton(pageViews);
			});

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("id=\"host-page-view\""));
			Assert.That(html, Does.Not.Contain("class=\"content-zone\""));
		});
	}
}

[TestFixture]
public class CmsLayoutChromeTests
{
	private sealed class MarkerChrome : Microsoft.AspNetCore.Components.ComponentBase
	{
		[Microsoft.AspNetCore.Components.Parameter] public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }

		protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
		{
			builder.OpenElement(0, "header");
			builder.AddContent(1, "CHROME-HEADER");
			builder.CloseElement();
			builder.OpenElement(2, "main");
			builder.AddAttribute(3, "class", "host-chrome");
			builder.AddContent(4, ChildContent);
			builder.CloseElement();
		}
	}

	private static Microsoft.AspNetCore.Components.RenderFragment Body =>
		b => b.AddMarkupContent(0, "<p>BODY</p>");

	private static Action<IServiceCollection> WithChrome(Type? chromeType) => s =>
	{
		var chrome = Substitute.For<ICmsChromeRegistry>();
		chrome.ChromeType.Returns(chromeType);
		s.AddSingleton(chrome);
	};

	[Test]
	public async Task NoChrome_RendersDefaultMainAroundBody()
	{
		var html = await BlazorRenderHarness.RenderAsync<CmsLayout>(
			new Dictionary<string, object?> { ["Title"] = "T", ["ChildContent"] = Body },
			WithChrome(null));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("<main role=\"main\">"));
			Assert.That(html, Does.Contain("<p>BODY</p>"));
			Assert.That(html, Does.Not.Contain("CHROME-HEADER"));
		});
	}

	[Test]
	public async Task WithChrome_WrapsBodyInHostChrome_NoDefaultMain()
	{
		var html = await BlazorRenderHarness.RenderAsync<CmsLayout>(
			new Dictionary<string, object?> { ["Title"] = "T", ["ChildContent"] = Body },
			WithChrome(typeof(MarkerChrome)));

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("CHROME-HEADER"));
			Assert.That(html, Does.Contain("class=\"host-chrome\""));
			Assert.That(html, Does.Contain("<p>BODY</p>"));
			Assert.That(html, Does.Not.Contain("role=\"main\""));
		});
	}
}
