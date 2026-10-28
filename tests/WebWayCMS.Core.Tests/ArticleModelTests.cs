using System.Linq.Expressions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ArticleModelTests
{
    private IContentStore<ArticleDTO> _store = null!;
    private IContentStore<ArticleListDTO> _listStore = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMapper _mapper = null!;
    private ArticleModel _model = null!;

    /// <summary>The articles the store's predicate queries run over (current drafts and read-context alike).</summary>
    private List<ArticleDTO> _articles = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<ArticleDTO>>();
        _listStore = Substitute.For<IContentStore<ArticleListDTO>>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleModel(_store, _listStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        _articles = new List<ArticleDTO>();
        _store.FindAsync(Arg.Any<Expression<Func<ArticleDTO, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => _articles.Where(c.Arg<Expression<Func<ArticleDTO, bool>>>().Compile()).ToList());
        _store.FindCurrentDraftsAsync(Arg.Any<Expression<Func<ArticleDTO, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => _articles.Where(c.Arg<Expression<Func<ArticleDTO, bool>>>().Compile()).ToList());
        _store.FindRelatedAsync(ArticleModel.ListRelation, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _articles.Where(a => a.Version.TargetOf(ArticleModel.ListRelation) == c.ArgAt<Guid>(1)).ToList());
        _store.FindCurrentDraftsRelatedAsync(ArticleModel.ListRelation, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _articles.Where(a => a.Version.TargetOf(ArticleModel.ListRelation) == c.ArgAt<Guid>(1)).ToList());
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(_ => _articles.ToList());
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO>());
    }

    private static ArticleDTO Article(Guid listNodeId, string title = "T", string slug = "s", Guid? nodeId = null,
        int version = 0, DateTime? created = null, DateTime? published = null)
    {
        var versionId = Guid.NewGuid();
        return new ArticleDTO
        {
            VersionId = versionId,
            Body = "b",
            AuthorName = "a",
            Summary = "sum",
            Version = new ContentVersion
            {
                Id = versionId,
                Node = new ContentNode { Id = nodeId ?? Guid.NewGuid(), CreatedUtc = created ?? DateTime.UtcNow },
                Title = title,
                Slug = slug,
                VersionNumber = version,
                PublishStartUtc = published,
                State = ContentVersionState.Draft,
                Relations = [new ContentRelation { SourceVersionId = versionId, TargetNodeId = listNodeId, Name = ArticleModel.ListRelation, Kind = RelationKind.Containment }]
            }
        };
    }

    private static ArticleListDTO List(Guid nodeId, string title = "L") => new()
    {
        VersionId = Guid.NewGuid(),
        Version = new ContentVersion
        {
            Node = new ContentNode { Id = nodeId, CreatedUtc = DateTime.UtcNow },
            Title = title,
            Slug = "list",
            State = ContentVersionState.Published
        }
    };

    private static IQueryCollection Query(string key, string value)
        => new QueryCollection(new Dictionary<string, StringValues> { [key] = value });

    private static ArticleUpsertViewModel ValidUpsert(Guid listId, Guid? nodeId = null, string? slug = null) => new()
    {
        NodeId = nodeId,
        Title = "My Post",
        Slug = slug,
        Body = "<p>b</p>",
        AuthorName = "a",
        ArticleListId = listId
    };

    /// <summary>Makes <paramref name="listNodeId"/> an existing list for the save-path check.</summary>
    private void ListExists(Guid listNodeId)
        => _listStore.GetCurrentDraftAsync(listNodeId, Arg.Any<CancellationToken>()).Returns(List(listNodeId));

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ArticleModel(null!, _listStore, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleModel(_store, null!, _mapper, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleModel(_store, _listStore, null!, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
            Assert.That(() => new ArticleModel(_store, _listStore, _mapper, null!, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_IsATopLevelArticlesType()
    {
        var handler = (IAdminCrudHandler)_model;
        Assert.Multiple(() =>
        {
            Assert.That(handler.ContentType, Is.EqualTo("articles"));
            Assert.That(handler.DisplayName, Is.EqualTo("Article"));
            Assert.That(handler.WriteRoles, Is.EqualTo(new[] { "Admin", "Editor" }));
            Assert.That(handler.IndexViewPath, Is.EqualTo("~/Views/AdminArticle/Articles.cshtml"));
            Assert.That(handler.UpsertViewPath, Is.EqualTo("~/Views/AdminArticle/Upsert.cshtml"));
            Assert.That(handler.ChildHandler, Is.Null);
            Assert.That(handler.SupportsPublishing, Is.True);
            Assert.That(handler.CreateEmptyUpsertViewModel(), Is.TypeOf<ArticleUpsertViewModel>());
        });
    }

    // ─── public reads ─────────────────────────────────────────────────────────

    // An article whose list is not visible is hidden by the store (containment visibility, covered by
    // the Data tests), so the model maps whatever the store returns.

    [Test]
    public async Task GetViewModelAsync_MapsTheStoreResult()
    {
        var listId = Guid.NewGuid();
        var article = Article(listId);
        var nodeId = article.Version.Node.Id;
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(article);

        var vm = await _model.GetViewModelAsync(nodeId);

        Assert.Multiple(async () =>
        {
            Assert.That(vm!.NodeId, Is.EqualTo(nodeId));
            Assert.That(vm.Summary, Is.EqualTo("sum"));
            Assert.That(vm.ArticleListId, Is.EqualTo(listId));
            Assert.That(await _model.GetViewModelAsync(Guid.NewGuid()), Is.Null, "article missing or hidden");
        });
    }

    [Test]
    public async Task GetBySlugAsync_MapsTheStoreResult()
    {
        _store.GetBySlugAsync("post", Arg.Any<CancellationToken>()).Returns(Article(Guid.NewGuid(), slug: "post"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.GetBySlugAsync("post"))!.Slug, Is.EqualTo("post"));
            Assert.That(await _model.GetBySlugAsync("missing"), Is.Null);
        });
    }

    [Test]
    public async Task GetListAsync_ListNotVisible_ReturnsNull()
    {
        Assert.That(await _model.GetListAsync(Guid.NewGuid(), null, null), Is.Null);
    }

    [Test]
    public async Task GetListAsync_FiltersToTheList_SortsAndCaps()
    {
        var listId = Guid.NewGuid();
        _listStore.GetAsync(listId, Arg.Any<CancellationToken>()).Returns(List(listId, "Blog"));
        var old = Article(listId, "B old", created: new DateTime(2020, 1, 1));
        var mid = Article(listId, "C mid", created: new DateTime(2021, 1, 1));
        var scheduled = Article(listId, "A scheduled", created: new DateTime(2019, 1, 1), published: new DateTime(2022, 1, 1));
        _articles.AddRange([old, mid, scheduled, Article(Guid.NewGuid(), "Other list")]);

        var newest = await _model.GetListAsync(listId, null, null);
        var oldestTwo = await _model.GetListAsync(listId, 2, ArticleSortOrder.Oldest);
        var byTitle = await _model.GetListAsync(listId, 0, ArticleSortOrder.Title);

        Assert.Multiple(() =>
        {
            Assert.That(newest!.ArticleListId, Is.EqualTo(listId));
            Assert.That(newest.ArticleListTitle, Is.EqualTo("Blog"));
            Assert.That(newest.ArticleListSlug, Is.EqualTo("list"));
            // The publication date wins over the creation date.
            Assert.That(newest.Articles.Select(a => a.Title), Is.EqualTo(new[] { "A scheduled", "C mid", "B old" }));
            Assert.That(oldestTwo!.Articles.Select(a => a.Title), Is.EqualTo(new[] { "B old", "C mid" }));
            Assert.That(byTitle!.Articles.Select(a => a.Title), Is.EqualTo(new[] { "A scheduled", "B old", "C mid" }));
        });
    }

    [Test]
    public async Task GetListAsync_NullTitleAndSlug_MapToEmpty()
    {
        var listId = Guid.NewGuid();
        var list = List(listId);
        list.Version.Title = null;
        list.Version.Slug = null;
        _listStore.GetAsync(listId, Arg.Any<CancellationToken>()).Returns(list);

        var vm = await _model.GetListAsync(listId, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.ArticleListTitle, Is.Empty);
            Assert.That(vm.ArticleListSlug, Is.Empty);
        });
    }

    // ─── admin index / upsert ─────────────────────────────────────────────────

    [Test]
    public async Task GetIndexViewModelAsync_Unfiltered_ListsEveryArticleWithItsListTitle()
    {
        var listA = Guid.NewGuid();
        var orphanList = Guid.NewGuid();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ArticleListDTO> { List(listA, "A") });
        _articles.AddRange([Article(listA, "in A"), Article(orphanList, "orphan")]);

        var vm = (ArticleIndexViewModel)await _model.GetIndexViewModelAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedListId, Is.Null);
            Assert.That(vm.SelectedListTitle, Is.Null);
            Assert.That(vm.ArticleLists.Single().Title, Is.EqualTo("A"));
            Assert.That(vm.Articles.Select(a => a.ArticleListTitle), Is.EqualTo(new[] { "A", "" }));
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_ListQuery_FiltersToThatList()
    {
        var listA = Guid.NewGuid();
        var listB = Guid.NewGuid();
        _listStore.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ArticleListDTO> { List(listA, "A"), List(listB, "B") });
        _articles.AddRange([Article(listA, "in A"), Article(listB, "in B")]);

        var vm = (ArticleIndexViewModel)await ((IAdminCrudHandler)_model)
            .GetIndexViewModelAsync(Query(ArticleModel.ListQueryKey, listB.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedListId, Is.EqualTo(listB));
            Assert.That(vm.SelectedListTitle, Is.EqualTo("B"));
            Assert.That(vm.Articles.Select(a => a.Title), Is.EqualTo(new[] { "in B" }));
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_UnknownListQuery_FiltersWithNoTitle()
    {
        var unknown = Guid.NewGuid();
        _articles.Add(Article(Guid.NewGuid()));

        var vm = (ArticleIndexViewModel)await ((IAdminCrudHandler)_model)
            .GetIndexViewModelAsync(Query("list", unknown.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedListId, Is.EqualTo(unknown));
            Assert.That(vm.SelectedListTitle, Is.Null);
            Assert.That(vm.Articles, Is.Empty);
        });
    }

    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task GetIndexViewModelAsync_InvalidOrEmptyListQuery_IsUnfiltered(string value)
    {
        _articles.Add(Article(Guid.NewGuid()));

        var vm = (ArticleIndexViewModel)await ((IAdminCrudHandler)_model).GetIndexViewModelAsync(Query("list", value));

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedListId, Is.Null);
            Assert.That(vm.Articles, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_Create_PreselectsTheQueryList()
    {
        var listId = Guid.NewGuid();

        var withList = (ArticleUpsertViewModel?)await _model.GetUpsertViewModelAsync(null, Query("list", listId.ToString()));
        var withEmptyId = (ArticleUpsertViewModel?)await _model.GetUpsertViewModelAsync(Guid.Empty, QueryCollection.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(withList!.ArticleListId, Is.EqualTo(listId));
            Assert.That(withList.NodeId, Is.Null);
            Assert.That(withEmptyId!.ArticleListId, Is.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_Edit_FoundAndNotFound()
    {
        var listId = Guid.NewGuid();
        var article = Article(listId, version: 2);
        article.Version.State = ContentVersionState.Published;
        var nodeId = article.Version.Node.Id;
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(article);

        var vm = (ArticleUpsertViewModel?)await _model.GetUpsertViewModelAsync(nodeId, QueryCollection.Empty);

        Assert.Multiple(async () =>
        {
            Assert.That(vm!.NodeId, Is.EqualTo(nodeId));
            Assert.That(vm.ArticleListId, Is.EqualTo(listId));
            Assert.That(vm.ExpectedVersionNumber, Is.EqualTo(2));
            Assert.That(vm.IsPublished, Is.True);
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), QueryCollection.Empty), Is.Null);
        });
    }

    // ─── save ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task SaveUpsertAsync_SavesADraftWithoutPublishing()
    {
        var listId = Guid.NewGuid();
        ListExists(listId);
        var nodeId = Guid.NewGuid();
        ArticleDTO? saved = null;
        _store.SaveDraftAsync(Arg.Do<ArticleDTO>(d => saved = d), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: nodeId));
        var vm = ValidUpsert(listId);

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.NodeId, Is.EqualTo(nodeId));
            Assert.That(vm.NodeId, Is.EqualTo(nodeId));
            Assert.That(saved!.Version.TargetOf(ArticleModel.ListRelation), Is.EqualTo(listId));
            await _store.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task SaveUpsertAsync_SanitizesRichTextBody()
    {
        var listId = Guid.NewGuid();
        ListExists(listId);
        ArticleDTO? saved = null;
        _store.SaveDraftAsync(Arg.Do<ArticleDTO>(d => saved = d), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));
        var vm = new ArticleUpsertViewModel
        {
            Title = "T",
            AuthorName = "a",
            ArticleListId = listId,
            Body = "<p onclick=\"alert(1)\">hello<script>alert(2)</script></p>"
        };

        var result = await _model.SaveUpsertAsync(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(saved!.Body, Does.Not.Contain("<script>"));
            Assert.That(saved.Body, Does.Not.Contain("onclick"));
        });
    }

    [Test]
    public async Task SaveUpsertAsync_EmptyOrUnknownList_RejectedOnTheListField()
    {
        // The real relation binder: the [ContentRelation] on ArticleListId is what rejects these.
        var relations = Substitute.For<IContentRelations>();
        relations.GetNodesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RelationNode>());
        var model = new ArticleModel(_store, _listStore, _mapper, _changeSetScope, new RelationBinder(relations), TestSupport.PassThroughDeletes());

        var empty = await model.SaveUpsertAsync(ValidUpsert(Guid.Empty));
        var unknown = await model.SaveUpsertAsync(ValidUpsert(Guid.NewGuid()));

        Assert.Multiple(async () =>
        {
            Assert.That(empty.Success, Is.False);
            Assert.That(empty.ErrorField, Is.EqualTo(nameof(ArticleUpsertViewModel.ArticleListId)));
            Assert.That(empty.ErrorMessage, Is.EqualTo("Choose the article list this article belongs to."));
            Assert.That(unknown.Success, Is.False);
            Assert.That(unknown.ErrorField, Is.EqualTo(nameof(ArticleUpsertViewModel.ArticleListId)));
            await _store.DidNotReceive().SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task SaveUpsertAsync_SlugUsedByAnotherArticle_RejectedOnTheSlugField()
    {
        var listId = Guid.NewGuid();
        ListExists(listId);
        _articles.Add(Article(Guid.NewGuid(), slug: "my-post"));

        // Derived from the title ("My Post" -> "my-post") and given explicitly in another form.
        var derived = await _model.SaveUpsertAsync(ValidUpsert(listId));
        var explicitSlug = await _model.SaveUpsertAsync(ValidUpsert(listId, slug: "My Post"));

        Assert.Multiple(async () =>
        {
            Assert.That(derived.Success, Is.False);
            Assert.That(derived.ErrorField, Is.EqualTo(nameof(ArticleUpsertViewModel.Slug)));
            Assert.That(explicitSlug.ErrorField, Is.EqualTo(nameof(ArticleUpsertViewModel.Slug)));
            await _store.DidNotReceive().SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task SaveUpsertAsync_ArticleKeepsItsOwnSlug()
    {
        var listId = Guid.NewGuid();
        ListExists(listId);
        var existing = Article(listId, slug: "my-post");
        _articles.Add(existing);
        _store.SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: existing.Version.Node.Id));

        var result = await _model.SaveUpsertAsync(ValidUpsert(listId, nodeId: existing.Version.Node.Id));

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertAsync_StoreFailure_ReportsItsMessageOrADefault()
    {
        var listId = Guid.NewGuid();
        ListExists(listId);
        _store.SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, ErrorMessage: "stale"), new ContentWriteResult(false));

        var withMessage = await _model.SaveUpsertAsync(ValidUpsert(listId, nodeId: Guid.NewGuid()));
        var withoutMessage = await _model.SaveUpsertAsync(ValidUpsert(listId, nodeId: Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(withMessage.ErrorMessage, Is.EqualTo("stale"));
            Assert.That(withoutMessage.ErrorMessage, Does.StartWith("Unable to update article"));
        });
    }

    // ─── delete / history / api ───────────────────────────────────────────────

    [Test]
    public async Task DeleteAsync_HardDeletesTheNode()
    {
        var nodeId = Guid.NewGuid();
        _store.DeleteAsync(nodeId, false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(nodeId), Is.True);
    }

    [Test]
    public async Task VersionHistory_BacksToTheArticlesPage()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<ArticleDTO> { Article(Guid.NewGuid(), nodeId: nodeId) });

        var vm = await _model.GetVersionHistoryViewModelAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.ContentType, Is.EqualTo("articles"));
            Assert.That(vm.BackUrl, Is.EqualTo("/wadmin/articles"));
        });
    }

    [Test]
    public async Task DeleteVersionAsync_DelegatesToStore()
    {
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task GetRestoreVersionViewModelAsync_CarriesTheCurrentVersionNumber()
    {
        var nodeId = Guid.NewGuid();
        var historical = Article(Guid.NewGuid(), nodeId: nodeId, version: 1);
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Article(Guid.NewGuid(), nodeId: nodeId, version: 3));

        var vm = (ArticleUpsertViewModel?)await _model.GetRestoreVersionViewModelAsync(historical.VersionId);

        Assert.Multiple(async () =>
        {
            Assert.That(vm!.ExpectedVersionNumber, Is.EqualTo(3));
            Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetApiListAsync_ReturnsIdAndTitle()
    {
        var article = Article(Guid.NewGuid(), "Hello");
        var untitled = Article(Guid.NewGuid());
        untitled.Version.Title = null;
        _articles.AddRange([article, untitled]);

        var items = (await _model.GetApiListAsync()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items[0].ToString(), Does.Contain(article.Version.Node.Id.ToString()).And.Contain("Hello"));
            Assert.That(items[1].ToString(), Does.EndWith("title =  }"));
        });
    }
}
