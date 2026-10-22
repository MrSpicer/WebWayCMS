using System.Linq.Expressions;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ArticleListModelTests
{
    private IContentStore<ArticleListDTO> _listStore = null!;
    private IContentStore<ArticleDTO> _articleStore = null!;
    private ICmsTransaction _transaction = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMapper _mapper = null!;
    private ArticleListModel _model = null!;
    private List<ArticleDTO> _articles = null!;

    [SetUp]
    public void SetUp()
    {
        _listStore = Substitute.For<IContentStore<ArticleListDTO>>();
        _articleStore = Substitute.For<IContentStore<ArticleDTO>>();
        _transaction = Substitute.For<ICmsTransaction>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleListModel(_listStore, _articleStore, _transaction, _mapper, _changeSetScope);

        _articles = new List<ArticleDTO>();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO>());
        _articleStore.FindCurrentDraftsAsync(Arg.Any<Expression<Func<ArticleDTO, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => _articles.Where(c.Arg<Expression<Func<ArticleDTO, bool>>>().Compile()).ToList());
        _articleStore.CountCurrentDraftsByAsync(Arg.Any<Expression<Func<ArticleDTO, Guid>>>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var key = c.Arg<Expression<Func<ArticleDTO, Guid>>>().Compile();
                return _articles.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
            });

        // The transaction runs the work as given; its own commit/rollback is covered by CmsTransactionTests.
        _transaction.RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));
    }

    private static ArticleListDTO List(Guid? nodeId = null, int version = 0)
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new ArticleListDTO
        {
            VersionId = Guid.NewGuid(),
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow },
                Title = "L",
                Slug = "list",
                VersionNumber = version,
                State = ContentVersionState.Draft
            }
        };
    }

    private static ArticleDTO Article(Guid listNodeId)
    {
        return new ArticleDTO
        {
            VersionId = Guid.NewGuid(),
            Body = "b",
            AuthorName = "a",
            ArticleListNodeId = listNodeId,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "A",
                Slug = "a",
                VersionNumber = 0,
                State = ContentVersionState.Published
            }
        };
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleListModel(null!, _articleStore, _transaction, _mapper, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, null!, _transaction, _mapper, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, null!, _mapper, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _transaction, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _transaction, _mapper, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("articlelists"));
            Assert.That(_model.DisplayName, Is.EqualTo("Article List"));
            Assert.That(_model.IndexViewPath, Does.Contain("Index.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("ArticleListUpsert.cshtml"));
            Assert.That(_model.HasSecondaryApiList, Is.False);
            Assert.That(_model.ChildHandler, Is.Null);
        });
    }

    [Test]
    public async Task GetArticleListIndexAsync_CountsArticlesPerList()
    {
        var list = List();
        var empty = List();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { list, empty });
        _articles.AddRange([Article(list.Version.Node.Id), Article(list.Version.Node.Id), Article(Guid.NewGuid())]);

        var vm = await _model.GetArticleListIndexAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ArticleLists[0].ArticleCount, Is.EqualTo(2));
            Assert.That(vm.ArticleLists[1].ArticleCount, Is.Zero);
        });
    }

    [Test]
    public async Task GetArticleListUpsertAsync_NullId_FoundAndNotFound()
    {
        var list = List();
        _listStore.GetCurrentDraftAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        _listStore.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != list.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticleListUpsertAsync(null), Is.Not.Null);
            Assert.That(await _model.GetArticleListUpsertAsync(list.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetArticleListUpsertAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void SaveArticleListUpsertAsync_NullModel_Throws()
    {
        Assert.That(async () => await _model.SaveArticleListUpsertAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task SaveArticleListUpsertAsync_CreateAndUpdate()
    {
        _listStore.SaveDraftAsync(Arg.Any<ArticleListDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(true), new ContentWriteResult(false, "err"), new ContentWriteResult(false));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = null, Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" })).ErrorMessage, Is.EqualTo("err"));
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" })).ErrorMessage, Does.StartWith("Unable to update"));
        });
    }

    [Test]
    public async Task DeleteArticleListAsync_NotFound_ReturnsFalseWithoutATransaction()
    {
        Assert.That(await _model.DeleteArticleListAsync(Guid.NewGuid()), Is.False);
        await _transaction.DidNotReceive().RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteArticleListAsync_DeletesItsArticlesThenTheList_InOneTransaction()
    {
        var list = List();
        var listId = list.Version.Node.Id;
        var mine = Article(listId);
        var other = Article(Guid.NewGuid());
        _articles.AddRange([mine, other]);
        _listStore.GetCurrentDraftAsync(listId, Arg.Any<CancellationToken>()).Returns(list);
        _articleStore.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);
        _listStore.DeleteAsync(listId, false, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await _model.DeleteArticleListAsync(listId);

        Assert.That(ok, Is.True);
        await _transaction.Received(1).RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _articleStore.DeleteAsync(mine.Version.Node.Id, false, Arg.Any<CancellationToken>());
            _listStore.DeleteAsync(listId, false, Arg.Any<CancellationToken>());
        });
        await _articleStore.DidNotReceive().DeleteAsync(other.Version.Node.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteArticleListAsync_AnArticleDeleteFails_StopsAndReportsFailure()
    {
        var list = List();
        var listId = list.Version.Node.Id;
        _articles.Add(Article(listId));
        _listStore.GetCurrentDraftAsync(listId, Arg.Any<CancellationToken>()).Returns(list);
        _articleStore.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(false);

        var ok = await _model.DeleteArticleListAsync(listId);

        Assert.That(ok, Is.False);
        await _listStore.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VersionHistoryAndDeleteVersion()
    {
        var nodeId = Guid.NewGuid();
        _listStore.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List(nodeId: nodeId) });
        var vm = await _model.GetVersionHistoryAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.ContentType, Is.EqualTo("articlelists"));
            Assert.That(vm.BackUrl, Is.EqualTo("/wadmin/articlelists"));
        });

        _listStore.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task AdminHandlerMembers()
    {
        var list = List(version: 4);
        var historical = List(nodeId: list.Version.Node.Id, version: 1);
        _listStore.GetCurrentDraftAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        _listStore.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ArticleListIndexViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(list.Version.Node.Id, query), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(null, query), Is.Not.Null);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ArticleListUpsertViewModel>());
            var restore = (ArticleListUpsertViewModel)(await _model.GetRestoreVersionViewModelAsync(historical.VersionId))!;
            Assert.That(restore.ExpectedVersionNumber, Is.EqualTo(list.Version.VersionNumber));
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetApiListAsync_ReturnsIdAndTitle()
    {
        var titled = List();
        var untitled = List();
        untitled.Version.Title = null;
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { titled, untitled });

        var items = (await _model.GetApiListAsync()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items[0].ToString(), Does.Contain(titled.Version.Node.Id.ToString()).And.Contain("title = L"));
            Assert.That(items[1].ToString(), Does.EndWith("title =  }"));
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NotFoundReturnsNull()
    {
        _listStore.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), query), Is.Null);
    }

    [Test]
    public async Task SaveUpsertAsync_ObjectOverload()
    {
        _listStore.SaveDraftAsync(Arg.Any<ArticleListDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        var ok = await _model.SaveUpsertAsync((object)new ArticleListUpsertViewModel { NodeId = null, Title = "T" });
        var failed = await _model.SaveUpsertAsync((object)new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" });

        Assert.Multiple(() =>
        {
            Assert.That(ok.Success, Is.True);
            Assert.That(failed.ErrorMessage, Is.EqualTo("err"));
        });
    }

    [Test]
    public async Task DeleteAsync_Override_Delegates()
    {
        var list = List();
        _listStore.GetCurrentDraftAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        _listStore.DeleteAsync(list.Version.Node.Id, false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(list.Version.Node.Id), Is.True);
    }

    [Test]
    public async Task PublishAsync_DelegatesToStore()
    {
        _listStore.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);
    }
}
