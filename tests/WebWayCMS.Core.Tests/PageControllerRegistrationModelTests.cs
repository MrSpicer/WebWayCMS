using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageControllerRegistration;
using WebWayCMS.Pages;

namespace WebWayCMS.Core.Tests;

public struct PageThrowingStruct
{
    public PageThrowingStruct() => throw new InvalidOperationException("Test exception for coverage");
}

public class PageConfigWithThrowingDefault
{
    public PageThrowingStruct Prop { get; set; }
}

[TestFixture]
public class PageControllerRegistrationModelTests
{
    private IContentStore<PageControllerRegistrationDTO> _store = null!;
    private IPageControllerRegistry _registry = null!;
    private IChangeSetScope _changeSetScope = null!;
    private PageControllerRegistrationModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<PageControllerRegistrationDTO>>();
        _registry = Substitute.For<IPageControllerRegistry>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new PageControllerRegistrationModel(_store, _registry, _changeSetScope);
    }

    private static PageControllerRegistrationDTO Dto(
        Guid? nodeId = null,
        string controllerName = "GenericPage",
        string controllerTypeName = "MyApp.GenericPageController",
        string displayName = "Generic Page",
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
                Slug = controllerName.ToLowerInvariant(),
                VersionNumber = version,
                State = ContentVersionState.Draft
            },
            ControllerName = controllerName,
            ControllerTypeName = controllerTypeName,
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
            Assert.That(() => new PageControllerRegistrationModel(null!, _registry, _changeSetScope),
                Throws.ArgumentNullException);
            Assert.That(() => new PageControllerRegistrationModel(_store, null!, _changeSetScope),
                Throws.ArgumentNullException);
            Assert.That(() => new PageControllerRegistrationModel(_store, _registry, null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_HasExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("pagetypes"));
            Assert.That(_model.DisplayName, Is.EqualTo("Page Controller Registration"));
            Assert.That(_model.IndexViewPath, Does.Contain("Index.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("PageControllerRegistrationUpsert.cshtml"));
            Assert.That(_model.SupportsVersionHistory, Is.True);
            Assert.That(_model.WriteRoles, Is.Null);
            Assert.That(_model.HasSecondaryApiList, Is.False);
            Assert.That(_model.RegistryHandler, Is.Null);
            Assert.That(_model.ChildHandler, Is.Null);
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_ReturnsList()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto(controllerName: "A"),
            Dto(controllerName: "B"),
        });

        var result = await _model.GetIndexViewModelAsync();

        Assert.That(result, Is.InstanceOf<PageControllerRegistrationIndexViewModel>());
        var vm = (PageControllerRegistrationIndexViewModel)result;
        Assert.That(vm.Registrations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NullId_ReturnsNewViewModel()
    {
        var result = await _model.GetUpsertViewModelAsync(null, new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.InstanceOf<PageControllerRegistrationUpsertViewModel>());
    }

    [Test]
    public async Task GetUpsertViewModelAsync_WithId_ReturnsPopulatedViewModel()
    {
        var id = Guid.NewGuid();
        _store.GetCurrentDraftAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(
            nodeId: id, controllerName: "GP", displayName: "Generic Page", configTypeName: "SomeType"));

        var result = await _model.GetUpsertViewModelAsync(id, new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.InstanceOf<PageControllerRegistrationUpsertViewModel>());
        var vm = (PageControllerRegistrationUpsertViewModel)result!;
        Assert.Multiple(() =>
        {
            Assert.That(vm.ControllerName, Is.EqualTo("GP"));
            Assert.That(vm.DisplayName, Is.EqualTo("Generic Page"));
            Assert.That(vm.ConfigurationTypeName, Is.EqualTo("SomeType"));
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NotFound_ReturnsNull()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var result = await _model.GetUpsertViewModelAsync(Guid.NewGuid(), new Microsoft.AspNetCore.Http.QueryCollection());

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CreateEmptyUpsertViewModel_ReturnsNewInstance()
    {
        Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<PageControllerRegistrationUpsertViewModel>());
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Create_SavesAndInvalidates()
    {
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "New Page Type",
            ControllerName = "New",
            ControllerTypeName = "MyApp.NewController",
            DisplayName = "New Page Type",
            Category = "Content",
            IsActive = true,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            _store.Received(1).SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_SavesAndInvalidates()
    {
        var id = Guid.NewGuid();
        var existing = Dto(nodeId: id, controllerName: "Old", displayName: "Old Page");
        _store.GetCurrentDraftAsync(id, Arg.Any<CancellationToken>()).Returns(existing);
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            NodeId = id,
            Title = "Updated",
            ControllerName = "Updated",
            ControllerTypeName = "MyApp.UpdatedController",
            DisplayName = "Updated Page",
            Category = "Layout",
            IsActive = false,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            _store.Received(1).SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_NotFound_ReturnsError()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            NodeId = Guid.NewGuid(),
            Title = "X",
            ControllerName = "X",
            ControllerTypeName = "Type.X",
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
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "Save failed."));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "New Page Type",
            ControllerName = "New",
            ControllerTypeName = "MyApp.NewController",
            DisplayName = "New Page Type",
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_WithConfigurationTypeName_BuildsProperties()
    {
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "Typed Page",
            ControllerName = "Typed",
            ControllerTypeName = "Type.Typed",
            DisplayName = "Typed Page",
            Category = "Content",
            ConfigurationTypeName = typeof(PageControllerRegistrationModelTests).FullName,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_WithSystemTypeName_BuildsEmptyProperties()
    {
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "System Page",
            ControllerName = "System",
            ControllerTypeName = "Type.System",
            DisplayName = "System Page",
            Category = "Content",
            ConfigurationTypeName = typeof(string).FullName,
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_InvalidConfigurationTypeName_ReturnsError()
    {
        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "Bad Page",
            ControllerName = "Bad",
            ControllerTypeName = "Type.Bad",
            DisplayName = "Bad Page",
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
        _store.SaveDraftAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Title = "Throwing Page",
            ControllerName = "Throwing",
            ControllerTypeName = "Type.Throwing",
            DisplayName = "Throwing Page",
            Category = "Content",
            ConfigurationTypeName = typeof(PageConfigWithThrowingDefault).FullName,
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
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto(controllerName: "A", displayName: "Alpha"),
            Dto(controllerName: "B", displayName: "Beta"),
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
            nodeId: nodeId, controllerName: "Hist", displayName: "History",
            configTypeName: "SomeType", version: 2));
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(
            nodeId: nodeId, controllerName: "Current", displayName: "Current", version: 5));

        var result = await _model.GetRestoreVersionViewModelAsync(historicalId);

        Assert.That(result, Is.InstanceOf<PageControllerRegistrationUpsertViewModel>());
        var vm = (PageControllerRegistrationUpsertViewModel)result!;
        Assert.That(vm.ControllerName, Is.EqualTo("Hist"));
        Assert.That(vm.NodeId, Is.EqualTo(nodeId));
        Assert.That(vm.ExpectedVersionNumber, Is.EqualTo(5));
    }

    [Test]
    public async Task RestoreVersion_HistoricalNotFound_ReturnsNull()
    {
        _store.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var result = await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task VersionHistory_DelegatesToStore()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<PageControllerRegistrationDTO> { Dto(nodeId: nodeId) });

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
}
