using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin;
using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminPageHistoryControllerTests
{
    private IPageHistoryModel _model = null!;
    private AdminPageHistoryController _controller = null!;
    private readonly Guid _page = Guid.NewGuid();
    private readonly Guid _entry = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IPageHistoryModel>();
        _controller = new AdminPageHistoryController(_model);
        new MvcHarness().Configure(_controller, ["Admin"]);
    }

    [Test]
    public void Constructor_Null_Throws() =>
        Assert.That(() => new AdminPageHistoryController(null!), Throws.ArgumentNullException);

    [Test]
    public async Task GetActions_RenderTheirViewOrNotFound()
    {
        _model.GetIndexAsync(_page, Arg.Any<CancellationToken>()).Returns(new PageHistoryIndexViewModel(), (PageHistoryIndexViewModel?)null);
        _model.GetEntryAsync(_page, _entry, Arg.Any<CancellationToken>()).Returns(new PageHistoryEntryViewModel());
        _model.GetCompareAsync(_page, _entry, Arg.Any<CancellationToken>()).Returns(new PageHistoryCompareViewModel());

        Assert.Multiple(async () =>
        {
            Assert.That(((ViewResult)await _controller.Index(_page, default)).ViewName, Does.EndWith("Index.cshtml"));
            Assert.That(await _controller.Index(_page, default), Is.InstanceOf<NotFoundResult>());
            Assert.That(((ViewResult)await _controller.Entry(_page, _entry, default)).ViewName, Does.EndWith("Entry.cshtml"));
            Assert.That(((ViewResult)await _controller.Compare(_page, _entry, default)).ViewName, Does.EndWith("Compare.cshtml"));
            Assert.That(((ViewResult)await _controller.Preview(_page, _entry, default)).ViewName, Does.EndWith("Preview.cshtml"));
            Assert.That(await _controller.Entry(_page, Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
            Assert.That(await _controller.Compare(_page, Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
            Assert.That(await _controller.Preview(_page, Guid.NewGuid(), default), Is.InstanceOf<NotFoundResult>());
        });
    }

    [Test]
    public async Task Revert_Success_GoesToTheEditor()
    {
        _model.RevertAsync(_page, _entry, "t", Arg.Any<CancellationToken>()).Returns((true, (string?)null));

        var result = (RedirectToActionResult)await _controller.Revert(_page, _entry, "t", default);

        Assert.Multiple(() =>
        {
            Assert.That(result.ControllerName, Is.EqualTo("AdminContentZone"));
            Assert.That(result.ActionName, Is.EqualTo("PageZones"));
            Assert.That(_controller.TempData["Success"], Is.Not.Null);
        });
    }

    [Test]
    public async Task Revert_Failure_ReturnsToTheComparisonWithTheReason()
    {
        _model.RevertAsync(_page, _entry, string.Empty, Arg.Any<CancellationToken>()).Returns((false, "stale"));

        var result = (RedirectToActionResult)await _controller.Revert(_page, _entry, null, default);

        Assert.Multiple(() =>
        {
            Assert.That(result.ActionName, Is.EqualTo(nameof(AdminPageHistoryController.Compare)));
            Assert.That(_controller.TempData["Error"], Is.EqualTo("stale"));
        });
    }

    [Test]
    public async Task Delete_ReportsTheOutcome()
    {
        _model.DeleteAsync(_page, _entry, Arg.Any<CancellationToken>()).Returns(true, false);

        var deleted = (RedirectToActionResult)await _controller.Delete(_page, _entry, default);
        var deletedMessage = _controller.TempData["Success"];
        await _controller.Delete(_page, _entry, default);

        Assert.Multiple(() =>
        {
            Assert.That(deleted.ActionName, Is.EqualTo(nameof(AdminPageHistoryController.Index)));
            Assert.That(deletedMessage, Is.Not.Null);
            Assert.That(_controller.TempData["Error"], Is.Not.Null);
        });
    }
}
