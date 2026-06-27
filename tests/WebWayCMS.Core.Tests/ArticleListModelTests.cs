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
    private IContentService<ArticleListDTO> _listService = null!;
    private IContentService<ArticleDTO> _articleService = null!;
    private IArticleModel _articleModel = null!;
    private IMapper _mapper = null!;
    private ArticleListModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _listService = Substitute.For<IContentService<ArticleListDTO>>();
        _articleService = Substitute.For<IContentService<ArticleDTO>>();
        _articleModel = Substitute.For<IArticleModel>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleListModel(_listService, _articleService, _mapper, _articleModel);

        _listService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO>());
        _articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO>());
    }

    private static ArticleListDTO List(Guid? id = null, Guid master = default, int version = 0)
    {
        var cid = id ?? Guid.NewGuid();
        return new ArticleListDTO
        {
            ContentId = cid,
            ContentMeta = new ContentDTO { Id = cid, MasterId = master == default ? Guid.NewGuid() : master, Version = version, Title = "L", Slug = "list" }
        };
    }

    private static ArticleDTO Article(Guid listMaster, bool published = true)
    {
        var cid = Guid.NewGuid();
        return new ArticleDTO
        {
            ContentId = cid,
            Body = "b",
            ArticleListMasterId = listMaster,
            ContentMeta = new ContentDTO { Id = cid, Title = "A", IsPublished = published, PublicationDate = DateTime.UtcNow.AddDays(-1) }
        };
    }

    private static ViewDataDictionary NewViewData() =>
        new(new EmptyModelMetadataProvider(), new ModelStateDictionary());

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleListModel(null!, _articleService, _mapper, _articleModel), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listService, null!, _mapper, _articleModel), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listService, _articleService, null!, _articleModel), Throws.ArgumentNullException);
            Assert.That(() => new ArticleListModel(_listService, _articleService, _mapper, null!), Throws.ArgumentNullException);
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
    public async Task IArticleListModel_GetIndexViewModelAsync_FiltersPublished()
    {
        var lm = List().ContentMeta.MasterId;
        _articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO>
        {
            Article(lm, published: true),
            Article(lm, published: false)
        });

        var vm = await ((IArticleListModel)_model).GetIndexViewModelAsync(default);

        Assert.That(vm.Articles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetArticleListIndexAsync_CountsArticlesPerList()
    {
        var list = List();
        _listService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { list });
        _articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.ContentMeta.MasterId), Article(list.ContentMeta.MasterId) });

        var vm = await _model.GetArticleListIndexAsync();

        Assert.That(vm.ArticleLists.Single().ArticleCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetArticleListUpsertAsync_NullId_FoundAndNotFound()
    {
        var list = List();
        _listService.GetByIdAsync(list.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(list);
        _listService.GetByIdAsync(Arg.Is<Guid>(g => g != list.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticleListUpsertAsync(null), Is.Not.Null);
            Assert.That(await _model.GetArticleListUpsertAsync(list.ContentMeta.Id), Is.Not.Null);
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
        _listService.UpdateAsync(Arg.Any<ArticleListDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { Id = null, Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { Id = Guid.NewGuid(), Title = "T" })).Success, Is.True);
            Assert.That((await _model.SaveArticleListUpsertAsync(new ArticleListUpsertViewModel { Id = Guid.NewGuid(), Title = "T" })).Success, Is.False);
        });
    }

    [Test]
    public async Task DeleteArticleListAsync_NotFoundAndCascades()
    {
        Assert.That(await _model.DeleteArticleListAsync(Guid.NewGuid()), Is.False);

        var list = List();
        _listService.GetByIdAsync(list.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(list);
        _articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.ContentMeta.MasterId) });
        _listService.DeleteAsync(list.ContentMeta.Id, false, true, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await _model.DeleteArticleListAsync(list.ContentMeta.Id);

        await _articleService.Received().DeleteAsync(Arg.Any<Guid>(), false, true, Arg.Any<CancellationToken>());
        Assert.That(ok, Is.True);
    }

    [Test]
    public async Task GetArticlesForListAsync_FoundAndNotFound()
    {
        var list = List();
        _listService.GetByMasterIdAsync(list.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(list, (ArticleListDTO?)null);
        _articleService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleDTO> { Article(list.ContentMeta.MasterId) });

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticlesForListAsync(list.ContentMeta.MasterId), Is.Not.Null);
            Assert.That(await _model.GetArticlesForListAsync(list.ContentMeta.MasterId), Is.Null);
        });
    }

    [Test]
    public async Task GetArticlesForListBySlugAsync_FoundAndNotFound()
    {
        var list = List();
        _listService.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list, (ArticleListDTO?)null);
        _listService.GetByMasterIdAsync(list.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(list);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetArticlesForListBySlugAsync("list"), Is.Not.Null);
            Assert.That(await _model.GetArticlesForListBySlugAsync("list"), Is.Null);
        });
    }

    [Test]
    public async Task VersionHistoryAndRestore()
    {
        var master = Guid.NewGuid();
        _listService.GetAllVersionsAsync(master, Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List(master: master) });
        Assert.That(await _model.GetVersionHistoryAsync(master), Is.Not.Null);

        var historical = List(version: 1);
        var latest = List(master: historical.ContentMeta.MasterId, version: 4);
        _listService.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
        _listService.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(latest);
        Assert.That((await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id))!.Version, Is.EqualTo(4));
    }

    [Test]
    public async Task GetUpsertModelForRestore_NullWhenMissing()
    {
        _listService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
        Assert.That(await _model.GetUpsertModelForRestoreAsync(Guid.NewGuid()), Is.Null);

        var historical = List();
        _listService.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
        _listService.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
        Assert.That(await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id), Is.Null);
    }

    [Test]
    public async Task DeleteVersionAsync_Delegates()
    {
        _listService.DeleteAsync(Arg.Any<Guid>(), false, false, Arg.Any<CancellationToken>()).Returns(true);
        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task AdminHandlerMembers()
    {
        var list = List();
        _listService.GetByIdAsync(list.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(list);
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ArticleListIndexViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(list.ContentMeta.Id, query), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(null, query), Is.Not.Null);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ArticleListUpsertViewModel>());
            Assert.That(await _model.GetApiListAsync(), Is.Not.Null);
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_NotFoundReturnsNull()
    {
        _listService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), query), Is.Null);
    }

    [Test]
    public async Task SaveUpsertAsync_ObjectOverload()
    {
        var ok = await _model.SaveUpsertAsync((object)new ArticleListUpsertViewModel { Id = null, Title = "T" });
        Assert.That(ok.Success, Is.True);
    }

    [Test]
    public async Task DeleteAsync_Override_Delegates()
    {
        var list = List();
        _listService.GetByIdAsync(list.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(list);
        _listService.DeleteAsync(list.ContentMeta.Id, false, true, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(list.ContentMeta.Id), Is.True);
    }

    [Test]
    public async Task SecondaryApiList_MatchingAndNonMatchingKey()
    {
        _listService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List() });

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetSecondaryApiListAsync("articlelists"), Is.Not.Empty);
            Assert.That(await _model.GetSecondaryApiListAsync("other"), Is.Empty);
        });
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
        _listService.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listService.GetByMasterIdAsync(list.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(list);

        Assert.That(await _model.ChildHandler!.GetChildIndexViewModelAsync("list"), Is.Not.Null);
    }

    [Test]
    public async Task ChildHandler_GetChildUpsert_Variants()
    {
        var child = _model.ChildHandler!;
        var list = List();
        _listService.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listService.GetByMasterIdAsync(list.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(list);

        // parent list missing -> null
        _listService.GetBySlugAsync("missing", Arg.Any<CancellationToken>()).Returns((ArticleListDTO?)null);
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
        _listService.GetBySlugAsync("list", Arg.Any<CancellationToken>()).Returns(list);
        _listService.GetByMasterIdAsync(list.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(list);
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