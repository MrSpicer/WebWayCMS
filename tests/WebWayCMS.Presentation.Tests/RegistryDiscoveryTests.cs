using System.Reflection;

using Microsoft.AspNetCore.Components;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

/// <summary>
/// Covers the convention-scan extension-point registries: chrome, page views, content-zone views, and
/// the (now scan-built) widget registry. Fixtures are nested component types in this test assembly.
/// </summary>
[TestFixture]
public class RegistryDiscoveryTests
{
	private static Assembly TestAssembly => typeof(RegistryDiscoveryTests).Assembly;

	// --- Fixtures -----------------------------------------------------------------------------

	private abstract class TestComponentBase : IComponent
	{
		public void Attach(RenderHandle renderHandle) { }
		public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
	}

	// Chrome: same Order -> FullName tiebreak picks AlphaChrome over BetaChrome; HighChrome loses on Order.
	[CmsChrome(Order = 1)] private sealed class AlphaChrome : TestComponentBase { }
	[CmsChrome(Order = 1)] private sealed class BetaChrome : TestComponentBase { }
	[CmsChrome(Order = 5)] private sealed class HighChrome : TestComponentBase { }
	[CmsChrome] private sealed class NotAComponentChrome { } // not IComponent -> excluded by the scanner

	// Page views.
	[CmsPageView("Generic", "Wide")] private sealed class GenericWideView : TestComponentBase { }
	[CmsPageView(ForController = "Generic", Name = "Narrow")] private sealed class GenericNarrowView : TestComponentBase { }
	[CmsPageView(ForController = "", Name = "Blank")] private sealed class BlankControllerView : TestComponentBase { }
	[CmsPageView(ForController = "Foo", Name = "")] private sealed class BlankNamePageView : TestComponentBase { }

	// Content-zone views.
	[ContentZoneView("ContentBlock", "Card")] private sealed class ContentBlockCardView : TestComponentBase { }

	// Widgets discovered by the scan-built widget registry.
	[ContentZoneComponent(DisplayName = "Sample")] private sealed class SampleScanWidget : TestComponentBase { }
	[ContentZoneComponent(Name = "ExplicitName")] private sealed class WhateverWidget : TestComponentBase { }

	// --- ComponentTypeScanner (exercised through the registries) -----------------------------

	[Test]
	public void Scanner_NullAssemblies_Throws()
		=> Assert.That(() => new CmsChromeRegistry(null!), Throws.ArgumentNullException);

	[Test]
	public void Scanner_SkipsFailingAssembly()
	{
		var bad = Substitute.For<Assembly>();
		bad.GetTypes().Returns(_ => throw new InvalidOperationException("boom"));

		// Despite the failing assembly, the chrome from the good assembly is still discovered.
		var registry = new CmsChromeRegistry(new[] { bad, TestAssembly });

		Assert.That(registry.ChromeType, Is.EqualTo(typeof(AlphaChrome)));
	}

	// --- CmsChromeRegistry -------------------------------------------------------------------

	[Test]
	public void Chrome_PicksLowestOrderThenFullName()
	{
		// NotAComponentChrome has the default Order 0 (lower than AlphaChrome); AlphaChrome only wins
		// because non-component types are excluded by the scanner.
		var registry = new CmsChromeRegistry(new[] { TestAssembly });

		Assert.That(registry.ChromeType, Is.EqualTo(typeof(AlphaChrome)));
	}

	[Test]
	public void Chrome_NoChrome_IsNull()
	{
		var registry = new CmsChromeRegistry(new[] { typeof(string).Assembly });

		Assert.That(registry.ChromeType, Is.Null);
	}

	// --- CmsPageViewRegistry -----------------------------------------------------------------

	[Test]
	public void PageViews_RegistersByControllerAndName_SkippingBlanks()
	{
		var registry = new CmsPageViewRegistry(new[] { TestAssembly });

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetControllerViews("Generic"), Is.EqualTo(new[] { "Narrow", "Wide" }));
			Assert.That(registry.Resolve("Generic", "Wide"), Is.EqualTo(typeof(GenericWideView)));
			Assert.That(registry.Resolve("generic", "wide"), Is.EqualTo(typeof(GenericWideView)), "case-insensitive");
			// Blank controller and blank name fixtures are skipped.
			Assert.That(registry.GetControllerViews("Foo"), Is.Empty);
			Assert.That(registry.GetControllerViews(string.Empty), Is.Empty);
			Assert.That(registry.GetControllerViews("Unknown"), Is.Empty);
		});
	}

	[Test]
	public void PageViews_Resolve_NullAndUnknownCases()
	{
		var registry = new CmsPageViewRegistry(new[] { TestAssembly });

		Assert.Multiple(() =>
		{
			Assert.That(registry.Resolve(string.Empty, "Wide"), Is.Null);
			Assert.That(registry.Resolve("Generic", string.Empty), Is.Null);
			Assert.That(registry.Resolve("Unknown", "Wide"), Is.Null);
			Assert.That(registry.Resolve("Generic", "Missing"), Is.Null);
		});
	}

	// --- ContentZoneViewRegistry -------------------------------------------------------------

	[Test]
	public void ZoneViews_RegistersByComponentAndName()
	{
		var registry = new ContentZoneViewRegistry(new[] { TestAssembly });

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetComponentViews("ContentBlock"), Is.EqualTo(new[] { "Card" }));
			Assert.That(registry.Resolve("ContentBlock", "Card"), Is.EqualTo(typeof(ContentBlockCardView)));
			Assert.That(registry.Resolve("ContentBlock", "Missing"), Is.Null);
			Assert.That(registry.GetComponentViews("Unknown"), Is.Empty);
		});
	}

	// --- ContentZoneWidgetRegistry.FromAssemblies --------------------------------------------

	[Test]
	public void WidgetRegistry_FromAssemblies_ScansBuiltInAndDecoratedWidgets()
	{
		var registry = ContentZoneWidgetRegistry.FromAssemblies(new[] { typeof(ContentBlockWidget).Assembly, TestAssembly });

		Assert.Multiple(() =>
		{
			// Built-in widgets (Presentation assembly), names match the former hard-coded map.
			Assert.That(registry.Resolve("ContentBlock"), Is.EqualTo(typeof(ContentBlockWidget)));
			Assert.That(registry.Resolve("Page"), Is.EqualTo(typeof(PageNavigationWidget)));
			// Scanned test-assembly widgets: suffix-stripped and explicit names.
			Assert.That(registry.Resolve("SampleScan"), Is.EqualTo(typeof(SampleScanWidget)));
			Assert.That(registry.Resolve("ExplicitName"), Is.EqualTo(typeof(WhateverWidget)));
			Assert.That(registry.Resolve("Nope"), Is.Null);
		});
	}
}
