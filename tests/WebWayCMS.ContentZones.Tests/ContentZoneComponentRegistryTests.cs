using System.Reflection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Forms;

namespace WebWayCMS.ContentZones.Tests;

[TestFixture]
public class ContentZoneComponentRegistryTests
{
	private static ContentZoneComponentRegistry CreateRegistry()
		=> new(new[] { typeof(ContentZoneComponentRegistryTests).Assembly });

	[Test]
	public void Scan_RegistersOnlyDecoratedViewComponents()
	{
		var registry = CreateRegistry();

		var names = registry.GetAllComponents().Select(c => c.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("Configured"));
			Assert.That(names, Does.Contain("Alpha"));
			Assert.That(names, Does.Contain("Banner"));
			Assert.That(names, Does.Not.Contain("Ignored"));
		});
	}

	[Test]
	public void Scan_GetComponentName_StripsViewComponentSuffixOnly()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Configured"), Is.Not.Null);
			Assert.That(registry.GetByName("Banner"), Is.Not.Null, "name without suffix is kept as-is");
		});
	}

	[Test]
	public void BuildComponentInfo_DefaultDisplayNameFallsBackToSpacedName()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Alpha")!.DisplayName, Is.EqualTo("Alpha"));
			Assert.That(registry.GetByName("Configured")!.DisplayName, Is.EqualTo("Custom Display"));
		});
	}

	[Test]
	public void BuildComponentInfo_PopulatesPropertiesWhenConfigurationTypePresent()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Configured")!.Properties, Is.Not.Empty);
			Assert.That(registry.GetByName("Banner")!.Properties, Is.Empty);
		});
	}

	[Test]
	public void ComponentInfo_HasConfiguration_ReflectsConfigTypeAndProperties()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName("Configured")!.HasConfiguration, Is.True);
			Assert.That(registry.GetByName("Banner")!.HasConfiguration, Is.False);
		});
	}

	[Test]
	public void Scan_SortsComponentsByCategoryThenOrderThenName()
	{
		var registry = CreateRegistry();
		var all = registry.GetAllComponents();

		var contentNames = all.Where(c => c.Category == "Content").Select(c => c.Name).ToList();

		// Alpha (Order 1) before Configured (Order 2) within the Content category.
		Assert.That(contentNames.IndexOf("Alpha"), Is.LessThan(contentNames.IndexOf("Configured")));
		// Content category sorts before Layout in the main list.
		var categoriesInOrder = all.Select(c => c.Category).ToList();
		Assert.That(categoriesInOrder.IndexOf("Content"), Is.LessThan(categoriesInOrder.IndexOf("Layout")));
	}

	[Test]
	public void Scan_FailingAssemblyIsSkipped()
	{
		var bad = Substitute.For<Assembly>();
		bad.GetTypes().Returns(_ => throw new InvalidOperationException("boom"));

		// Should not throw despite the failing assembly.
		var registry = new ContentZoneComponentRegistry(new[] { bad, typeof(ContentZoneComponentRegistryTests).Assembly });

		Assert.That(registry.GetByName("Banner"), Is.Not.Null);
	}

	[Test]
	public void DefaultConstructor_DoesNotThrow()
	{
		var registry = new ContentZoneComponentRegistry();

		Assert.That(registry.GetAllComponents(), Is.Not.Null);
	}

	[Test]
	public void GetByName_NullOrEmpty_ReturnsNull()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByName(string.Empty), Is.Null);
			Assert.That(registry.GetByName("Missing"), Is.Null);
		});
	}

	[Test]
	public void GetCategories_ReturnsSortedDistinctCategories()
	{
		var registry = CreateRegistry();

		Assert.That(registry.GetCategories(), Is.EqualTo(new[] { "Content", "Layout" }));
	}

	[Test]
	public void GetByCategory_EmptyOrUnknown_ReturnsEmpty()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetByCategory(string.Empty), Is.Empty);
			Assert.That(registry.GetByCategory("Nope"), Is.Empty);
		});
	}

	[Test]
	public void GetByCategory_Known_ReturnsComponents()
	{
		var registry = CreateRegistry();

		Assert.That(registry.GetByCategory("Layout").Select(c => c.Name), Is.EqualTo(new[] { "Banner" }));
	}

	[Test]
	public void GetComponentsByCategory_GroupsByCategory()
	{
		var registry = CreateRegistry();

		var grouped = registry.GetComponentsByCategory();

		Assert.Multiple(() =>
		{
			Assert.That(grouped.Keys, Does.Contain("Content"));
			Assert.That(grouped.Keys, Does.Contain("Layout"));
			Assert.That(grouped["Layout"].Select(c => c.Name), Is.EqualTo(new[] { "Banner" }));
		});
	}

	[Test]
	public void CreateDefaultConfiguration_NoConfigType_ReturnsNull()
	{
		var registry = CreateRegistry();

		Assert.Multiple(() =>
		{
			Assert.That(registry.CreateDefaultConfiguration("Banner"), Is.Null);
			Assert.That(registry.CreateDefaultConfiguration("Missing"), Is.Null);
		});
	}

	[Test]
	public void CreateDefaultConfiguration_ValidConfigType_ReturnsInstance()
	{
		var registry = CreateRegistry();

		Assert.That(registry.CreateDefaultConfiguration("Configured"), Is.InstanceOf<SampleConfig>());
	}

	[Test]
	public void CreateDefaultConfiguration_ConfigTypeWithoutDefaultCtor_ReturnsNull()
	{
		var registry = CreateRegistry();

		Assert.That(registry.CreateDefaultConfiguration("BrokenConfig"), Is.Null);
	}

	// --- ValidateConfiguration ---

	[Test]
	public void ValidateConfiguration_UnknownComponent_ReturnsError()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Missing", new object()), Has.Some.Contains("Unknown component"));
	}

	[Test]
	public void ValidateConfiguration_NoConfigType_ReturnsNoErrors()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Banner", new object()), Is.Empty);
	}

	[Test]
	public void ValidateConfiguration_ValidObject_ReturnsNoErrors()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Configured", new SampleConfig()), Is.Empty);
	}

	[Test]
	public void ValidateConfiguration_InvalidObject_ReturnsAllErrors()
	{
		var registry = CreateRegistry();
		var config = new SampleConfig
		{
			Name = "  ",
			Ref = Guid.Empty,
			RequiredNullable = null,
			Count = 20,         // above max
			Code = "toolong",   // exceeds MaxLength
			Digits = "abc",     // pattern (custom message)
			Letters = "123"     // pattern (default message)
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
	public void ValidateConfiguration_BelowMinimum_ReturnsError()
	{
		var registry = CreateRegistry();
		var config = new SampleConfig { Count = 0 };

		Assert.That(registry.ValidateConfiguration("Configured", config), Has.Some.Contains("at least"));
	}

	[Test]
	public void ValidateConfiguration_ValidJsonString_ReturnsNoErrors()
	{
		var registry = CreateRegistry();
		var json = System.Text.Json.JsonSerializer.Serialize(new SampleConfig());

		Assert.That(registry.ValidateConfiguration("Configured", json), Is.Empty);
	}

	[Test]
	public void ValidateConfiguration_InvalidJsonString_ReturnsError()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Configured", "{ not json"), Has.Some.Contains("Invalid JSON"));
	}

	[Test]
	public void ValidateConfiguration_NullJsonString_ReturnsRequiredError()
	{
		var registry = CreateRegistry();

		Assert.That(registry.ValidateConfiguration("Configured", "null"), Has.Some.Contains("Configuration is required"));
	}

	[Test]
	public void ValidateConfiguration_PropertyMissingFromType_IsSkipped()
	{
		var registry = CreateRegistry();
		var info = registry.GetByName("Configured")!;
		// Inject a metadata entry that does not map to a real property on SampleConfig.
		info.Properties.Add(new FormPropertyInfo { Name = "Nonexistent", IsRequired = true });

		// The missing property is skipped (continue), so a valid config still validates clean.
		Assert.That(registry.ValidateConfiguration("Configured", new SampleConfig()), Is.Empty);
	}
}
