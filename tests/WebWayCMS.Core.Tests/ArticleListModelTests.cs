using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ArticleListModelTests
{
    private IContentStore<ArticleListDTO> _listStore = null!;
    private IContentStore<ArticleDTO> _articleStore = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMapper _mapper = null!;
    private ArticleListModel _model = null!;
    private List<ArticleDTO> _articles = null!;

    [SetUp]
    public void SetUp()
    {
        _listStore = Substitute.For<IContentStore<ArticleListDTO>>();
        _articleStore = Substitute.For<IContentStore<ArticleDTO>>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleListModel(_listStore, _articleStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        _articles = new List<ArticleDTO>();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO>());
        _articleStore.CountCurrentDraftsByRelationAsync(ArticleModel.ListRelation, Arg.Any<CancellationToken>())
            .Returns(_ => _articles.GroupBy(a => a.Version.TargetOf(ArticleModel.ListRelation)!.Value).ToDictionary(g => g.Key, g => g.Count()));
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
        var versionId = Guid.NewGuid();
        return new ArticleDTO
        {
            VersionId = versionId,
            Body = "b",
            AuthorName = "a",
            Version = new ContentVersion
            {
                Id = versionId,
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "A",
                Slug = "a",
                VersionNumber = 0,
                State = ContentVersionState.Published,
                Relations = [new ContentRelation { SourceVersionId = versionId, TargetNodeId = listNodeId, Name = ArticleModel.ListRelation, Kind = RelationKind.Containment }]
            }
        };
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleListModel(null!, _articleStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, null!, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, null!, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _mapper, null!, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _mapper, _changeSetScope, null!, TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), null!), Throws.ArgumentNullException);
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
    public async Task DeleteArticleListAsync_GoesThroughTheRelationDeleteService()
    {
        // The service deletes the list's articles first (containment); this model only supplies the list's own delete.
        var deletes = Substitute.For<IContentDeleteService>();
        var listId = Guid.NewGuid();
        deletes.DeleteAsync(listId, Arg.Any<Func<Guid, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null, 2));
        var model = new ArticleListModel(_listStore, _articleStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), deletes);

        Assert.Multiple(async () =>
        {
            Assert.That(await model.DeleteArticleListAsync(listId), Is.True);
            Assert.That(await model.TryDeleteAsync(listId), Is.EqualTo((true, (string?)null, 2)));
        });
    }

    [Test]
    public async Task DeleteSelf_DeletesOnlyTheList()
    {
        var listId = Guid.NewGuid();
        _listStore.DeleteAsync(listId, false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteArticleListAsync(listId), Is.True);
        await _articleStore.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
