using ModelContextProtocol;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Mcp.Tests;

[TestFixture]
public class PageHistoryToolsetTests
{
    private IPageHistoryModel _model = null!;
    private PageHistoryToolset _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IPageHistoryModel>();
        _tools = new PageHistoryToolset(_model);
    }

    [Test]
    public void Constructor_NullModel_Throws() =>
        Assert.That(() => new PageHistoryToolset(null!), Throws.ArgumentNullException);

    [Test]
    public async Task ListPageHistory_ReturnsTheIndex_OrThrowsForAnUnknownPage()
    {
        var page = Guid.NewGuid();
        var index = new PageHistoryIndexViewModel { PageNodeId = page };
        _model.GetIndexAsync(page, Arg.Any<CancellationToken>()).Returns(index);
        _model.GetIndexAsync(Arg.Is<Guid>(g => g != page), Arg.Any<CancellationToken>()).Returns((PageHistoryIndexViewModel?)null);

        Assert.That(await _tools.ListPageHistory(page), Is.SameAs(index));
        Assert.That(async () => await _tools.ListPageHistory(Guid.NewGuid()), Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task GetPageHistoryEntry_DetailOrComparison()
    {
        var page = Guid.NewGuid();
        var entry = Guid.NewGuid();
        var detail = new PageHistoryEntryViewModel();
        var compare = new PageHistoryCompareViewModel { StateToken = "t" };
        _model.GetEntryAsync(page, entry, Arg.Any<CancellationToken>()).Returns(detail);
        _model.GetCompareAsync(page, entry, Arg.Any<CancellationToken>()).Returns(compare);

        Assert.Multiple(async () =>
        {
            Assert.That(await _tools.GetPageHistoryEntry(page, entry), Is.SameAs(detail));
            Assert.That(await _tools.GetPageHistoryEntry(page, entry, compareWithCurrent: true), Is.SameAs(compare));
        });
    }

    [Test]
    public void GetPageHistoryEntry_Unknown_Throws()
    {
        _model.GetEntryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PageHistoryEntryViewModel?)null);

        Assert.That(async () => await _tools.GetPageHistoryEntry(Guid.NewGuid(), Guid.NewGuid()), Throws.TypeOf<McpException>());
    }

    [Test]
    public async Task RevertPageToEntry_Success_OrThrowsTheReason()
    {
        var page = Guid.NewGuid();
        var entry = Guid.NewGuid();
        _model.RevertAsync(page, entry, "good", Arg.Any<CancellationToken>()).Returns((true, (string?)null));
        _model.RevertAsync(page, entry, "stale", Arg.Any<CancellationToken>()).Returns((false, "changed since"));

        Assert.That((await _tools.RevertPageToEntry(page, entry, "good")).Reverted, Is.True);
        Assert.That(async () => await _tools.RevertPageToEntry(page, entry, "stale"),
            Throws.TypeOf<McpException>().With.Message.EqualTo("changed since"));
    }

    [Test]
    public async Task DeletePageHistoryEntry_Deletes_OrThrowsForAnUnknownEntry()
    {
        _model.DeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true, false);

        Assert.That((await _tools.DeletePageHistoryEntry(Guid.NewGuid(), Guid.NewGuid())).Deleted, Is.True);
        Assert.That(async () => await _tools.DeletePageHistoryEntry(Guid.NewGuid(), Guid.NewGuid()),
            Throws.TypeOf<McpException>().With.Message.Contains("found"));
    }
}
