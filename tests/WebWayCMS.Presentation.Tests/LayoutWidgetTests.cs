using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Layout;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class LayoutWidgetTests
{
	/// <summary>Registers an empty-zone resolver so the nested ContentZone components render their structure only.</summary>
	internal static Action<IServiceCollection> EmptyZones()
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

	[TestCase("Default", "<section class=\"section\">")]
	[TestCase("SingleColumn", "<section class=\"section\">")]
	[TestCase("CenteredNarrow", "is-8 is-offset-2")]
	[TestCase("TwoColumnEqual", "is-6")]
	[TestCase("TwoColumnSidebar", "is-8")]
	[TestCase("OneThirdTwoThird", "is-one-third")]
	[TestCase("ThreeColumn", "is-4")]
	[TestCase("FourColumn", "is-3")]
	[TestCase("HeaderContentFooter", "<section class=\"section\">")]
	[TestCase("HeroWithColumns", "columns")]
	[TestCase("AsymmetricRightHeavy", "is-8")]
	public async Task RendersSelectedLayout(string viewName, string expected)
	{
		var html = await BlazorRenderHarness.RenderAsync<LayoutWidget>(
			new Dictionary<string, object?> { ["Config"] = new LayoutContentZoneConfiguration { ViewName = viewName } },
			EmptyZones());

		Assert.That(html, Does.Contain(expected));
	}

	[Test]
	public async Task NullConfig_FallsBackToDefault()
	{
		var html = await BlazorRenderHarness.RenderAsync<LayoutWidget>(configureServices: EmptyZones());
		Assert.That(html, Does.Contain("<section class=\"section\">"));
	}

	[Test]
	public async Task UnknownName_FallsBackToDefault()
	{
		var html = await BlazorRenderHarness.RenderAsync<LayoutWidget>(
			new Dictionary<string, object?> { ["Config"] = new LayoutContentZoneConfiguration { ViewName = "DoesNotExist" } },
			EmptyZones());

		Assert.That(html, Does.Contain("<section class=\"section\">"));
	}
}
