using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.WidgetRegistration;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

public struct WidgetThrowingStruct
{
    public WidgetThrowingStruct() => throw new InvalidOperationException("Test exception for coverage");
}

public class WidgetConfigWithThrowingDefault
{
    public WidgetThrowingStruct Prop { get; set; }
}

[TestFixture]
public class WidgetRegistrationModelTests
{
    private IContentStore<WidgetRegistrationDTO> _store = null!;
    private IWidgetRegistry _registry = null!;
    private IViewDiscoveryService _viewDiscovery = null!;
    private IChangeSetScope _changeSetScope = null!;
    private WidgetRegistrationModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<WidgetRegistrationDTO>>();
        _registry = Substitute.For<IWidgetRegistry>();
        _viewDiscovery = Substitute.For<IViewDiscoveryService>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new WidgetRegistrationModel(_store, _registry, _viewDiscovery, _changeSetScope);
    }

    private static WidgetRegistrationDTO Dto(
        Guid? nodeId = null,
        string componentName = "TestWidget",
        string displayName = "Test Widget",
        string category = "General",
        bool isActive = true,
        string? configTypeName = null,
        string propertyJson = "[]",
        int version = 0)
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new()
        {
            VersionId = Guid.NewGuid(),
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow },
                Title = displayName,
                Slug = componentName.ToLowerInvariant(),
                VersionNumber = version,
                State = ContentVersionState.Draft
            },
            ComponentName = componentName,
            DisplayName = displayName,
            Description = "desc",
            Category = category,
            IconClass = "fa",
            Order = 0,
            ConfigurationTypeName = configTypeName,
            PropertyDefinitionsJson = propertyJson,
            IsActive = isActive,
        };
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new WidgetRegistrationModel(null!, _registry, _viewDiscovery, _changeSetScope),
                Throws.ArgumentNullException);
            Assert.That(() => new WidgetRegistrationModel(_store, null!, _viewDiscovery, _changeSetScope),
                Throws.ArgumentNullException);
            Assert.That(() => new WidgetRegistrationModel(_store, _registry, null!, _changeSetScope),
                Throws.ArgumentNullException);
            Assert.That(() => new WidgetRegistrationModel(_store, _registry, _viewDiscovery, null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_HasExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("widgets"));
            Assert.That(_model.DisplayName, Is.EqualTo("Widget Registration"));
            Assert.That(_model.IndexViewPath, Does.Contain("Index.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("WidgetRegistrationUpsert.cshtml"));
            Assert.That(_model.SupportsVersionHistory, Is.True);
            Assert.That(_model.WriteRoles, Is.Null);
            Assert.That(_model.HasSecondaryApiList, Is.False);
            Assert.That(_model.RegistryHandler, Is.Not.Null);
            Assert.That(_model.ChildHandler, Is.Null);
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_ReturnsList()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            Dto(componentName: "A"),
            Dto(componentName: "B"),
        });

        var result = await _model.GetIndexViewModelAsync();

        Assert.That(result, Is.InstanceOf<WidgetRegistrationIndexViewModel>());
        var vm = (WidgetRegistrationIndexViewModel)result;
        Assert.That(vm.Registrations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NullId_ReturnsNewViewModel()
    {
        var result = await _model.GetUpsertViewModelAsync(null, new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.InstanceOf<WidgetRegistrationUpsertViewModel>());
    }

    [Test]
    public async Task GetUpsertViewModelAsync_WithId_ReturnsPopulatedViewModel()
    {
        var id = Guid.NewGuid();
        _store.GetCurrentDraftAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(
            nodeId: id, componentName: "CB", displayName: "Content Block", configTypeName: "SomeType"));

        var result = await _model.GetUpsertViewModelAsync(id, new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.InstanceOf<WidgetRegistrationUpsertViewModel>());
        var vm = (WidgetRegistrationUpsertViewModel)result!;
        Assert.Multiple(() =>
        {
            Assert.That(vm.ComponentName, Is.EqualTo("CB"));
            Assert.That(vm.DisplayName, Is.EqualTo("Content Block"));
            Assert.That(vm.ConfigurationTypeName, Is.EqualTo("SomeType"));
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NotFound_ReturnsNull()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WidgetRegistrationDTO?)null);

        var result = await _model.GetUpsertViewModelAsync(Guid.NewGuid(), new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CreateEmptyUpsertViewModel_ReturnsNewInstance()
    {
        Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<WidgetRegistrationUpsertViewModel>());
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Create_SavesAndInvalidates()
    {
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "New Widget",
            ComponentName = "New",
            DisplayName = "New Widget",
            Category = "Content",
            IsActive = true,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            _store.Received(1).SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_SavesAndInvalidates()
    {
        var id = Guid.NewGuid();
        var existing = Dto(nodeId: id, componentName: "Old", displayName: "Old Widget");
        _store.GetCurrentDraftAsync(id, Arg.Any<CancellationToken>()).Returns(existing);
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            NodeId = id,
            Title = "Updated",
            ComponentName = "Updated",
            DisplayName = "Updated Widget",
            Category = "Layout",
            IsActive = false,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            _store.Received(1).SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_NotFound_ReturnsError()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WidgetRegistrationDTO?)null);

        var vm = new WidgetRegistrationUpsertViewModel
        {
            NodeId = Guid.NewGuid(),
            Title = "X",
            ComponentName = "X",
            DisplayName = "X",
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("not found"));
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_SaveFailure_ReturnsError()
    {
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "Save failed."));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "New Widget",
            ComponentName = "New",
            DisplayName = "New Widget",
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_WithConfigurationTypeName_BuildsProperties()
    {
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "Typed Widget",
            ComponentName = "Typed",
            DisplayName = "Typed Widget",
            Category = "Content",
            ConfigurationTypeName = typeof(WidgetRegistrationModelTests).FullName,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_WithSystemTypeName_BuildsEmptyProperties()
    {
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "System Widget",
            ComponentName = "System",
            DisplayName = "System Widget",
            Category = "Content",
            ConfigurationTypeName = typeof(string).FullName,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_InvalidConfigurationTypeName_ReturnsError()
    {
        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "Bad Widget",
            ComponentName = "Bad",
            DisplayName = "Bad Widget",
            ConfigurationTypeName = "NonExistent.Type.Name",
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("could not be resolved"));
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_ThrowingDefaultValue_ReturnsError()
    {
        _store.SaveDraftAsync(Arg.Any<WidgetRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new WidgetRegistrationUpsertViewModel
        {
            Title = "Throwing Widget",
            ComponentName = "Throwing",
            DisplayName = "Throwing Widget",
            Category = "Content",
            ConfigurationTypeName = typeof(WidgetConfigWithThrowingDefault).FullName,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Failed to build properties"));
        });
    }

    [Test]
    public async Task DeleteAsync_DeletesAndInvalidates()
    {
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task DeleteAsync_Failure_DoesNotInvalidate()
    {
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _model.DeleteAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            _registry.DidNotReceive().Invalidate();
        });
    }

    [Test]
    public async Task GetApiListAsync_ReturnsAllDrafts()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<WidgetRegistrationDTO>
        {
            Dto(componentName: "A", displayName: "Alpha"),
            Dto(componentName: "B", displayName: "Beta"),
        });

        var result = await _model.GetApiListAsync();

        var list = result.ToList();
        Assert.That(list, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetRestoreVersionViewModelAsync_ReturnsViewModel()
    {
        var historicalId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        _store.GetVersionAsync(historicalId, Arg.Any<CancellationToken>()).Returns(Dto(
            nodeId: nodeId, componentName: "Hist", displayName: "History",
            configTypeName: "SomeType", version: 2));
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(
            nodeId: nodeId, componentName: "Current", displayName: "Current", version: 5));

        var result = await _model.GetRestoreVersionViewModelAsync(historicalId);

        Assert.That(result, Is.InstanceOf<WidgetRegistrationUpsertViewModel>());
        var vm = (WidgetRegistrationUpsertViewModel)result!;
        Assert.That(vm.ComponentName, Is.EqualTo("Hist"));
        Assert.That(vm.NodeId, Is.EqualTo(nodeId));
        Assert.That(vm.ExpectedVersionNumber, Is.EqualTo(5));
    }

    [Test]
    public async Task RestoreVersion_HistoricalNotFound_ReturnsNull()
    {
        _store.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WidgetRegistrationDTO?)null);

        var result = await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task VersionHistory_DelegatesToStore()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<WidgetRegistrationDTO> { Dto(nodeId: nodeId) });

        var result = await _model.GetVersionHistoryViewModelAsync(nodeId);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task DeleteVersion_DelegatesToStore()
    {
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteVersionAsync(Guid.NewGuid());

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task PublishAsync_DelegatesToStore()
    {
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);
    }

    // --- Registry handler tests ---

    [Test]
    public void RegistryHandler_GetAll_ReturnsJson()
    {
        _registry.GetAllComponents().Returns(new List<WidgetRegistrationInfo>
        {
            new() { Name = "A", DisplayName = "Alpha", Description = "d", Category = "Content" }
        });

        var result = _model.RegistryHandler!.GetAll();

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_EmptyName_ReturnsBadRequest()
    {
        var result = _model.RegistryHandler!.GetProperties(" ");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_NotFound_ReturnsNotFound()
    {
        _registry.GetByName("X").Returns((WidgetRegistrationInfo?)null);

        var result = _model.RegistryHandler!.GetProperties("X");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_ReturnsPropertyData()
    {
        _registry.GetByName("C").Returns(new WidgetRegistrationInfo
        {
            Name = "C",
            DisplayName = "Component C",
            Category = "General",
            Properties = new List<FormPropertyInfo>
            {
                new() { Name = "Title", Label = "Title", EditorType = EditorType.Text, IsRequired = true, Order = 1 },
            }
        });

        var result = _model.RegistryHandler!.GetProperties("C");

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_ViewPickerPopulatesDropdownFromDiscovery()
    {
        var views = new List<string> { "Default", "List" };
        _viewDiscovery.GetAvailableViews("SomeComponent").Returns(views);

        _registry.GetByName("V").Returns(new WidgetRegistrationInfo
        {
            Name = "V",
            DisplayName = "V",
            Category = "General",
            Properties = new List<FormPropertyInfo>
            {
                new()
                {
                    Name = "ViewName",
                    Label = "View",
                    EditorType = EditorType.ViewPicker,
                    ViewComponentName = "SomeComponent",
                    Order = 1,
                }
            }
        });

        var result = _model.RegistryHandler!.GetProperties("V");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _viewDiscovery.Received(1).GetAvailableViews("SomeComponent");
    }

    [Test]
    public void RegistryHandler_GetProperties_ViewPickerNoViews_ReturnsEmptyDict()
    {
        _viewDiscovery.GetAvailableViews("HasNoViews").Returns(new List<string>());

        _registry.GetByName("V").Returns(new WidgetRegistrationInfo
        {
            Name = "V",
            DisplayName = "V",
            Category = "General",
            Properties = new List<FormPropertyInfo>
            {
                new()
                {
                    Name = "ViewName",
                    Label = "View",
                    EditorType = EditorType.ViewPicker,
                    ViewComponentName = "HasNoViews",
                    Order = 1,
                }
            }
        });

        var result = _model.RegistryHandler!.GetProperties("V");

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_EmptyName_ReturnsBadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetForm("  ", null), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NotFound_ReturnsNotFound()
    {
        _registry.GetByName("X").Returns((WidgetRegistrationInfo?)null);

        Assert.That(_model.RegistryHandler!.GetForm("X", null), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NoConfigTypeName_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Plain").Returns(new WidgetRegistrationInfo
        {
            Name = "Plain",
            ConfigurationTypeName = null
        });

        var result = _model.RegistryHandler!.GetForm("Plain", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_UnresolvableConfigTypeName_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Bad").Returns(new WidgetRegistrationInfo
        {
            Name = "Bad",
            ConfigurationTypeName = "NonExistent.Type.Name.ForTest"
        });

        var result = _model.RegistryHandler!.GetForm("Bad", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_WithConfigTypeName_ReturnsPartialViewWithInstance()
    {
        _registry.GetByName("Typed").Returns(new WidgetRegistrationInfo
        {
            Name = "Typed",
            ConfigurationTypeName = typeof(SampleZoneConfig).FullName
        });

        var result = _model.RegistryHandler!.GetForm("Typed", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.TypeOf<SampleZoneConfig>());
    }

    [Test]
    public void RegistryHandler_GetForm_CoreAssemblyType_ResolvedDirectly()
    {
        _registry.GetByName("Core").Returns(new WidgetRegistrationInfo
        {
            Name = "Core",
            ConfigurationTypeName = typeof(WebWayCMS.Models.Page.PageTreeNode).FullName
        });

        var result = _model.RegistryHandler!.GetForm("Core", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
    }
}
