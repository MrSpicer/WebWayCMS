using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Route;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RouteModelTests
{
    private IContentStore<RouteDTO> _store = null!;
    private ICMSRouteService _routeService = null!;
    private RouteModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<RouteDTO>>();
        _routeService = Substitute.For<ICMSRouteService>();
        _model = new RouteModel(_store, _routeService, TestSupport.CreateMapper(),
            Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());
    }

    private static RouteDTO Route(string pattern = "/x", RouteMode mode = RouteMode.Explicit, Guid? nodeId = null)
        => new()
        {
            Mode = mode,
            Pattern = pattern,
            NavigationName = "Nav",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nodeId ?? Guid.NewGuid() },
                Title = "Nav",
                VersionNumber = 1,
                State = ContentVersionState.Draft
            }
        };

    [Test]
    public void Constructor_Null_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new RouteModel(null!, _routeService, TestSupport.CreateMapper(), Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new RouteModel(_store, null!, TestSupport.CreateMapper(), Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new RouteModel(_store, _routeService, null!, Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_IsARouteContentType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("routes"));
            Assert.That(_model.DisplayName, Is.EqualTo("Route"));
            Assert.That(_model.SupportsPublishing, Is.False);
            Assert.That(_model.WriteRoles, Is.EquivalentTo(new[] { "Admin", "Editor" }));
        });
    }

    [Test]
    public async Task IndexUpsertAndApiList_ReadTheStore()
    {
        var route = Route();
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<RouteDTO> { route });
        _store.GetCurrentDraftAsync(route.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(route);

        var index = (IEnumerable<RouteDTO>)await _model.GetIndexViewModelAsync();
        var edit = (RouteUpsertViewModel?)await _model.GetUpsertViewModelAsync(route.Version.Node.Id, new QueryCollection());
        var missing = await _model.GetUpsertViewModelAsync(Guid.NewGuid(), new QueryCollection());
        var create = (RouteUpsertViewModel)await _model.GetUpsertViewModelAsync(null, new QueryCollection());
        var api = (IEnumerable<object>)await _model.GetApiListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(index.Single(), Is.SameAs(route));
            Assert.That(edit!.Pattern, Is.EqualTo("/x"));
            Assert.That(missing, Is.Null);
            Assert.That(create.Pattern, Is.Empty);
            Assert.That(api.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SaveExplicit_ValidatesAndSaves()
    {
        var nodeId = Guid.NewGuid();
        _routeService.IsPatternAvailableAsync(Arg.Any<string>(), ct: Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: nodeId));

        var result = await _model.SaveUpsertAsync(new RouteUpsertViewModel { Mode = RouteMode.Explicit, Pattern = "/Docs/" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.NodeId, Is.EqualTo(nodeId));
        });
    }

    [Test]
    public async Task SaveExplicit_UnchangedPatternOfAProjectedRoute_ExcludesItsOwnRow()
    {
        var routeNode = Guid.NewGuid();
        var ownRow = new CMSRouteDTO { Id = Guid.NewGuid(), Pattern = "/docs", RouteNodeId = routeNode, OwningContentNodeId = Guid.NewGuid() };
        var otherRow = new CMSRouteDTO { Id = Guid.NewGuid(), Pattern = "/other", RouteNodeId = Guid.NewGuid(), OwningContentNodeId = Guid.NewGuid() };
        _routeService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns(new List<CMSRouteDTO> { otherRow, ownRow });
        _routeService.IsPatternAvailableAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);
        _routeService.IsPatternAvailableAsync("/docs", null, ownRow.Id, Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: routeNode));

        var own = await _model.SaveUpsertAsync(new RouteUpsertViewModel { NodeId = routeNode, Mode = RouteMode.Explicit, Pattern = "/docs" });
        var unprojected = await _model.SaveUpsertAsync(new RouteUpsertViewModel { NodeId = Guid.NewGuid(), Mode = RouteMode.Explicit, Pattern = "/docs" });

        Assert.Multiple(() =>
        {
            Assert.That(own.Success, Is.True);
            Assert.That(unprojected.ErrorMessage, Does.Contain("already in use"), "another route's row still collides");
        });
    }

    [Test]
    public async Task SaveExplicit_RouteListUnavailable_ChecksWithoutExclusion()
    {
        _routeService.GetAllRoutesAsync(Arg.Any<CancellationToken>()).Returns((List<CMSRouteDTO>)null!);
        _routeService.IsPatternAvailableAsync("/docs", null, null, Arg.Any<CancellationToken>()).Returns(true);
        _store.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));

        var result = await _model.SaveUpsertAsync(new RouteUpsertViewModel { NodeId = Guid.NewGuid(), Mode = RouteMode.Explicit, Pattern = "/docs" });

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveExplicit_ReservedOrTakenOrSaveFailure_ReturnsErrors()
    {
        _routeService.IsPatternAvailableAsync(Arg.Any<string>(), ct: Arg.Any<CancellationToken>()).Returns(false);
        _store.SaveDraftAsync(Arg.Any<RouteDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "stale"));

        var reserved = await _model.SaveUpsertAsync(new RouteUpsertViewModel { Mode = RouteMode.Explicit, Pattern = "/{action}" });
        var taken = await _model.SaveUpsertAsync(new RouteUpsertViewModel { Mode = RouteMode.Explicit, Pattern = "/taken" });

        _routeService.IsPatternAvailableAsync(Arg.Any<string>(), ct: Arg.Any<CancellationToken>()).Returns(true);
        var failed = await _model.SaveUpsertAsync(new RouteUpsertViewModel { Mode = RouteMode.Explicit, Pattern = "/ok" });

        Assert.Multiple(() =>
        {
            Assert.That(reserved.Success, Is.False);
            Assert.That(reserved.ErrorField, Is.EqualTo("Pattern"));
            Assert.That(taken.ErrorMessage, Does.Contain("already in use"));
            Assert.That(failed.Success, Is.False);
            Assert.That(failed.ErrorMessage, Is.EqualTo("stale"));
        });
    }

    [Test]
    public async Task VersionHistoryPathsAndStore_AreWired()
    {
        var route = Route(nodeId: Guid.NewGuid());
        _store.GetAllVersionsAsync(route.Version.Node.Id, Arg.Any<CancellationToken>())
            .Returns(new List<RouteDTO> { route });
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, VersionId: Guid.NewGuid()));

        var history = await _model.GetVersionHistoryViewModelAsync(route.Version.Node.Id);
        var deleted = await _model.DeleteVersionAsync(Guid.NewGuid());
        var published = await _model.PublishAsync(route.Version.Node.Id);

        Assert.Multiple(() =>
        {
            Assert.That(_model.IndexViewPath, Is.EqualTo("~/Views/AdminRoute/Routes.cshtml"));
            Assert.That(_model.UpsertViewPath, Is.EqualTo("~/Views/AdminRoute/RouteUpsert.cshtml"));
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<RouteUpsertViewModel>());
            Assert.That(history, Is.Not.Null);
            Assert.That(deleted, Is.True);
            Assert.That(published.Success, Is.True);
        });
    }

    [Test]
    public async Task Delete_RemovesProjectionThenTheItem()
    {
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        var deleted = await _model.DeleteAsync(Guid.NewGuid());

        Assert.That(deleted, Is.True);
        await _routeService.Received(1).DeleteByRouteNodeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
