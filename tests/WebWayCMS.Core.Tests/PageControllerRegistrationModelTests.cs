using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageControllerRegistration;
using WebWayCMS.Pages;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageControllerRegistrationModelTests
{
    private IContentService<PageControllerRegistrationDTO> _service = null!;
    private IPageControllerRegistry _registry = null!;
    private PageControllerRegistrationModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IContentService<PageControllerRegistrationDTO>>();
        _registry = Substitute.For<IPageControllerRegistry>();
        _model = new PageControllerRegistrationModel(_service, _registry);
    }

    private static PageControllerRegistrationDTO Dto(
        Guid? id = null,
        string controllerName = "GenericPage",
        string controllerTypeName = "MyApp.GenericPageController",
        string displayName = "Generic Page",
        string category = "General",
        bool isActive = true,
        string? configTypeName = null,
        string propertyJson = "[]",
        bool published = true,
        Guid masterId = default) =>
        new()
        {
            ContentId = id ?? Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = id ?? Guid.NewGuid(),
                MasterId = masterId == default ? Guid.NewGuid() : masterId,
                Version = 0,
                Title = displayName,
                Slug = controllerName.ToLowerInvariant(),
                IsPublished = published,
                IsDeleted = false,
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

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageControllerRegistrationModel(null!, _registry),
                Throws.ArgumentNullException);
            Assert.That(() => new PageControllerRegistrationModel(_service, null!),
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
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
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
        _service.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(
            id: id, controllerName: "GP", displayName: "Generic Page", configTypeName: "SomeType"));

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
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

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
        _service.CreateAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<CancellationToken>())
            .Returns(Dto(controllerName: "New"));

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
            _service.Received(1).CreateAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_SavesAndInvalidates()
    {
        var id = Guid.NewGuid();
        var existing = Dto(id: id, controllerName: "Old", displayName: "Old Page");
        _service.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(existing);
        _service.UpdateAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<CancellationToken>()).Returns(true);

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Id = id,
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
            _service.Received(1).UpdateAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<CancellationToken>());
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task SaveUpsertCoreAsync_Update_NotFound_ReturnsError()
    {
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var vm = new PageControllerRegistrationUpsertViewModel
        {
            Id = Guid.NewGuid(),
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
    public async Task SaveUpsertCoreAsync_WithConfigurationTypeName_BuildsProperties()
    {
        _service.CreateAsync(Arg.Any<PageControllerRegistrationDTO>(), Arg.Any<CancellationToken>())
            .Returns(Dto(controllerName: "Typed"));

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
    public async Task DeleteAsync_DeletesAndInvalidates()
    {
        _service.DeleteAsync(Arg.Any<Guid>(), false, true, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            _registry.Received(1).Invalidate();
        });
    }

    [Test]
    public async Task GetApiListAsync_ReturnsPublishedControllers()
    {
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageControllerRegistrationDTO>
        {
            Dto(controllerName: "A", displayName: "Alpha", published: true),
            Dto(controllerName: "B", displayName: "Beta", published: false),
        });

        var result = await _model.GetApiListAsync();

        var list = result.ToList();
        Assert.That(list, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetRestoreVersionViewModelAsync_ReturnsViewModel()
    {
        var historicalId = Guid.NewGuid();
        var masterId = Guid.NewGuid();
        var latestId = Guid.NewGuid();

        _service.GetByIdAsync(historicalId, Arg.Any<CancellationToken>()).Returns(Dto(
            id: historicalId, controllerName: "Hist", displayName: "History",
            configTypeName: "SomeType", masterId: masterId));

        _service.GetByMasterIdAsync(masterId, Arg.Any<CancellationToken>()).Returns(Dto(
            id: latestId, controllerName: "Latest", masterId: masterId));

        var result = await _model.GetRestoreVersionViewModelAsync(historicalId);

        Assert.That(result, Is.InstanceOf<PageControllerRegistrationUpsertViewModel>());
        var vm = (PageControllerRegistrationUpsertViewModel)result!;
        Assert.That(vm.ControllerName, Is.EqualTo("Hist"));
        Assert.That(vm.Id, Is.EqualTo(latestId));
    }

    [Test]
    public async Task RestoreVersion_HistoricalNotFound_ReturnsNull()
    {
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var result = await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RestoreVersion_LatestNotFound_ReturnsNull()
    {
        var dto = Dto();
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(dto);
        _service.GetByMasterIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageControllerRegistrationDTO?)null);

        var result = await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task VersionHistory_DelegatesToService()
    {
        var masterId = Guid.NewGuid();
        _service.GetAllVersionsAsync(masterId, Arg.Any<CancellationToken>())
            .Returns(new List<PageControllerRegistrationDTO> { Dto() });

        var result = await _model.GetVersionHistoryViewModelAsync(masterId);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task DeleteVersion_DelegatesToService()
    {
        _service.DeleteAsync(Arg.Any<Guid>(), false, false, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteVersionAsync(Guid.NewGuid());

        Assert.That(result, Is.True);
    }
}
