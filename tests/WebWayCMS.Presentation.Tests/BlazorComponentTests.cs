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
				s.AddSingleton(Substitute.For<IContentBlockModel>());
			});

		Assert.That(html.Trim(), Is.Empty);
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

		return s =>
		{
			s.AddSingleton(resolver);
			s.AddSingleton<IContentZoneWidgetRegistry>(new ContentZoneWidgetRegistry(new Dictionary<string, Type>()));
			s.AddSingleton(Substitute.For<IContentBlockModel>());
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
}
