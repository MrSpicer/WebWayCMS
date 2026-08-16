using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.CMSRoute;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class CMSRouteModelTests
{
    private ICMSRouteService _routeService = null!;
    private IMapper _mapper = null!;
    private CMSRouteModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _routeService = Substitute.For<ICMSRouteService>();
        _mapper = TestSupport.CreateMapper();
        _model = new CMSRouteModel(_routeService, _mapper);
    }

    [Test]
    public void Constructor_NullRouteService_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new CMSRouteModel(null!, _mapper));
        Assert.That(ex!.ParamName, Is.EqualTo("routeService"));
    }

    [Test]
    public void Constructor_NullMapper_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new CMSRouteModel(_routeService, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("mapper"));
    }

    [Test]
    public void ContentType_ReturnsCmsroutes()
    {
        Assert.That(_model.ContentType, Is.EqualTo("cmsroutes"));
    }

    [Test]
    public void DisplayName_ReturnsCMSRoute()
    {
        Assert.That(_model.DisplayName, Is.EqualTo("CMS Route"));
    }

    [Test]
    public void SupportsVersionHistory_ReturnsFalse()
    {
        Assert.That(_model.SupportsVersionHistory, Is.False);
    }

    [Test]
    public void SupportsPublishing_ReturnsFalse()
    {
        Assert.That(_model.SupportsPublishing, Is.False);
    }

    [Test]
    public void WriteRoles_IsNull()
    {
        Assert.That(_model.WriteRoles, Is.Null);
    }

    [Test]
    public void RegistryHandlerAndChildHandler_AreNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.RegistryHandler, Is.Null);
            Assert.That(_model.ChildHandler, Is.Null);
        });
    }

    [Test]
    public async Task GetSecondaryApiListAsync_ReturnsEmpty()
    {
        Assert.That(await _model.GetSecondaryApiListAsync("k"), Is.Empty);
    }

    [Test]
    public void HasSecondaryApiList_ReturnsFalse()
    {
        Assert.That(_model.HasSecondaryApiList, Is.False);
    }

    [Test]
    public void CreateEmptyUpsertViewModel_ReturnsNewInstance()
    {
        var vm = _model.CreateEmptyUpsertViewModel();
        Assert.That(vm, Is.InstanceOf<CMSRouteUpsertViewModel>());
    }

    [Test]
    public async Task GetRouteIndexAsync_MapsIsReserved()
    {
        var dto = new CMSRouteDTO
        {
            Id = Guid.NewGuid(),
            Pattern = "/test",
            OwningContentType = "Page",
            IsReserved = true
        };

        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { dto });

        var result = await _model.GetRouteIndexAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Routes, Has.Count.EqualTo(1));
            Assert.That(result.Routes[0].IsReserved, Is.True);
            Assert.That(result.Routes[0].Pattern, Is.EqualTo("/test"));
            Assert.That(result.Routes[0].OwningContentType, Is.EqualTo("Page"));
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_ReturnsIndexViewModel()
    {
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.GetIndexViewModelAsync();

        Assert.That(result, Is.InstanceOf<CMSRouteIndexViewModel>());
    }

    [Test]
    public void IndexViewPath_ReturnsCorrectPath()
    {
        Assert.That(_model.IndexViewPath, Is.EqualTo("~/Views/AdminCMSRoute/CMSRoutes.cshtml"));
    }

    [Test]
    public void UpsertViewPath_ReturnsCorrectPath()
    {
        Assert.That(_model.UpsertViewPath, Is.EqualTo("~/Views/AdminCMSRoute/CMSRouteUpsert.cshtml"));
    }

    [Test]
    public async Task GetRouteUpsertAsync_NullOrEmptyId_ReturnsNewViewModel()
    {
        var result = await _model.GetRouteUpsertAsync(null);
        Assert.That(result, Is.InstanceOf<CMSRouteUpsertViewModel>());

        var result2 = await _model.GetRouteUpsertAsync(Guid.Empty);
        Assert.That(result2, Is.InstanceOf<CMSRouteUpsertViewModel>());
    }

    [Test]
    public async Task GetRouteUpsertAsync_ExistingId_ReturnsMappedViewModel()
    {
        var id = Guid.NewGuid();
        var dto = new CMSRouteDTO
        {
            Id = id,
            Pattern = "/test",
            IsReserved = true
        };

        _routeService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _model.GetRouteUpsertAsync(id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsReserved, Is.True);
        Assert.That(result.Pattern, Is.EqualTo("/test"));
    }

    [Test]
    public async Task GetRouteUpsertAsync_NotFound_ReturnsNull()
    {
        _routeService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CMSRouteDTO?)null);

        var result = await _model.GetRouteUpsertAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetUpsertViewModelAsync_DelegatesToGetRouteUpsertAsync()
    {
        _routeService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CMSRouteDTO?)null);

        var result = await _model.GetUpsertViewModelAsync(Guid.NewGuid(), null!);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SaveRouteUpsertAsync_DuplicatePattern_ReturnsError()
    {
        var vm = new CMSRouteUpsertViewModel
        {
            Pattern = "/test"
        };

        _routeService.IsPatternAvailableAsync("/test", Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);

        var (success, error) = await _model.SaveRouteUpsertAsync(vm);

        Assert.That(success, Is.False);
        Assert.That(error, Is.EqualTo("This route pattern is already in use."));
    }

    [Test]
    public async Task SaveRouteUpsertAsync_AvailablePattern_CallsUpsertAndReturnsSuccess()
    {
        var vm = new CMSRouteUpsertViewModel
        {
            Pattern = "/test",
            IsReserved = true
        };

        _routeService.IsPatternAvailableAsync("/test", Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var (success, error) = await _model.SaveRouteUpsertAsync(vm);

        Assert.That(success, Is.True);
        Assert.That(error, Is.Null);
        await _routeService.Received(1).UpsertAsync(
            Arg.Is<CMSRouteDTO>(d => d.IsReserved == true && d.Pattern == "/test"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveRouteUpsertAsync_WithExistingId_ExcludesOwnRoute()
    {
        var id = Guid.NewGuid();
        var vm = new CMSRouteUpsertViewModel
        {
            Id = id,
            Pattern = "/test"
        };

        _routeService.IsPatternAvailableAsync("/test", Arg.Any<Guid?>(), id, Arg.Any<CancellationToken>()).Returns(true);
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var (success, _) = await _model.SaveRouteUpsertAsync(vm);

        Assert.That(success, Is.True);
    }

    [Test]
    public async Task SaveRouteUpsertAsync_NullViewModel_Throws()
    {
        var ex = Assert.ThrowsAsync<ArgumentNullException>(() => _model.SaveRouteUpsertAsync(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("model"));
    }

    [Test]
    public async Task SaveUpsertCoreAsync_DelegatesAndReturnsResult()
    {
        var vm = new CMSRouteUpsertViewModel { Pattern = "/test" };

        _routeService.IsPatternAvailableAsync("/test", Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _routeService.UpsertAsync(Arg.Any<CMSRouteDTO>(), Arg.Any<CancellationToken>())
            .Returns(x => (true, null, x.Arg<CMSRouteDTO>()));

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task SaveUpsertCoreAsync_DuplicatePattern_ReturnsErrorResult()
    {
        var vm = new CMSRouteUpsertViewModel { Pattern = "/test" };

        _routeService.IsPatternAvailableAsync("/test", Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _model.SaveUpsertAsync(vm);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("This route pattern is already in use."));
    }

    [Test]
    public async Task SaveUpsertAsync_BlankPattern_ReturnsValidationErrorWithoutSaving()
    {
        var vm = new CMSRouteUpsertViewModel { Pattern = "" };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Pattern"));
        });
        await _routeService.DidNotReceive().IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteRouteAsync_DelegatesToService()
    {
        var id = Guid.NewGuid();
        _routeService.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteRouteAsync(id);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteAsync_DelegatesToDeleteRouteAsync()
    {
        var id = Guid.NewGuid();
        _routeService.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _model.DeleteAsync(id);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GetApiListAsync_ReturnsRoutesAsObjects()
    {
        _routeService.GetActiveRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new() { Id = Guid.NewGuid(), Pattern = "/test" }
        });

        var result = await _model.GetApiListAsync();

        Assert.That(result.Count(), Is.EqualTo(1));
    }
}
