using System.Reflection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class PageControllerRegistryTests
{
	private static PageControllerRegistry CreateRegistry()
		=> new(new[] { typeof(PageControllerRegistryTests).Assembly });

	[Test]
	public void Scan_RegistersOnlyDecoratedControllers()
	{
		var names = CreateRegistry().GetAllControllers().Select(c => c.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("Configured"));
			Assert.That(names, Does.Contain("Alpha"));
			Assert.That(names, Does.Contain("Banner"));
			Assert.That(names, Does.Not.Contain("Ignored"));
		});
	}

	[Test]
	public void BuildControllerInfo_DisplayNameFallbackAndOverride()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Alpha")!.DisplayName, Is.EqualTo("Alpha"));
			Assert.That(registry.GetByName("Configured")!.DisplayName, Is.EqualTo("Custom Display"));
		});
	}

	[Test]
	public void BuildControllerInfo_PropertiesAndHasConfiguration()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Configured")!.Properties, Is.Not.Empty);
			Assert.That(registry.GetByName("Configured")!.HasConfiguration, Is.True);
			Assert.That(registry.GetByName("Banner")!.Properties, Is.Empty);
			Assert.That(registry.GetByName("Banner")!.HasConfiguration, Is.False);
		});
	}

	[Test]
	public void Scan_SortsByCategoryThenOrderThenName()
	{
		var all = CreateRegistry().GetAllControllers();
		var contentNames = all.Where(c => c.Category == "Content").Select(c => c.Name).ToList();
		var categories = all.Select(c => c.Category).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(contentNames.IndexOf("Alpha"), Is.LessThan(contentNames.IndexOf("Configured")));
			Assert.That(categories.IndexOf("Content"), Is.LessThan(categories.IndexOf("Layout")));
		});
	}

	[Test]
	public void Scan_FailingAssemblyIsSkipped()
	{
		var bad = Substitute.For<Assembly>();
		bad.GetTypes().Returns(_ => throw new InvalidOperationException("boom"));

		var registry = new PageControllerRegistry(new[] { bad, typeof(PageControllerRegistryTests).Assembly });

		Assert.That(registry.GetByName("Banner"), Is.Not.Null);
	}

	[Test]
	public void DefaultConstructor_DoesNotThrow()
	{
		Assert.That(new PageControllerRegistry().GetAllControllers(), Is.Not.Null);
	}

	[Test]
	public void GetByName_NullOrUnknown_ReturnsNull()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName(string.Empty), Is.Null);
			Assert.That(registry.GetByName("Missing"), Is.Null);
		});
	}

	[Test]
	public void GetCategories_SortedDistinct()
	{
		Assert.That(CreateRegistry().GetCategories(), Is.EqualTo(new[] { "Content", "Layout" }));
	}

	[Test]
	public void GetByCategory_EmptyUnknownAndKnown()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByCategory(string.Empty), Is.Empty);
			Assert.That(registry.GetByCategory("Nope"), Is.Empty);
			Assert.That(registry.GetByCategory("Layout").Select(c => c.Name), Is.EqualTo(new[] { "Banner" }));
		});
	}

	[Test]
	public void CreateDefaultConfiguration_Variants()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.CreateDefaultConfiguration("Banner"), Is.Null);
			Assert.That(registry.CreateDefaultConfiguration("Missing"), Is.Null);
			Assert.That(registry.CreateDefaultConfiguration("Configured"), Is.InstanceOf<SamplePageConfig>());
			Assert.That(registry.CreateDefaultConfiguration("BrokenConfig"), Is.Null);
		});
	}

	[Test]
	public void ValidateConfiguration_UnknownAndNoConfig()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.ValidateConfiguration("Missing", new object()), Has.Some.Contains("Unknown controller"));
			Assert.That(registry.ValidateConfiguration("Banner", new object()), Is.Empty);
		});
	}

	[Test]
	public void ValidateConfiguration_ValidObjectAndJson()
	{
		var registry = CreateRegistry();
		var json = System.Text.Json.JsonSerializer.Serialize(new SamplePageConfig());

		Assert.Multiple(() =>
		{
			Assert.That(registry.ValidateConfiguration("Configured", new SamplePageConfig()), Is.Empty);
			Assert.That(registry.ValidateConfiguration("Configured", json), Is.Empty);
		});
	}

	[Test]
	public void ValidateConfiguration_InvalidObject_ReturnsErrors()
	{
		var registry = CreateRegistry();
		var config = new SamplePageConfig
		{
			Title = "  ",
			Ref = Guid.Empty,
			PageSize = 20,
			Code = "toolong",
			Digits = "abc",
			Letters = "123"
		};

		var errors = registry.ValidateConfiguration("Configured", config);

		Assert.Multiple(() =>
		{
			Assert.That(errors, Has.Some.Contains("is required"));
			Assert.That(errors, Has.Some.Contains("at most"));
			Assert.That(errors, Has.Some.Contains("not exceed"));
			Assert.That(errors, Has.Some.Contains("digits only"));
			Assert.That(errors, Has.Some.Contains("invalid format"));
		});
	}

	[Test]
	public void ValidateConfiguration_BelowMinimum()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Configured", new SamplePageConfig { PageSize = 0 }),
			Has.Some.Contains("at least"));
	}

	[Test]
	public void ValidateConfiguration_InvalidAndNullJson()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.ValidateConfiguration("Configured", "{ not json"), Has.Some.Contains("Invalid JSON"));
			Assert.That(registry.ValidateConfiguration("Configured", "null"), Has.Some.Contains("Configuration is required"));
		});
	}

	[Test]
	public void ValidateConfiguration_PropertyMissingFromType_IsSkipped()
	{
		var registry = CreateRegistry();
		registry.GetByName("Configured")!.Properties.Add(new FormPropertyInfo { Name = "Nonexistent", IsRequired = true });

		Assert.That(registry.ValidateConfiguration("Configured", new SamplePageConfig()), Is.Empty);
	}
}
