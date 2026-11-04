using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentZoneControllerPageZonesTests
{
    private IContentZoneModel _model = null!;
    private IContentStore<PageDTO> _pageStore = null!;
    private IPageHistoryService _pageHistory = null!;
    private AdminContentZoneController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentZoneModel>();
        _pageStore = Substitute.For<IContentStore<PageDTO>>();
        _pageHistory = Substitute.For<IPageHistoryService>();
        _controller = new AdminContentZoneController(_model, Substitute.For<IWidgetRegistry>(), _pageStore, _pageHistory);
        new MvcHarness().Configure(_controller, new[] { "Admin" });
    }

    private static PageDTO Page(Guid nodeId, ContentVersionState state, string title = "About")
        => new()
        {
            Version = new ContentVersion { Node = new ContentNode { Id = nodeId }, Title = title, State = state }
        };

    [Test]
    public async Task PageZones_MissingPage_ReturnsNotFound()
    {
        _pageStore.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageDTO?)null);

        Assert.That(await _controller.PageZones(Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task PageZones_PublishedPageWithZoneDrafts_ReadsDraftsAndReportsPending()
    {
        var pageId = Guid.NewGuid();
        var page = Page(pageId, ContentVersionState.Published);
        var slots = new List<PageZoneSlot> { new("Main", Guid.NewGuid()) };
        _pageStore.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _pageStore.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _pageHistory.GetPagesWithUnpublishedChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { pageId });
        _model.GetPageZoneSlotsAsync(pageId, Arg.Any<CancellationToken>()).Returns(slots);

        var view = (ViewResult)await _controller.PageZones(pageId, default);
        var vm = (PageZonesEditViewModel)view.Model!;

        Assert.Multiple(() =>
        {
            Assert.That(view.ViewName, Is.EqualTo("~/Views/AdminContentZone/PageZonesEdit.cshtml"));
            Assert.That(_controller.HttpContext.Items[WebWayCMS.PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
            Assert.That(vm.PageNodeId, Is.EqualTo(pageId));
            Assert.That(vm.PageTitle, Is.EqualTo("About"));
            Assert.That(vm.IsPublished, Is.True);
            Assert.That(vm.HasPendingChanges, Is.True);
            Assert.That(vm.HasPendingZoneChanges, Is.True);
            Assert.That(vm.Slots, Is.SameAs(slots));
        });
    }

    [Test]
    public async Task PageZones_PassesTheUnplacedCandidates()
    {
        var pageId = Guid.NewGuid();
        var page = Page(pageId, ContentVersionState.Published);
        var candidates = new List<UnplacedZoneCandidate> { new(Guid.NewGuid(), Guid.NewGuid(), "Layout › Column3") };
        _pageStore.GetCurrentDraftAsync(pageId, Arg.Any<CancellationToken>()).Returns(page);
        _pageStore.GetAllVersionsAsync(pageId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { page });
        _pageHistory.GetPagesWithUnpublishedChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _model.GetUnplacedCandidatesAsync(pageId, Arg.Any<CancellationToken>()).Returns(candidates);

        var vm = (PageZonesEditViewModel)((ViewResult)await _controller.PageZones(pageId, default)).Model!;

        Assert.That(vm.UnplacedCandidates, Is.SameAs(candidates));
    }

    [Test]
    public async Task PageZones_UpToDateAndNeverPublished_Variants()
    {
        var liveId = Guid.NewGuid();
        var live = Page(liveId, ContentVersionState.Published);
        var newId = Guid.NewGuid();
        var draft = Page(newId, ContentVersionState.Draft);
        _pageStore.GetCurrentDraftAsync(liveId, Arg.Any<CancellationToken>()).Returns(live);
        _pageStore.GetAllVersionsAsync(liveId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { live });
        _pageStore.GetCurrentDraftAsync(newId, Arg.Any<CancellationToken>()).Returns(draft);
        _pageStore.GetAllVersionsAsync(newId, Arg.Any<CancellationToken>()).Returns(new List<PageDTO> { draft });
        // The history service reports a page that is not live as having unpublished changes.
        _pageHistory.GetPagesWithUnpublishedChangesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { newId });

        var upToDate = (PageZonesEditViewModel)((ViewResult)await _controller.PageZones(liveId, default)).Model!;
        var neverPublished = (PageZonesEditViewModel)((ViewResult)await _controller.PageZones(newId, default)).Model!;

        Assert.Multiple(() =>
        {
            Assert.That(upToDate.IsPublished, Is.True);
            Assert.That(upToDate.HasPendingChanges, Is.False);
            Assert.That(neverPublished.IsPublished, Is.False);
            Assert.That(neverPublished.HasPendingChanges, Is.True);
            Assert.That(neverPublished.HasPendingZoneChanges, Is.False);
        });
    }

    [Test]
    public async Task DiscardPageZones_RedirectsBackToTheEditor_ReportingFailures()
    {
        var pageId = Guid.NewGuid();
        _pageHistory.DiscardUnpublishedChangesAsync(pageId, Arg.Any<CancellationToken>()).Returns((true, (string?)null), (false, "boom"));

        var ok = (RedirectToActionResult)await _controller.DiscardPageZones(pageId, default);
        Assert.That(_controller.TempData.ContainsKey("Error"), Is.False);

        var failed = (RedirectToActionResult)await _controller.DiscardPageZones(pageId, default);

        Assert.Multiple(() =>
        {
            Assert.That(ok.ActionName, Is.EqualTo(nameof(AdminContentZoneController.PageZones)));
            Assert.That(ok.RouteValues!["pageNodeId"], Is.EqualTo(pageId));
            Assert.That(failed.ActionName, Is.EqualTo(nameof(AdminContentZoneController.PageZones)));
            Assert.That(_controller.TempData["Error"], Is.EqualTo("boom"));
        });
    }
}
