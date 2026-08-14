using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
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
    private IArticleModel _articleModel = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMapper _mapper = null!;
    private ArticleListModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _listStore = Substitute.For<IContentStore<ArticleListDTO>>();
        _articleStore = Substitute.For<IContentStore<ArticleDTO>>();
        _articleModel = Substitute.For<IArticleModel>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleListModel(_listStore, _articleStore, _mapper, _articleModel, _changeSetScope);

        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO>());
        _articleStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO>());
        _articleStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO>());
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

    private static ViewDataDictionary NewViewData() =>
        new(new EmptyModelMetadataProvider(), new ModelStateDictionary());

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleListModel(null!, _articleStore, _mapper, _articleModel, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, null!, _mapper, _articleModel, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, null!, _articleModel, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _mapper, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listStore, _articleStore, _mapper, _articleModel, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("articles"));
            Assert.That(_model.DisplayName, Is.EqualTo("Article List"));
            Assert.That(_model.IndexViewPath, Does.Contain("Index.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("ArticleListUpsert.cshtml"));
            Assert.That(_model.HasSecondaryApiList, Is.True);
            Assert.That(_model.ChildHandler, Is.Not.Null);
        });
    }

    [Test]
    public async Task IArticleListModel_GetIndexViewModelAsync_MapsAllArticles()
    {
        var listNodeId = Guid.NewGuid();
        _articleStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO>
        {
            Article(listNodeId),
            Article(Guid.NewGuid())
        });

        var vm = await ((IArticleListModel)_model).GetIndexViewModelAsync(default);

        Assert.That(vm.Articles, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetArticleListIndexAsync_CountsArticlesPerList()
    {
        var list = List();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { list });
        _articleStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.Version.Node.Id), Article(list.Version.Node.Id) });

        var vm = await _model.GetArticleListIndexAsync();

        Assert.That(vm.ArticleLists.Single().ArticleCount, Is.EqualTo(2));
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
            .Returns(new ContentWriteResult(true), new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = null, Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T" })).Success, Is.False);
        });
    }

    [Test]
    public async Task DeleteArticleListAsync_NotFoundAndCascades()
    {
        Assert.That(await _model.DeleteArticleListAsync(Guid.NewGuid()), Is.False);

        var list = List();
        _listStore.GetCurrentDraftAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        _articleStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.Version.Node.Id) });
        _listStore.DeleteAsync(list.Version.Node.Id, false, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await _model.DeleteArticleListAsync(list.Version.Node.Id);

        await _articleStore.Received().DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>());
        Assert.That(ok, Is.True);
    }

    [Test]
    public async Task GetArticlesForListAsync_FoundAndNotFound()
    {
        var list = List();
        _listStore.GetAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list, (ArticleListDTO?)null);
        _articleStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.Version.Node.Id) });

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticlesForListAsync(list.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetArticlesForListAsync(list.Version.Node.Id), Is.Null);
        });
    }

    [Test]
    public async Task GetArticlesForListBySlugAsync_FoundAndNotFound()
    {
        var list = List();
        _listStore.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list, (ArticleListDTO?)null);
        _listStore.GetAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticlesForListBySlugAsync("list"), Is.Not.Null);
            Assert.That(await _model.GetArticlesForListBySlugAsync("list"), Is.Null);
        });
    }

    [Test]
    public async Task VersionHistoryAndDeleteVersion()
    {
        var nodeId = Guid.NewGuid();
        _listStore.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List(nodeId: nodeId) });
        Assert.That(await _model.GetVersionHistoryAsync(nodeId), Is.Not.Null);

        _listStore.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task AdminHandlerMembers()
    {
        var list = List();
        _listStore.GetCurrentDraftAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ArticleListIndexViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(list.Version.Node.Id, query), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(null, query), Is.Not.Null);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ArticleListUpsertViewModel>());
            Assert.That(await _model.GetApiListAsync(), Is.Not.Null);
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
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
            .Returns(new ContentWriteResult(true));

        var ok = await _model.SaveUpsertAsync((object)new ArticleListUpsertViewModel { NodeId = null, Title = "T" });
        Assert.That(ok.Success, Is.True);
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
    public async Task SecondaryApiList_MatchingAndNonMatchingKey()
    {
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List() });

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetSecondaryApiListAsync("articlelists"), Is.Not.Empty);
            Assert.That(await _model.GetSecondaryApiListAsync("other"), Is.Empty);
        });
    }

    [Test]
    public async Task PublishAsync_DelegatesToStore()
    {
        _listStore.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ContentWriteResult(true));

        Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);
    }

    // --- Child handler ---

    [Test]
    public void ChildHandler_Metadata()
    {
        var child = _model.ChildHandler!;

        Assert.Multiple(() =>
        {
            Assert.That(child.ChildType, Is.EqualTo("articles"));
            Assert.That(child.ChildDisplayName, Is.EqualTo("Article"));
            Assert.That(child.WriteRoles, Is.EqualTo(new[] { "Admin", "Editor" }));
            Assert.That(child.ChildIndexViewPath, Does.Contain("Articles.cshtml"));
            Assert.That(child.ChildUpsertViewPath, Does.Contain("Upsert.cshtml"));
            Assert.That(child.SupportsReorder, Is.False);
            Assert.That(child.SupportsVersionHistory, Is.True);
            Assert.That(child.CreateEmptyChildUpsertViewModel(), Is.InstanceOf<ArticleUpsertViewModel>());
        });
    }

    [Test]
    public async Task ChildHandler_GetChildIndex_ReturnsListBySlug()
    {
        var list = List();
        _listStore.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listStore.GetAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);

        Assert.That(await _model.ChildHandler!.GetChildIndexViewModelAsync("list"), Is.Not.Null);
    }

    [Test]
    public async Task ChildHandler_GetChildUpsert_Variants()
    {
        var child = _model.ChildHandler!;
        var list = List();
        _listStore.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listStore.GetAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);

        // parent list missing -> null
        _listStore.GetBySlugAsync("missing", Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
        Assert.That(await child.GetChildUpsertViewModelAsync("missing", Guid.NewGuid()), Is.Null);

        // article model returns a vm
        _articleModel.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ArticleUpsertViewModel());
        Assert.That(await child.GetChildUpsertViewModelAsync("list", null), Is.Not.Null);

        // article model returns null with non-null id -> null
        _articleModel.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ArticleUpsertViewModel?)null);
        Assert.That(await child.GetChildUpsertViewModelAsync("list", Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task ChildHandler_SetViewData_SetsSlugAndTitle()
    {
        var list = List();
        _listStore.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listStore.GetAsync(list.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(list);
        var viewData = NewViewData();

        await _model.ChildHandler!.SetChildUpsertViewDataAsync(viewData, "list");

        Assert.That(viewData["ArticleListSlug"], Is.EqualTo("list"));
    }

    [Test]
    public async Task ChildHandler_SaveDeleteReorderAndVersions()
    {
        var child = _model.ChildHandler!;
        _articleModel.SaveUpsertAsync(Arg.Any<ArticleUpsertViewModel>(), Arg.Any<CancellationToken>()).Returns((true, (string?)null), (false, "err"));
        _articleModel.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _articleModel.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.Multiple(async () =>
        {
            Assert.That((await child.SaveChildUpsertAsync("list", new ArticleUpsertViewModel())).Success, Is.True);
            Assert.That((await child.SaveChildUpsertAsync("list", new ArticleUpsertViewModel())).Success, Is.False);
            Assert.That(await child.DeleteChildAsync(Guid.NewGuid()), Is.True);
            Assert.That(await child.ReorderAsync("list", new List<Guid>()), Is.False);
            Assert.That(await child.DeleteChildVersionAsync(Guid.NewGuid()), Is.True);
        });
    }

    [Test]
    public async Task ChildHandler_VersionHistoryAndRestore_Delegate()
    {
        var child = _model.ChildHandler!;
        _articleModel.GetVersionHistoryAsync(Arg.Any<Guid>(), "list", Arg.Any<CancellationToken>())
            .Returns(new Models.Shared.VersionHistoryViewModel());
        _articleModel.GetUpsertModelForRestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ArticleUpsertViewModel());

        Assert.Multiple(async () =>
        {
            Assert.That(await child.GetChildVersionHistoryViewModelAsync("list", Guid.NewGuid()), Is.Not.Null);
            Assert.That(await child.GetChildRestoreVersionViewModelAsync("list", Guid.NewGuid()), Is.Not.Null);
        });
    }
}
