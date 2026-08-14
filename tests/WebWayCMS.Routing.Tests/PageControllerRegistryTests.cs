using System.Reflection;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class PageControllerRegistryTests
{
    private IPageControllerRegistrationService _service = null!;
    private PageControllerRegistry _registry = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    private static PageControllerRegistrationDTO Dto(
        string controllerName,
        string displayName,
        string category = "General",
        string? configTypeName = null,
        string propertyJson = "[]",
        int order = 0,
        string controllerTypeName = "Some.Type") => new()
        {
            ControllerName = controllerName,
            ControllerTypeName = controllerTypeName,
            DisplayName = displayName,
            Description = $"{controllerName} desc",
            Category = category,
            IconClass = "fa",
            Order = order,
            ConfigurationTypeName = configTypeName,
            PropertyDefinitionsJson = propertyJson,
            IsActive = true
        };

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IPageControllerRegistrationService>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IPageControllerRegistrationService)).Returns(_service);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);

        _registry = new PageControllerRegistry(_scopeFactory);
    }

    [Test]
    public void GetAllControllers_ReturnsActiveControllers()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Custom Display", "Content", configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig)))),
            Dto("Alpha", "Alpha", "Content", order: 1),
            Dto("Banner", "Banner", "Layout"),
        });

        var names = _registry.GetAllControllers().Select(c => c.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Configured"));
            Assert.That(names, Does.Contain("Alpha"));
            Assert.That(names, Does.Contain("Banner"));
        });
    }

    [Test]
    public void GetByName_DisplayNameFallbackAndOverride()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Alpha", "Alpha", "Content", order: 1),
            Dto("Configured", "Custom Display", "Content", order: 2),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByName("Alpha")!.DisplayName, Is.EqualTo("Alpha"));
            Assert.That(_registry.GetByName("Configured")!.DisplayName, Is.EqualTo("Custom Display"));
        });
    }

    [Test]
    public void GetByName_PropertiesAndHasConfiguration()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
            Dto("Banner", "Banner", "Layout"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByName("Configured")!.Properties, Is.Not.Empty);
            Assert.That(_registry.GetByName("Configured")!.HasConfiguration, Is.True);
            Assert.That(_registry.GetByName("Banner")!.Properties, Is.Empty);
            Assert.That(_registry.GetByName("Banner")!.HasConfiguration, Is.False);
        });
    }

    [Test]
    public void GetAllControllers_SortsByCategoryThenOrderThenName()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content", order: 2),
            Dto("Alpha", "Alpha", "Content", order: 1),
            Dto("Banner", "Banner", "Layout", order: 0),
        });

        var all = _registry.GetAllControllers();
        var contentNames = all.Where(c => c.Category == "Content").Select(c => c.Name).ToList();
        var categories = all.Select(c => c.Category).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(contentNames.IndexOf("Alpha"), Is.LessThan(contentNames.IndexOf("Configured")));
            Assert.That(categories.IndexOf("Content"), Is.LessThan(categories.IndexOf("Layout")));
        });
    }

    [Test]
    public void ServiceThrows_ReturnsEmptyAndDoesNotCrash()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<List<PageControllerRegistrationDTO>>(new InvalidOperationException("db down")));

        var result = _registry.GetAllControllers();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByName_NullOrUnknown_ReturnsNull()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>());

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByName(string.Empty), Is.Null);
            Assert.That(_registry.GetByName("Missing"), Is.Null);
            Assert.That(_registry.GetByName(null!), Is.Null);
        });
    }

    [Test]
    public void GetCategories_SortedDistinct()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("A", "A", "Content"),
            Dto("B", "B", "Layout"),
        });

        Assert.That(_registry.GetCategories(), Is.EqualTo(new[] { "Content", "Layout" }));
    }

    [Test]
    public void GetByCategory_EmptyUnknownAndKnown()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Banner", "Banner", "Layout"),
            Dto("Alpha", "Alpha", "Content"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.GetByCategory(string.Empty), Is.Empty);
            Assert.That(_registry.GetByCategory("Nope"), Is.Empty);
            Assert.That(_registry.GetByCategory("Layout").Select(c => c.Name), Is.EqualTo(new[] { "Banner" }));
        });
    }

    [Test]
    public void CreateDefaultConfiguration_Variants()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Banner", "Banner", "Layout"),
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig)))),
            Dto("BrokenConfig", "BrokenConfig", "Content",
                configTypeName: typeof(NoDefaultCtorConfig).FullName,
                propertyJson: "[]"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.CreateDefaultConfiguration("Banner"), Is.Null);
            Assert.That(_registry.CreateDefaultConfiguration("Missing"), Is.Null);
            Assert.That(_registry.CreateDefaultConfiguration("Configured"), Is.InstanceOf<SamplePageConfig>());
            Assert.That(_registry.CreateDefaultConfiguration("BrokenConfig"), Is.Null);
        });
    }

    [Test]
    public void ValidateConfiguration_UnknownAndNoConfig()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Banner", "Banner", "Layout"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.ValidateConfiguration("Missing", new object()), Has.Some.Contains("Unknown controller"));
            Assert.That(_registry.ValidateConfiguration("Banner", new object()), Is.Empty);
        });
    }

    [Test]
    public void ValidateConfiguration_ValidObjectAndJson()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
        });

        var json = JsonSerializer.Serialize(new SamplePageConfig
        {
            Title = "valid",
            Ref = Guid.NewGuid(),
            PageSize = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc"
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.ValidateConfiguration("Configured", new SamplePageConfig
            {
                Title = "valid",
                Ref = Guid.NewGuid(),
                PageSize = 5,
                Code = "ok",
                Digits = "123",
                Letters = "abc"
            }), Is.Empty);
            Assert.That(_registry.ValidateConfiguration("Configured", json), Is.Empty);
        });
    }

    [Test]
    public void ValidateConfiguration_InvalidObject_ReturnsErrors()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
        });

        var config = new SamplePageConfig
        {
            Title = "  ",
            Ref = Guid.Empty,
            PageSize = 20,
            Code = "toolong",
            Digits = "abc",
            Letters = "123"
        };

        var errors = _registry.ValidateConfiguration("Configured", config);

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
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
        });

        Assert.That(_registry.ValidateConfiguration("Configured", new SamplePageConfig
        {
            Title = "v",
            Ref = Guid.NewGuid(),
            PageSize = 0,
            Code = "ok",
            Digits = "123",
            Letters = "abc"
        }), Has.Some.Contains("at least"));
    }

    [Test]
    public void ValidateConfiguration_InvalidAndNullJson()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.ValidateConfiguration("Configured", "{ not json"), Has.Some.Contains("Invalid JSON"));
            Assert.That(_registry.ValidateConfiguration("Configured", "null"), Has.Some.Contains("Configuration is required"));
        });
    }

    [Test]
    public void ValidateConfiguration_PropertyMissingFromType_IsSkipped()
    {
        var properties = FormPropertyBuilder.BuildPropertyInfos(typeof(SamplePageConfig));
        properties.Add(new FormPropertyInfo { Name = "Nonexistent", IsRequired = true });
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Configured", "Configured", "Content",
                configTypeName: typeof(SamplePageConfig).FullName,
                propertyJson: JsonSerializer.Serialize(properties)),
        });

        Assert.That(_registry.ValidateConfiguration("Configured", new SamplePageConfig
        {
            Title = "v",
            Ref = Guid.NewGuid(),
            PageSize = 5,
            Code = "ok",
            Digits = "123",
            Letters = "abc"
        }), Is.Empty);
    }

    [Test]
    public void Invalidate_ForcesReload()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
            new List<PageControllerRegistrationDTO> { Dto("First", "First") },
            new List<PageControllerRegistrationDTO> { Dto("Second", "Second") });

        var first = _registry.GetAllControllers();
        Assert.That(first[0].Name, Is.EqualTo("First"));

        _registry.Invalidate();

        var second = _registry.GetAllControllers();
        Assert.That(second[0].Name, Is.EqualTo("Second"));
    }

    [Test]
    public void ValidateConfiguration_ConfigTypeNotFound_ReturnsEmpty()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Unknown", "Unknown", configTypeName: "NonExistent.Type", controllerTypeName: "NonExistent.Type")
        });

        var errors = _registry.ValidateConfiguration("Unknown", new object());

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void EnsureLoaded_CachesWithinInterval()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("First", "First")
        });

        _registry.GetAllControllers();

        _registry.GetAllControllers();

        _service.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void ResolveType_ResolvesFromAssemblyScan()
    {
        var type = PageControllerRegistry.ResolveType(typeof(SamplePageConfig).FullName!);

        Assert.That(type, Is.Not.Null);
        Assert.That(type, Is.EqualTo(typeof(SamplePageConfig)));
    }

    [Test]
    public void ResolveType_NonExistentType_ReturnsNull()
    {
        var type = PageControllerRegistry.ResolveType("This.Type.Does.Not.Exist");

        Assert.That(type, Is.Null);
    }

    [Test]
    public void ResolveType_NullOrEmpty_ReturnsNull()
    {
        Assert.That(PageControllerRegistry.ResolveType(""), Is.Null);
        Assert.That(PageControllerRegistry.ResolveType(null!), Is.Null);
    }

    [Test]
    public void ResolveType_FindsBuiltInTypeDirectly()
    {
        var type = PageControllerRegistry.ResolveType("System.String");

        Assert.That(type, Is.Not.Null);
        Assert.That(type, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void BuildFromDtos_NullPropertyDefinitionsJson_ReturnsEmptyList()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Test", "Test", propertyJson: null!)
        });

        var info = _registry.GetByName("Test");

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Properties, Is.Empty);
    }

    [Test]
    public void BuildFromDtos_MalformedPropertyDefinitionsJson_ReturnsEmptyList()
    {
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto("Test", "Test", propertyJson: "{ not valid"),
            Dto("NullJson", "NullJson", propertyJson: "null")
        });

        var info = _registry.GetByName("Test");
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Properties, Is.Empty);

        var nullInfo = _registry.GetByName("NullJson");
        Assert.That(nullInfo, Is.Not.Null);
        Assert.That(nullInfo!.Properties, Is.Empty);
    }

    [Test]
    public void Constructor_NullScopeFactory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PageControllerRegistry(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("scopeFactory"));
    }

    [Test]
    public void ResolveType_InvalidTypeName_ReturnsNull()
    {
        Assert.That(PageControllerRegistry.ResolveType("["), Is.Null);
        Assert.That(PageControllerRegistry.ResolveType("a,b,c,d"), Is.Null);
        Assert.That(PageControllerRegistry.ResolveType(new string('x', 10000)), Is.Null);
        Assert.That(PageControllerRegistry.ResolveType("a\r\nb"), Is.Null);
        Assert.That(PageControllerRegistry.ResolveType("ab\u001Fcd"), Is.Null);
    }
}
