using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class FormComponentRegistryTests
{
    private IServiceScopeFactory _scopeFactory = null!;
    private IServiceScope _scope = null!;
    private IServiceProvider _serviceProvider = null!;
    private IFormComponentRegistrationService _service = null!;
    private FormComponentRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scope = Substitute.For<IServiceScope>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _service = Substitute.For<IFormComponentRegistrationService>();

        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider
            .GetService(typeof(IFormComponentRegistrationService))
            .Returns(_service);

        _service
            .GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<FormComponentRegistrationDTO>()));

        _registry = new FormComponentRegistry(_scopeFactory);
    }

    private static FormComponentRegistrationDTO CreateDto(
        string name,
        string category = "General",
        int order = 0,
        string displayName = "Display",
        string? editorTypeAlias = null,
        bool isDefault = false,
        string dataTypeNamesJson = "[]")
    {
        return new FormComponentRegistrationDTO
        {
            ComponentName = name,
            ViewComponentName = $"VC-{name}",
            DisplayName = displayName,
            Category = category,
            Order = order,
            EditorTypeAlias = editorTypeAlias,
            IsDefaultForType = isDefault,
            DataTypeNamesJson = dataTypeNamesJson
        };
    }

    [Test]
    public void Constructor_NullScopeFactory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new FormComponentRegistry(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void GetAll_NoComponents_ReturnsEmptyList()
    {
        _service
            .GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<FormComponentRegistrationDTO>()));

        var result = _registry.GetAll();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetAll_SortsByCategoryThenOrderThenDisplayName()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Third", category: "B", order: 1, displayName: "B"),
            CreateDto("First", category: "A", order: 0, displayName: "A"),
            CreateDto("Second", category: "A", order: 1, displayName: "A"),
            CreateDto("Fourth", category: "B", order: 0, displayName: "A"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetAll();

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result[0].Name, Is.EqualTo("First"));
        Assert.That(result[1].Name, Is.EqualTo("Second"));
        Assert.That(result[2].Name, Is.EqualTo("Fourth"));
        Assert.That(result[3].Name, Is.EqualTo("Third"));
    }

    [Test]
    public void GetAll_SameCategoryAndOrder_SortsByDisplayName()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("C", category: "X", order: 0, displayName: "Charlie"),
            CreateDto("A", category: "X", order: 0, displayName: "Alpha"),
            CreateDto("B", category: "X", order: 0, displayName: "Bravo"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetAll();

        Assert.That(result[0].Name, Is.EqualTo("A"));
        Assert.That(result[1].Name, Is.EqualTo("B"));
        Assert.That(result[2].Name, Is.EqualTo("C"));
    }

    [Test]
    public void GetByName_NullName_ReturnsNull()
    {
        var result = _registry.GetByName(null!);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetByName_EmptyStringName_ReturnsNull()
    {
        var result = _registry.GetByName(string.Empty);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetByName_ExactMatch_ReturnsComponent()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("TestComponent")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetByName("TestComponent");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestComponent"));
    }

    [Test]
    public void GetByName_CaseInsensitiveMatch_ReturnsComponent()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("TestCaseComponent")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetByName("testcasecomponent");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TestCaseComponent"));
    }

    [Test]
    public void GetByName_UnmatchedName_ReturnsNull()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("ExistingComponent")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetByName("NonExistentComponent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetByName_WhitespaceName_ProceedsToLookupAndReturnsNull()
    {
        var result = _registry.GetByName("   ");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetForEditorType_ValidEditorType_ReturnsComponent()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("TextComponent", editorTypeAlias: "Text")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("TextComponent"));
    }

    [Test]
    public void GetForEditorType_UnmatchedEditorType_ReturnsNull()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("TextComponent", editorTypeAlias: "Text")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Checkbox);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetForEditorType_NoComponents_ReturnsNull()
    {
        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetForEditorType_LastRegisteredWins_OnDuplicateEditorTypes()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("First", editorTypeAlias: "Text", order: 0),
            CreateDto("Second", editorTypeAlias: "Text", order: 1),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Second"));
    }

    [Test]
    public void GetForEditorType_EditorTypeAliasIsNull_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("NoEditor", editorTypeAlias: null)
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetForEditorType_EditorTypeAliasIsEmptyString_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("EmptyEditor", editorTypeAlias: "")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetForEditorType_InvalidEditorTypeAlias_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("InvalidEditor", editorTypeAlias: "NotAnEditorType")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetForEditorType(EditorType.Text);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetDefaultFor_NullType_ReturnsNull()
    {
        var result = _registry.GetDefaultFor(null!);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetDefaultFor_ValidType_ReturnsDefaultComponent()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "StringEditor",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("StringEditor"));
    }

    [Test]
    public void GetDefaultFor_NullableType_UnwrapsUnderlyingType()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "IntEditor",
                isDefault: true,
                dataTypeNamesJson: "[\"System.Int32\"]")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(int?));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("IntEditor"));
    }

    [Test]
    public void GetDefaultFor_UnmatchedType_ReturnsNull()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "StringEditor",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(int));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetDefaultFor_LowestOrderWins_OnMultipleDefaultsForType()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("HighOrder", isDefault: true, order: 10,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto("LowOrder", isDefault: true, order: 1,
                dataTypeNamesJson: "[\"System.String\"]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("LowOrder"));
    }

    [Test]
    public void GetDefaultFor_SameOrder_NameOrdinalBreaksTie()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Zebra", isDefault: true, order: 0,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto("Alpha", isDefault: true, order: 0,
                dataTypeNamesJson: "[\"System.String\"]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Alpha"));
    }

    [Test]
    public void GetDefaultFor_InvalidTypeNameInDataTypes_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "ValidOne",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto(
                "InvalidOne",
                isDefault: true,
                dataTypeNamesJson: "[\"NonExistent.Type.Invalid\"]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("ValidOne"));
    }

    [Test]
    public void GetDefaultFor_NullDataTypeNames_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "ValidOne",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto(
                "NullDataTypes",
                isDefault: true,
                dataTypeNamesJson: "null"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("ValidOne"));
    }

    [Test]
    public void GetDefaultFor_EmptyDataTypeNamesArray_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "ValidOne",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto(
                "EmptyArray",
                isDefault: true,
                dataTypeNamesJson: "[]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("ValidOne"));
    }

    [Test]
    public void Invalidate_ForcesReload_ServiceCalledAgain()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Original")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));
        _registry.GetAll();

        _service.ClearReceivedCalls();

        var newDtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Reloaded")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(newDtos));

        _registry.Invalidate();
        var result = _registry.GetAll();

        Assert.That(result[0].Name, Is.EqualTo("Reloaded"));
        _ = _service.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void ServiceThrowsOnLoad_ReturnsEmptyAndDoesNotCrash()
    {
        _service
            .GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<FormComponentRegistrationDTO>>(
                new InvalidOperationException("DB unavailable")));

        Assert.That(() => _registry.GetAll(), Throws.Nothing);
        var result = _registry.GetAll();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ServiceReturnsNull_ReturnsEmptyAndDoesNotCrash()
    {
        _service
            .GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<FormComponentRegistrationDTO>>(null!));

        Assert.That(() => _registry.GetAll(), Throws.Nothing);
        var result = _registry.GetAll();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnsureLoaded_CachedWithinInterval_ServiceCalledOnce()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Cached")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        _registry.GetAll();
        _service.ClearReceivedCalls();
        var result = _registry.GetAll();

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Cached"));
        _ = _service.DidNotReceive().GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void EnsureLoaded_ZeroComponents_AlwaysReloads()
    {
        _service
            .GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<FormComponentRegistrationDTO>()));

        _registry.GetAll();

        _service.ClearReceivedCalls();
        _registry.GetAll();

        _ = _service.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Invalidate_SnapshotEmpty_ForcesServiceCallEvenWhenCached()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("First")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));
        _registry.GetAll();

        _registry.Invalidate();

        var freshDtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Second")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(freshDtos));

        var result = _registry.GetAll();

        Assert.That(result[0].Name, Is.EqualTo("Second"));
    }

    [Test]
    public void GetAll_ConcurrentReaders_SeeConsistentSnapshot()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("Consistent")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var snapshot1 = _registry.GetAll();
        var snapshot2 = _registry.GetAll();

        Assert.That(snapshot1.Count, Is.EqualTo(snapshot2.Count));
        Assert.That(snapshot1[0].Name, Is.EqualTo(snapshot2[0].Name));
    }

    [Test]
    public void GetAll_SnapshotIsReadOnly()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("ReadOnly")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetAll();

        Assert.That(result.GetType().Name, Does.Contain("ReadOnly"));
    }

    [Test]
    public void GetDefaultFor_OnlyNonDefaultComponents_ReturnsNull()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("NotDefault", isDefault: false, dataTypeNamesJson: "[\"System.String\"]")
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetDefaultFor_NullOrEmptyTypeNamesInDataTypes_Skipped()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "ValidOne",
                isDefault: true,
                dataTypeNamesJson: "[\"System.String\"]"),
            CreateDto(
                "EmptyTypeNames",
                isDefault: true,
                dataTypeNamesJson: "[\"\", \"   \"]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(string));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("ValidOne"));
    }

    [Test]
    public void GetDefaultFor_TypeFoundViaAssemblyGetType_NotViaTypeGetType()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "ViaAssembly",
                isDefault: true,
                dataTypeNamesJson: "[\"WebWayCMS.Forms.FormComponentInfo\"]"),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetDefaultFor(typeof(FormComponentInfo));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("ViaAssembly"));
    }

    [Test]
    public void GetDefaultFor_MalformedDataTypeNamesJson_HandledGracefully()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto(
                "Malformed",
                isDefault: true,
                dataTypeNamesJson: "\"this is not a json array\""),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        Assert.That(() => _registry.GetAll(), Throws.Nothing);

        var byName = _registry.GetByName("Malformed");
        Assert.That(byName, Is.Not.Null);
        Assert.That(byName!.DataTypeNames, Is.Empty);
    }

    [Test]
    public void GetAll_WhitespaceOnlyDataTypeNamesJson_ReturnsEmptyDataTypeNames()
    {
        var dtos = new List<FormComponentRegistrationDTO>
        {
            CreateDto("WhitespaceJson", dataTypeNamesJson: "   "),
        };
        _service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dtos));

        var result = _registry.GetAll();

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].DataTypeNames, Is.Empty);
    }
}
