using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageModelTests
{
    private IPageService _service = null!;
    private IMapper _mapper = null!;
    private IPageControllerRegistry _registry = null!;
    private IViewDiscoveryService _viewDiscovery = null!;
    private IRouteRegistrationService _routeRegistration = null!;
    private ICMSRouteService _cmsRouteService = null!;
    private PageModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IPageService>();
        _mapper = TestSupport.CreateMapper();
        _registry = Substitute.For<IPageControllerRegistry>();
        _viewDiscovery = Substitute.For<IViewDiscoveryService>();
        _routeRegistration = Substitute.For<IRouteRegistrationService>();
        _cmsRouteService = Substitute.For<ICMSRouteService>();
        _model = new PageModel(_service, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService);
    }

    private static PageDTO Page(string title = "T", bool published = true, bool hidden = false,
        Guid? masterId = null)
    {
        var id = Guid.NewGuid();
        return new PageDTO
        {
            ContentId = id,
            ContentMeta = new ContentDTO
            {
                Id = id,
                MasterId = masterId ?? Guid.NewGuid(),
                Title = title,
                IsPublished = published,
                IsHidden = hidden
            }
        };
    }

    private static CMSRouteDTO RouteFor(Guid contentMasterId, string pattern)
    {
        var id = Guid.NewGuid();
        return new CMSRouteDTO
        {
            ContentId = id,
            Pattern = pattern,
            OwningContentMasterId = contentMasterId,
            OwningContentType = "Page",
            ContentMeta = new ContentDTO { Id = id, MasterId = contentMasterId, IsPublished = true }
        };
    }

    private static IQueryCollection Query(params (string, string)[] pairs) =>
        new QueryCollection(pairs.ToDictionary(p => p.Item1, p => new Microsoft.Extensions.Primitives.StringValues(p.Item2)));

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new PageModel(null!, _mapper, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_service, null!, _registry, _viewDiscovery, _routeRegistration, _cmsRouteService), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_service, _mapper, null!, _viewDiscovery, _routeRegistration, _cmsRouteService), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_service, _mapper, _registry, null!, _routeRegistration, _cmsRouteService), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_service, _mapper, _registry, _viewDiscovery, null!, _cmsRouteService), Throws.ArgumentNullException);
            Assert.That(() => new PageModel(_service, _mapper, _registry, _viewDiscovery, _routeRegistration, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("pages"));
            Assert.That(_model.DisplayName, Is.EqualTo("Page"));
            Assert.That(_model.IndexViewPath, Does.Contain("Pages.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("PageUpsert.cshtml"));
            Assert.That(_model.RegistryHandler, Is.Not.Null);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_BuildsTreeWithRootIntermediateAndLeaf()
    {
        var page1 = Page(title: "Home");
        var page2 = Page(title: "Leaf");
        var page3 = Page(title: "Branch");

        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page1, page2, page3 });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page1.ContentMeta.MasterId, "/"),
            RouteFor(page2.ContentMeta.MasterId, "/a/b"),
            RouteFor(page3.ContentMeta.MasterId, "/a"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Pages.Any(p => p.Path == "/"), Is.True);
            var branch = vm.Pages.First(p => p.Path == "/a");
            Assert.That(branch.Children.Any(c => c.Path == "/a/b"), Is.True);
        });
    }

    [Test]
    public async Task GetPageIndexAsync_DuplicateRootUpdatesExistingNode()
    {
        var first = Page(title: "First");
        var second = Page(title: "Second");

        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { first, second });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(first.ContentMeta.MasterId, "/"),
            RouteFor(second.ContentMeta.MasterId, "/"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single(p => p.Path == "/").Title, Is.EqualTo("Second"));
    }

    [Test]
    public async Task GetPageIndexAsync_IntermediateThenLeafForSameSegmentUpdatesNode()
    {
        var page1 = Page(title: "Deep");
        var page2 = Page(title: "RealA");

        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page1, page2 });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page1.ContentMeta.MasterId, "/a/b"),
            RouteFor(page2.ContentMeta.MasterId, "/a"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages.Single(p => p.Path == "/a").Title, Is.EqualTo("RealA"));
    }

    [Test]
    public async Task GetPageIndexAsync_DeepPath_CreatesIntermediateNonLeafNodes()
    {
        var page = Page(title: "Deep");

        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(page.ContentMeta.MasterId, "/x/y/z"),
        });

        var vm = await _model.GetPageIndexAsync();
        var x = vm.Pages.Single(p => p.Path == "/x");
        var y = x.Children.Single(c => c.Path == "/x/y");

        Assert.Multiple(() =>
        {
            Assert.That(x.PageId, Is.Null, "intermediate node has no page id");
            Assert.That(y.Children.Single().Path, Is.EqualTo("/x/y/z"));
            Assert.That(y.Children.Single().Title, Is.EqualTo("Deep"));
        });
    }

    [Test]
    public async Task GetPageUpsertAsync_NullId_FoundAndNotFound()
    {
        var page = Page();
        _service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
        _service.GetByIdAsync(Arg.Is<Guid>(g => g != page.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetPageUpsertAsync(null), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(Guid.Empty), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(page.ContentMeta.Id), Is.Not.Null);
            Assert.That(await _model.GetPageUpsertAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void SavePageUpsertAsync_NullModel_Throws()
    {
        Assert.That(async () => await _model.SavePageUpsertAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task SavePageUpsertAsync_CreateAndUpdate()
    {
        var savedDto = new PageDTO { ContentId = Guid.NewGuid(), ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), IsPublished = false } };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.UpdateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = null, Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.True);
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.True);
            Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Slug = "r", ControllerName = "C" })).Success, Is.False);
        });
    }

    [Test]
    public async Task DeletePageAsync_DelegatesAndUnregistersRoute()
    {
        var page = Page();
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(page);
        _service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeletePageAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task VersionHistory_RestoreAndDeleteVersion()
    {
        var master = Guid.NewGuid();
        _service.GetAllVersionsAsync(master, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { Page() });
        Assert.That(await _model.GetVersionHistoryAsync(master), Is.Not.Null);

        var historical = Page();
        _service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
        var latest = Page(masterId: historical.ContentMeta.MasterId);
        _service.GetAllVersionsAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { latest });
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        Assert.That((await _model.GetPageUpsertForRestoreAsync(historical.ContentMeta.Id))!.Id, Is.EqualTo(latest.ContentMeta.Id));

        _service.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await _model.DeletePageVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task GetPageUpsertForRestore_NullWhenHistoricalOrLatestMissing()
    {
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        Assert.That(await _model.GetPageUpsertForRestoreAsync(Guid.NewGuid()), Is.Null);

        var historical = Page();
        _service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
        _service.GetAllVersionsAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
        Assert.That(await _model.GetPageUpsertForRestoreAsync(historical.ContentMeta.Id), Is.Null);
    }

    [Test]
    public async Task AdminHandler_UpsertViewModel_EditFoundMissingAndCreateWithParentRoute()
    {
        var page = Page();
        _service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
        _service.GetByIdAsync(Arg.Is<Guid>(g => g != page.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertViewModelAsync(page.ContentMeta.Id, Query()), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), Query()), Is.Null);
            var withParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "blog/"))))!;
            Assert.That(withParent.ParentRoutePrefix, Is.EqualTo("/blog"));
            var rootParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/"))))!;
            Assert.That(rootParent.ParentRoutePrefix, Is.EqualTo("/"));
            var plain = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query()))!;
            Assert.That(plain.ParentRoutePrefix, Is.Null);
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_RouteConflictAndSuccess()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false, true);
        _registry.GetByName(Arg.Any<string>()).Returns((PageControllerInfo?)null);
        var savedDto = new PageDTO { ContentId = Guid.NewGuid(), ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), IsPublished = false } };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var conflict = await _model.SaveUpsertAsync(new PageUpsertViewModel { Title = "T", Slug = "x", ControllerName = "C", MasterId = Guid.NewGuid() });
        var ok = await _model.SaveUpsertAsync(new PageUpsertViewModel { Id = null, Title = "T", Slug = "y", ControllerName = "C" });

        Assert.Multiple(() =>
        {
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.ErrorField, Is.EqualTo("Slug"));
            Assert.That(ok.Success, Is.True);
        });
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_SaveFailureSurfaces()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _registry.GetByName(Arg.Any<string>()).Returns((PageControllerInfo?)null);
        _service.UpdateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Slug = "x", ControllerName = "C" });

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_ConfigValidationFailure_ReturnsError()
    {
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _registry.ValidateConfiguration("C", Arg.Any<object>()).Returns(new List<string> { "Custom CSS is required." });

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "x",
            ControllerName = "C",
            ConfigurationJson = "{}"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ConfigurationJson"));
            Assert.That(result.ErrorMessage, Does.Contain("Custom CSS is required."));
        });
    }

    [Test]
    public async Task AdminHandler_IndexCreateEmptyDeleteApiRestoreAndDeleteVersion()
    {
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());
        _service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<PageIndexViewModel>());
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<PageUpsertViewModel>());
            Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
            Assert.That(await _model.GetApiListAsync(), Is.Empty);
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
        });
    }

    // --- PageRegistryHandler ---

    [Test]
    public void RegistryHandler_GetAll_ReturnsJson()
    {
        _registry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Generic", DisplayName = "Generic", Description = "d", Category = "General" }
        });

        var result = _model.RegistryHandler!.GetAll();

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_EmptyName_BadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetProperties(" "), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_NotFound()
    {
        _registry.GetByName("X").Returns((PageControllerInfo?)null);

        Assert.That(_model.RegistryHandler!.GetProperties("X"), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetProperties_ReturnsJsonWithViews()
    {
        _registry.GetByName("Generic").Returns(new PageControllerInfo
        {
            Name = "Generic",
            DisplayName = "Generic",
            Category = "General",
            Properties = new List<WebWayCMS.Forms.FormPropertyInfo>
            {
                new() { Name = "ViewName", Label = "View", Order = 1 }
            }
        });
        _viewDiscovery.GetControllerViews("Generic").Returns(new[] { "Default" });

        var result = _model.RegistryHandler!.GetProperties("Generic");

        Assert.That(result, Is.InstanceOf<JsonResult>());
    }

    [Test]
    public void RoutableContent_RouteContentType_ReturnsPage()
    {
        IRoutableContent routable = _model;
        Assert.That(routable.RouteContentType, Is.EqualTo("Page"));
    }

    [Test]
    public async Task RoutableContent_GetRoutesAsync_ReturnsRoutes()
    {
        var masterId = Guid.NewGuid();
        _cmsRouteService.GetByOwningContentAsync(masterId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO> { new() { Pattern = "/test" } });

        IRoutableContent routable = _model;
        var routes = await routable.GetRoutesAsync(masterId, CancellationToken.None);

        Assert.That(routes, Has.Count.EqualTo(1));
        Assert.That(routes[0].Pattern, Is.EqualTo("/test"));
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_PageWithoutRouteIsExcluded()
    {
        var pageWithRoute = Page(title: "HasRoute");
        var pageWithoutRoute = Page(title: "NoRoute");

        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { pageWithRoute, pageWithoutRoute });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(pageWithRoute.ContentMeta.MasterId, "/hasroute"),
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages, Has.Count.EqualTo(1));
        Assert.That(vm.Pages[0].Title, Is.EqualTo("HasRoute"));
    }

    [Test]
    public async Task GetPageUpsertAsync_WithRouteDefaults_DeserializesControllerName()
    {
        var page = Page();
        _service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(page.ContentMeta.MasterId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new()
                {
                    Pattern = "/test",
                    OwningContentMasterId = page.ContentMeta.MasterId,
                    DefaultsJson = "{\"controller\":\"MyController\",\"action\":\"Index\"}"
                }
            });

        var vm = await _model.GetPageUpsertAsync(page.ContentMeta.Id);

        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.ControllerName, Is.EqualTo("MyController"));
    }

    [Test]
    public async Task GetPageUpsertAsync_WithInvalidDefaultsJson_DoesNotSetController()
    {
        var page = Page();
        _service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(page.ContentMeta.MasterId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new()
                {
                    Pattern = "/test",
                    OwningContentMasterId = page.ContentMeta.MasterId,
                    DefaultsJson = "{invalid"
                }
            });

        var vm = await _model.GetPageUpsertAsync(page.ContentMeta.Id);

        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.ControllerName, Is.Null.Or.Empty);
    }

    [Test]
    public async Task GetPageUpsertAsync_WithEmptyDefaultsJson_DoesNotSetController()
    {
        var page = Page();
        _service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(page.ContentMeta.MasterId, Arg.Any<CancellationToken>())
            .Returns(new List<CMSRouteDTO>
            {
                new()
                {
                    Pattern = "/test",
                    OwningContentMasterId = page.ContentMeta.MasterId,
                    DefaultsJson = "{}"
                }
            });

        var vm = await _model.GetPageUpsertAsync(page.ContentMeta.Id);

        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.ControllerName, Is.Null.Or.Empty);
    }

    [Test]
    public async Task SavePageUpsertAsync_WithConfigJson_TryDeserializeConfig()
    {
        var controllerInfo = new PageControllerInfo
        {
            Name = "C",
            DisplayName = "C",
            ConfigurationType = typeof(SampleSaveConfig)
        };
        _registry.GetByName("C").Returns(controllerInfo);

        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = true
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "test",
            ControllerName = "C",
            ConfigurationJson = "{\"Value\":42}"
        });

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SavePageUpsertAsync_WithInvalidConfigJson_TryDeserializeConfigCatches()
    {
        var controllerInfo = new PageControllerInfo
        {
            Name = "C",
            DisplayName = "C",
            ConfigurationType = typeof(SampleSaveConfig)
        };
        _registry.GetByName("C").Returns(controllerInfo);

        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = true
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "test",
            ControllerName = "C",
            ConfigurationJson = "{invalid"
        });

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SavePageUpsertAsync_WithEmptyConfigJson_TryDeserializeConfigReturnsNull()
    {
        var controllerInfo = new PageControllerInfo
        {
            Name = "C",
            DisplayName = "C",
            ConfigurationType = typeof(SampleSaveConfig)
        };
        _registry.GetByName("C").Returns(controllerInfo);

        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = true
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "test",
            ControllerName = "C",
            ConfigurationJson = "{}"
        });

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SavePageUpsertAsync_HomeSlug_DeriveRoutePatternToRoot()
    {
        var controllerInfo = new PageControllerInfo
        {
            Name = "C",
            DisplayName = "C",
            ConfigurationType = typeof(SampleSaveConfig)
        };
        _registry.GetByName("C").Returns(controllerInfo);

        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = true
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "Home",
            Slug = "home",
            ControllerName = "C",
            ConfigurationJson = "{\"Value\":42}"
        });

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            Arg.Any<IRoutableContent>(),
            "/",
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavePageUpsertAsync_NotPublished_RegistersRoutesWithIsPublishedFalse()
    {
        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = false
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "draft",
            ControllerName = "C"
        });

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            Arg.Any<IRoutableContent>(),
            "/draft",
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavePageUpsertAsync_WithParentRoutePrefix_DerivesCorrectPattern()
    {
        var savedDto = new PageDTO
        {
            ContentId = Guid.NewGuid(),
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = Guid.NewGuid(),
                IsPublished = true
            }
        };
        _service.CreateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(savedDto);
        _cmsRouteService.GetByOwningContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>());

        var result = await _model.SavePageUpsertAsync(new PageUpsertViewModel
        {
            Id = null,
            Title = "T",
            Slug = "child",
            ControllerName = "C",
            ParentRoutePrefix = "/blog"
        });

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            Arg.Any<IRoutableContent>(),
            "/blog/child",
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPageIndexAsync_BuildTree_DifferentContentTypeRoutesAreIgnored()
    {
        var page = Page(title: "T");
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            new()
            {
                Pattern = "/test",
                OwningContentMasterId = page.ContentMeta.MasterId,
                OwningContentType = "Widget",
                ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = page.ContentMeta.MasterId }
            }
        });

        var vm = await _model.GetPageIndexAsync();

        Assert.That(vm.Pages, Is.Empty);
    }

    // --- PageRegistryHandler GetForm ---

    [Test]
    public void RegistryHandler_GetForm_EmptyName_ReturnsBadRequest()
    {
        Assert.That(_model.RegistryHandler!.GetForm("  ", null), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NotFound_ReturnsNotFound()
    {
        _registry.GetByName("X").Returns((PageControllerInfo?)null);

        Assert.That(_model.RegistryHandler!.GetForm("X", null), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RegistryHandler_GetForm_NoConfigType_ReturnsPartialViewWithNullModel()
    {
        _registry.GetByName("Plain").Returns(new PageControllerInfo
        {
            Name = "Plain",
            ConfigurationType = null
        });

        var result = _model.RegistryHandler!.GetForm("Plain", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.Null);
    }

    [Test]
    public void RegistryHandler_GetForm_WithConfigType_ReturnsPartialViewWithInstance()
    {
        _registry.GetByName("Typed").Returns(new PageControllerInfo
        {
            Name = "Typed",
            ConfigurationType = typeof(SampleSaveConfig)
        });

        var result = _model.RegistryHandler!.GetForm("Typed", null);

        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(((PartialViewResult)result).ViewData!.Model, Is.TypeOf<SampleSaveConfig>());
    }

    public async Task GetPageIndexAsync_BuildTree_IntermediateNodeWithoutPage_NullPageId()
    {
        var deepPage = Page(title: "Deep");
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { deepPage });
        _cmsRouteService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO>
        {
            RouteFor(deepPage.ContentMeta.MasterId, "/a/b"),
        });

        var vm = await _model.GetPageIndexAsync();
        var a = vm.Pages.Single(p => p.Path == "/a");

        Assert.That(a.PageId, Is.Null);
    }

    [Test]
    public async Task AdminHandler_SaveUpsert_ExistingRoutePreservesPrefix()
    {
        var page = Page();
        var existingRoute = new CMSRouteDTO
        {
            Pattern = "/blog/old-slug",
            OwningContentMasterId = page.ContentMeta.MasterId,
            ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = page.ContentMeta.MasterId }
        };
        _cmsRouteService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.UpdateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(true);
        _service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(page);
        _cmsRouteService.GetByOwningContentAsync(page.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { existingRoute });

        var result = await _model.SaveUpsertAsync(new PageUpsertViewModel
        {
            Id = page.ContentMeta.Id,
            MasterId = page.ContentMeta.MasterId,
            Title = "T",
            Slug = "new-slug",
            ControllerName = "C"
        });

        Assert.That(result.Success, Is.True);
        await _routeRegistration.Received(1).RegisterContentRoutesAsync(
            Arg.Any<IRoutableContent>(),
            "/blog/new-slug",
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }
}

public class SampleSaveConfig
{
    public int Value { get; set; }
}
