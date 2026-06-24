using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class PageModelTests
{
	private IPageService _service = null!;
	private IMapper _mapper = null!;
	private IPageControllerRegistry _registry = null!;
	private PageModel _model = null!;

	[SetUp]
	public void SetUp()
	{
		_service = Substitute.For<IPageService>();
		_mapper = TestSupport.CreateMapper();
		_registry = Substitute.For<IPageControllerRegistry>();
		_model = new PageModel(_service, _mapper, _registry);
	}

	private static PageDTO Page(string route, string title = "T", bool published = true, bool hidden = false)
	{
		var id = Guid.NewGuid();
		return new PageDTO
		{
			ContentId = id,
			Route = route,
			ControllerName = "Generic",
			ContentMeta = new ContentDTO { Id = id, MasterId = Guid.NewGuid(), Title = title, IsPublished = published, IsHidden = hidden }
		};
	}

	private static IQueryCollection Query(params (string, string)[] pairs) =>
		new QueryCollection(pairs.ToDictionary(p => p.Item1, p => new Microsoft.Extensions.Primitives.StringValues(p.Item2)));

	[Test]
	public void Constructor_NullArguments_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new PageModel(null!, _mapper, _registry), Throws.ArgumentNullException);
			Assert.That(() => new PageModel(_service, null!, _registry), Throws.ArgumentNullException);
			Assert.That(() => new PageModel(_service, _mapper, null!), Throws.ArgumentNullException);
		});
	}

	[Test]
	public void Metadata()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_model.ContentType, Is.EqualTo("pages"));
			Assert.That(_model.DisplayName, Is.EqualTo("Page"));
			Assert.That(_model.RegistryHandler, Is.Not.Null);
		});
	}

	[Test]
	public async Task GetByRouteAsync_Delegates()
	{
		var page = Page("/x");
		_service.GetByRouteAsync("/x", Arg.Any<CancellationToken>()).Returns(page);

		Assert.That(await _model.GetByRouteAsync("/x"), Is.SameAs(page));
	}

	[Test]
	public async Task GetPageIndexAsync_BuildsTreeWithRootIntermediateAndLeaf()
	{
		// "/" root, "/a/b" creates intermediate "/a" + leaf "/a/b", and a duplicate root update.
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>
		{
			Page("/"),
			Page("/a/b", title: "Leaf"),
			Page("/a", title: "Branch"),
		});

		var vm = await _model.GetPageIndexAsync();

		Assert.Multiple(() =>
		{
			Assert.That(vm.Pages.Any(p => p.Route == "/"), Is.True);
			var branch = vm.Pages.First(p => p.Route == "/a");
			Assert.That(branch.Children.Any(c => c.Route == "/a/b"), Is.True);
		});
	}

	[Test]
	public async Task GetPageIndexAsync_DuplicateRootUpdatesExistingNode()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>
		{
			Page("/", title: "First"),
			Page("/", title: "Second"),
		});

		var vm = await _model.GetPageIndexAsync();

		Assert.That(vm.Pages.Single(p => p.Route == "/").Title, Is.EqualTo("Second"));
	}

	[Test]
	public async Task GetPageIndexAsync_IntermediateThenLeafForSameSegmentUpdatesNode()
	{
		// "/a" appears as intermediate first (via /a/b) then as its own leaf page.
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>
		{
			Page("/a/b"),
			Page("/a", title: "RealA"),
		});

		var vm = await _model.GetPageIndexAsync();

		Assert.That(vm.Pages.Single(p => p.Route == "/a").Title, Is.EqualTo("RealA"));
	}

	[Test]
	public async Task GetPageIndexAsync_DeepPath_CreatesIntermediateNonLeafNodes()
	{
		// No "/x" or "/x/y" pages exist, so they are created as intermediate (non-leaf) nodes.
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { Page("/x/y/z", title: "Deep") });

		var vm = await _model.GetPageIndexAsync();
		var x = vm.Pages.Single(p => p.Route == "/x");
		var y = x.Children.Single(c => c.Route == "/x/y");

		Assert.Multiple(() =>
		{
			Assert.That(x.PageId, Is.Null, "intermediate node has no page id");
			Assert.That(y.Children.Single().Route, Is.EqualTo("/x/y/z"));
			Assert.That(y.Children.Single().Title, Is.EqualTo("Deep"));
		});
	}

	[Test]
	public async Task GetPageIndexAsync_DuplicateLeafRoute_UpdatesExistingLeaf()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>
		{
			Page("/dup", title: "First"),
			Page("/dup", title: "Second"),
		});

		var vm = await _model.GetPageIndexAsync();

		Assert.That(vm.Pages.Single(p => p.Route == "/dup").Title, Is.EqualTo("Second"));
	}

	[Test]
	public async Task GetPageUpsertAsync_NullId_FoundAndNotFound()
	{
		var page = Page("/x");
		_service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != page.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

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
	public async Task SavePageUpsertAsync_CreateUpdateSuccessAndFailure()
	{
		_service.UpdateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

		Assert.Multiple(async () =>
		{
			Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = null, Title = "T", Route = "/r", ControllerName = "C" })).Success, Is.True);
			Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Route = "/r", ControllerName = "C" })).Success, Is.True);
			Assert.That((await _model.SavePageUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Route = "/r", ControllerName = "C" })).Success, Is.False);
		});
	}

	[Test]
	public async Task DeletePageAsync_And_IsRouteAvailable_Delegate()
	{
		_service.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
		_service.IsRouteAvailableAsync("/x", null, Arg.Any<CancellationToken>()).Returns(true);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.DeletePageAsync(Guid.NewGuid()), Is.True);
			Assert.That(await _model.IsRouteAvailableAsync("/x"), Is.True);
		});
	}

	[Test]
	public async Task VersionHistory_RestoreAndDeleteVersion()
	{
		var master = Guid.NewGuid();
		_service.GetAllVersionsAsync(master, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { Page("/x") });
		Assert.That(await _model.GetVersionHistoryAsync(master), Is.Not.Null);

		var historical = Page("/x");
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		var latest = Page("/x");
		_service.GetAllVersionsAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { latest });
		Assert.That((await _model.GetPageUpsertForRestoreAsync(historical.ContentMeta.Id))!.Id, Is.EqualTo(latest.ContentMeta.Id));

		_service.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
		Assert.That(await _model.DeletePageVersionAsync(Guid.NewGuid()), Is.True);
	}

	[Test]
	public async Task GetPageUpsertForRestore_NullWhenHistoricalOrLatestMissing()
	{
		_service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);
		Assert.That(await _model.GetPageUpsertForRestoreAsync(Guid.NewGuid()), Is.Null);

		var historical = Page("/x");
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		_service.GetAllVersionsAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
		Assert.That(await _model.GetPageUpsertForRestoreAsync(historical.ContentMeta.Id), Is.Null);
	}

	[Test]
	public async Task AdminHandler_UpsertViewModel_EditFoundMissingAndCreateWithParentRoute()
	{
		var page = Page("/x");
		_service.GetByIdAsync(page.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(page);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != page.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetUpsertViewModelAsync(page.ContentMeta.Id, Query()), Is.Not.Null);
			Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), Query()), Is.Null);
			// create with parentRoute lacking slashes
			var withParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "blog/"))))!;
			Assert.That(withParent.Route, Is.EqualTo("/blog/"));
			// create with root parentRoute
			var rootParent = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query(("parentRoute", "/"))))!;
			Assert.That(rootParent.Route, Is.EqualTo("/"));
			// create with no parentRoute
			var plain = (PageUpsertViewModel)(await _model.GetUpsertViewModelAsync(null, Query()))!;
			Assert.That(plain.Route, Is.Empty);
		});
	}

	[Test]
	public async Task AdminHandler_SaveUpsert_RouteConflictAndSuccess()
	{
		_service.IsRouteAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false, true);

		var conflict = await _model.SaveUpsertAsync(new PageUpsertViewModel { Title = "T", Route = "/x", ControllerName = "C", MasterId = Guid.NewGuid() });
		var ok = await _model.SaveUpsertAsync(new PageUpsertViewModel { Id = null, Title = "T", Route = "/y", ControllerName = "C" });

		Assert.Multiple(() =>
		{
			Assert.That(conflict.Success, Is.False);
			Assert.That(conflict.ErrorField, Is.EqualTo("Route"));
			Assert.That(ok.Success, Is.True);
		});
	}

	[Test]
	public async Task AdminHandler_SaveUpsert_SaveFailureSurfaces()
	{
		_service.IsRouteAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(true);
		_service.UpdateAsync(Arg.Any<PageDTO>(), Arg.Any<CancellationToken>()).Returns(false);

		var result = await _model.SaveUpsertAsync(new PageUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Route = "/x", ControllerName = "C" });

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public async Task AdminHandler_IndexCreateEmptyDeleteApiRestoreAndDeleteVersion()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PageDTO>());
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

		var result = _model.RegistryHandler!.GetProperties("Generic");

		Assert.That(result, Is.InstanceOf<JsonResult>());
	}
}
