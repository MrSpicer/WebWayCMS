using System.Threading;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.ContentZones.Tests;

[TestFixture]
public class WidgetRegistryTests
{
    private IWidgetRegistrationService _service = null!;
    private WidgetRegistry _registry = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    private static WidgetRegistrationDTO WidgetDto(
        string name,
        string displayName,
        string category = "General",
        string? configType = null,
        string propertyJson = "[]",
        int order = 0)
    {
        var versionId = Guid.NewGuid();
        return new WidgetRegistrationDTO
        {
            ComponentName = name,
            DisplayName = displayName,
            Description = $"{name} desc",
            Category = category,
            IconClass = "fa",
            Order = order,
            ConfigurationTypeName = configType,
            PropertyDefinitionsJson = propertyJson,
            IsActive = true,
            VersionId = versionId,
            Version = new ContentVersion
            {
                Id = versionId,
                NodeId = Guid.NewGuid(),
                Node = new ContentNode { Id = Guid.NewGuid(), ContentTypeKey = "widgets" },
                State = ContentVersionState.Published,
                Title = name,
                Slug = name.ToLowerInvariant(),
            }
        };
    }

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IWidgetRegistrationService>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IWidgetRegistrationService)).Returns(_service);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);

        _registry = new WidgetRegistry(_scopeFactory);
    }

    [Test]
    public void GetAllComponents_ReturnsActiveWidgets()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("A", "Alpha", "Content"),
            WidgetDto("B", "Beta", "Layout")
        });

        var result = _registry.GetAllComponents();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Name, Is.EqualTo("A"));
            Assert.That(result[1].Name, Is.EqualTo("B"));
        });
    }

    [Test]
    public void GetAllComponents_SortsByCategoryThenOrderThenDisplayName()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("C", "CCC", "Layout", order: 2),
            WidgetDto("A", "AAA", "Content", order: 1),
            WidgetDto("B", "BBB", "Content", order: 0),
        });

        var result = _registry.GetAllComponents();

        Assert.That(result.Select(c => c.Name), Is.EqualTo(new[] { "B", "A", "C" }));
    }

    [Test]
    public void GetByName_ReturnsNullForEmptyOrUnknown()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>());

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByName(""), Is.Null);
            Assert.That(_registry.GetByName("missing"), Is.Null);
        });
    }

    [Test]
    public void GetByName_ReturnsWidget()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("ContentBlock", "Content Block", "Content")
        });

        var result = _registry.GetByName("ContentBlock");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DisplayName, Is.EqualTo("Content Block"));
    }

    [Test]
    public void GetCategories_ReturnsOrderedCategories()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("A", "A", "Layout"),
            WidgetDto("B", "B", "Content"),
            WidgetDto("C", "C", "Navigation"),
        });

        var categories = _registry.GetCategories();

        Assert.That(categories, Is.EqualTo(new[] { "Content", "Layout", "Navigation" }));
    }

    [Test]
    public void GetByCategory_EmptyOrNullReturnsEmpty()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>());

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByCategory(""), Is.Empty);
            Assert.That(_registry.GetByCategory(null!), Is.Empty);
        });
    }

    [Test]
    public void GetByCategory_ReturnsMatchingWidgets()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("A", "A", "Content"),
            WidgetDto("B", "B", "Layout"),
        });

        var result = _registry.GetByCategory("Content");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("A"));
    }

    [Test]
    public void GetComponentsByCategory_GroupsCorrectly()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("A", "A", "Content"),
            WidgetDto("B", "B", "Layout"),
        });

        var result = _registry.GetComponentsByCategory();

        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Has.Count.EqualTo(2));
            Assert.That(result["Content"], Has.Count.EqualTo(1));
            Assert.That(result["Layout"], Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void CreateDefaultConfiguration_NullConfigTypeName_ReturnsNull()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("NoConfig", "No Config", configType: null)
        });

        Assert.That(_registry.CreateDefaultConfiguration("NoConfig"), Is.Null);
    }

    [Test]
    public void CreateDefaultConfiguration_UnknownComponent_ReturnsNull()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>());

        Assert.That(_registry.CreateDefaultConfiguration("Missing"), Is.Null);
    }

    [Test]
    public void CreateDefaultConfiguration_ValidType_CreatesInstance()
    {
        var configType = typeof(SampleConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType)
        });

        var result = _registry.CreateDefaultConfiguration("Sample");

        Assert.That(result, Is.InstanceOf<SampleConfig>());
    }

    [Test]
    public void CreateDefaultConfiguration_InvalidTypeName_ReturnsNull()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Bad", "Bad", configType: "NonExistent.Type")
        });

        Assert.That(_registry.CreateDefaultConfiguration("Bad"), Is.Null);
    }

    [Test]
    public void CreateDefaultConfiguration_NoDefaultCtor_ReturnsNull()
    {
        var configType = typeof(NoDefaultCtorConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("NoCtor", "No Ctor", configType: configType)
        });

        Assert.That(_registry.CreateDefaultConfiguration("NoCtor"), Is.Null);
    }

    [Test]
    public void ValidateConfiguration_UnknownComponent_ReturnsError()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>());

        var errors = _registry.ValidateConfiguration("Missing", new object());

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Unknown component"));
    }

    [Test]
    public void ValidateConfiguration_NoConfigType_ReturnsEmpty()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("NoConfig", "NoConfig")
        });

        var errors = _registry.ValidateConfiguration("NoConfig", new object());

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ValidateConfiguration_InvalidJson_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(
                    new List<FormPropertyInfo> { new() { Name = "Name", IsRequired = true, Label = "Name" } }))
        });

        var errors = _registry.ValidateConfiguration("Sample", "{invalid json");

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Invalid JSON"));
    }

    [Test]
    public void ValidateConfiguration_RequiredFieldMissing_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(
                    new List<FormPropertyInfo> { new() { Name = "Name", IsRequired = true, Label = "Name" } }))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig { Name = "" });

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Name is required"));
    }

    [Test]
    public void ValidateConfiguration_NullConfig_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: "[]")
        });

        var errors = _registry.ValidateConfiguration("Sample", (object)null!);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Configuration is required"));
    }

    [Test]
    public void ValidateConfiguration_ValidConfig_ReturnsEmpty()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "valid",
            Ref = Guid.NewGuid(),
            RequiredNullable = "set",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Invalidate_ForcesReload()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            new List<WidgetRegistrationDTO> { WidgetDto("First", "First") },
            new List<WidgetRegistrationDTO> { WidgetDto("Second", "Second") });

        var first = _registry.GetAllComponents();
        Assert.That(first[0].Name, Is.EqualTo("First"));

        _registry.Invalidate();

        var second = _registry.GetAllComponents();
        Assert.That(second[0].Name, Is.EqualTo("Second"));
    }

    [Test]
    public void ServiceThrows_ReturnsEmptyAndDoesNotCrash()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<List<WidgetRegistrationDTO>>(new InvalidOperationException("db down")));

        var result = _registry.GetAllComponents();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DeserializesPropertyDefinitions()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        var json = System.Text.Json.JsonSerializer.Serialize(properties);

        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", propertyJson: json,
                configType: typeof(SampleConfig).FullName)
        });

        var result = _registry.GetByName("Sample");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Has.Count.EqualTo(properties.Count));
        Assert.That(result.HasConfiguration, Is.True);
    }

    [Test]
    public void ValidateConfiguration_MinValue_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 0,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_MaxValue_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 11,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_MinOnly_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            MinOnly = 0,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_MaxOnly_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            MaxOnly = 101,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_MaxLength_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            Code = "1234",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_PatternMismatch_CustomErrorMessage()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            Code = "ok",
            Digits = "abc",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_PatternMismatch_DefaultMessage()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "123",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_GuidEmpty_Required_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.Empty,
            RequiredNullable = "s",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_RequiredNullableNull_ReturnsError()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = null,
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ValidateConfiguration_ConfigTypeNotFound_ReturnsEmpty()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Unknown", "Unknown", configType: "NonExistent.Type")
        });

        var errors = _registry.ValidateConfiguration("Unknown", new object());

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void GetByCategory_UnknownCategory_ReturnsEmpty()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("A", "A", "Content")
        });

        var result = _registry.GetByCategory("Unknown");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByName_NullInput_ReturnsNull()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>());

        Assert.That(_registry.GetByName(null!), Is.Null);
    }

    [Test]
    public void ValidateConfiguration_NotANumberForRange_DoesNotThrow()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var config = new SampleConfig
        {
            Name = "n",
            Ref = Guid.NewGuid(),
            RequiredNullable = "s",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
            OptionalNum = null,
            NotANumber = "abc",
        };

        var errors = _registry.ValidateConfiguration("Sample", config);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void DeserializeEmptyJson_ReturnsEmptyList()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Empty", "Empty", propertyJson: "")
        });

        var result = _registry.GetByName("Empty");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Is.Empty);
    }

    [Test]
    public void GetAllComponents_SameCategorySameOrder_SortsByDisplayName()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("B", "BBB", "Content", order: 0),
            WidgetDto("A", "AAA", "Content", order: 0),
        });

        var result = _registry.GetAllComponents();

        Assert.That(result.Select(c => c.Name), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void ValidateConfiguration_JsonStringInput_Deserializes()
    {
        var configType = typeof(SampleConfig).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SampleConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var json = System.Text.Json.JsonSerializer.Serialize(new SampleConfig
        {
            Name = "valid",
            Ref = Guid.NewGuid(),
            RequiredNullable = "set",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        var errors = _registry.ValidateConfiguration("Sample", json);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void BuildFromDtos_DeserializeInvalidJson_ReturnsEmptyProperties()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Bad", "Bad", propertyJson: "{invalid json}")
        });

        var result = _registry.GetByName("Bad");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Is.Empty);
    }

    [Test]
    public void EnsureLoaded_CachesWithinInterval()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("First", "First")
        });

        _registry.GetAllComponents();

        _registry.GetAllComponents();

        _service.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void BuildFromDtos_NullJson_DeserializeHandlesGracefully()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Null", "Null", propertyJson: null!)
        });

        var result = _registry.GetByName("Null");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Is.Empty);
    }

    [Test]
    public void CreateDefaultConfiguration_ResolvesTypeFromReferencedAssembly()
    {
        var configType = typeof(WebWayCMS.Forms.FormPropertyInfo).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("External", "External", configType: configType)
        });

        var result = _registry.CreateDefaultConfiguration("External");

        Assert.That(result, Is.InstanceOf<WebWayCMS.Forms.FormPropertyInfo>());
    }

    [Test]
    public void ValidateConfiguration_TypeFromReferencedAssembly_ValidatesSuccessfully()
    {
        var configType = typeof(WebWayCMS.Forms.FormPropertyInfo).FullName;
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(WebWayCMS.Forms.FormPropertyInfo));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("FPI", "FPI", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(properties))
        });

        var errors = _registry.ValidateConfiguration("FPI", new WebWayCMS.Forms.FormPropertyInfo());

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ResolveType_ResolvesFromAssemblyScan()
    {
        var type = WidgetRegistry.ResolveType(typeof(WebWayCMS.Forms.FormPropertyInfo).FullName!);

        Assert.That(type, Is.Not.Null);
        Assert.That(type, Is.EqualTo(typeof(WebWayCMS.Forms.FormPropertyInfo)));
    }

    [Test]
    public void ResolveType_NonExistentType_ReturnsNull()
    {
        var type = WidgetRegistry.ResolveType("This.Type.Does.Not.Exist");

        Assert.That(type, Is.Null);
    }

    [Test]
    public void ResolveType_NullOrEmpty_ReturnsNull()
    {
        Assert.That(WidgetRegistry.ResolveType(""), Is.Null);
        Assert.That(WidgetRegistry.ResolveType(null!), Is.Null);
    }

    [Test]
    public void ResolveType_AssemblyQualifiedNonExistentAssembly_ReturnsNull()
    {
        var type = WidgetRegistry.ResolveType("SomeType, NonExistentAssembly.DoesNotExist.Version99");

        Assert.That(type, Is.Null);
    }

    [Test]
    public void Constructor_NullScopeFactory_ThrowsArgumentNullException()
    {
        Assert.That(() => new WidgetRegistry(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void ValidateConfiguration_UnknownPropertyInDefinitions_SkipsGracefully()
    {
        var configType = typeof(SampleConfig).FullName;
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Sample", "Sample", configType: configType,
                propertyJson: System.Text.Json.JsonSerializer.Serialize(
                    new List<FormPropertyInfo>
                    {
                        new() { Name = "NonExistentProperty", IsRequired = true, Label = "Missing" }
                    }))
        });

        var errors = _registry.ValidateConfiguration("Sample", new SampleConfig
        {
            Name = "valid",
            Ref = Guid.NewGuid(),
            RequiredNullable = "set",
            Count = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc",
        });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void DeserializeEmptyBracketsJson_ReturnsEmptyList()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("Brackets", "Brackets", propertyJson: "[]")
        });

        var result = _registry.GetByName("Brackets");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Is.Empty);
    }

    [Test]
    public void ResolveType_ValidTypeName_ReturnsType()
    {
        var type = WidgetRegistry.ResolveType("System.String");

        Assert.That(type, Is.Not.Null);
        Assert.That(type, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ResolveType_MalformedAssemblyQualifiedName_ReturnsNull()
    {
        var type = WidgetRegistry.ResolveType("SomeType, SomeAssembly, Version=not.a.version");

        Assert.That(type, Is.Null);
    }

    [Test]
    public void DeserializeNullJson_ReturnsEmptyList()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            WidgetDto("NullJson", "NullJson", propertyJson: "null")
        });

        var result = _registry.GetByName("NullJson");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties, Is.Empty);
    }

    [Test]
    public void EnsureLoaded_DoubleCheckLocked_ReturnsCachedSnapshot()
    {
        var t1Blocked = new ManualResetEventSlim(false);
        var releaseT1 = new ManualResetEventSlim(false);

        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            t1Blocked.Set();
            releaseT1.Wait();
            return Task.FromResult(new List<WidgetRegistrationDTO>
            {
                WidgetDto("Threaded", "Threaded")
            });
        });

        string? t1Result = null;
        string? t2Result = null;

        var t1 = new Thread(() =>
        {
            t1Result = _registry.GetByName("Threaded")?.DisplayName;
        });
        t1.Start();

        t1Blocked.Wait();

        var t2 = new Thread(() =>
        {
            t2Result = _registry.GetByName("Threaded")?.DisplayName;
        });
        t2.Start();

        Thread.Sleep(100);

        releaseT1.Set();
        t1.Join();
        t2.Join();

        Assert.Multiple(() =>
        {
            Assert.That(t1Result, Is.EqualTo("Threaded"));
            Assert.That(t2Result, Is.EqualTo("Threaded"));
        });

        _service.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }
}
