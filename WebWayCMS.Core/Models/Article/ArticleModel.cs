using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Slugs;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Articles: a top-level content type (draft → publish like every other type). Each article belongs to
/// exactly one article list through the <see cref="ListRelation"/> containment relation, chosen with an
/// ordinary entity picker; the admin Articles page filters by list with <c>?list={nodeId}</c>. Because it
/// is a containment, an article is visible only while its list is, and deleting a list deletes its
/// articles (both done by the relation engine).
/// </summary>
public sealed class ArticleModel : AdminCrudModel<ArticleDTO>, IArticleModel, IAdminCrudHandler
{
    /// <summary>The query-string key the admin index reads its list filter from (a new article preselects its list with <c>?ArticleListId=</c>).</summary>
    public const string ListQueryKey = "list";

    /// <summary>The article → article list containment relation's name.</summary>
    public const string ListRelation = nameof(ArticleUpsertViewModel.ArticleListId);

    private readonly IContentStore<ArticleDTO> _store;
    private readonly IContentStore<ArticleListDTO> _listStore;
    private readonly IMapper _mapper;

    protected override IContentStore<ArticleDTO> Store => _store;

    public override string ContentType => "articles";
    public override string DisplayName => "Article";
    public override string[]? WriteRoles => UserProfileRules.AdminOrEditor;
    public override string IndexViewPath => "~/Views/AdminArticle/Articles.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/Upsert.cshtml";

    public ArticleModel(
        IContentStore<ArticleDTO> store,
        IContentStore<ArticleListDTO> listStore,
        IMapper mapper,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _listStore = listStore ?? throw new ArgumentNullException(nameof(listStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    // ─── public reads (widget) ────────────────────────────────────────────────

    // The store hides an article whose list is not visible in the read context (containment).

    public async Task<ArticleViewModel?> GetViewModelAsync(Guid nodeId, CancellationToken ct = default)
        => Map(await _store.GetAsync(nodeId, ct));

    public async Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Map(await _store.GetBySlugAsync(slug, ct));

    public async Task<ArticleListViewModel?> GetListAsync(Guid articleListNodeId, int? maxItems, string? sortOrder, CancellationToken ct = default)
    {
        var list = await _listStore.GetAsync(articleListNodeId, ct);
        if (list == null) return null;

        var articles = await _store.FindRelatedAsync(ListRelation, articleListNodeId, ct);
        var sorted = ArticleSortOrder.Apply(articles.Select(a => _mapper.Map<ArticleViewModel>(a)), sortOrder);

        return new ArticleListViewModel
        {
            ArticleListId = articleListNodeId,
            ArticleListTitle = list.Version.Title ?? string.Empty,
            ArticleListSlug = list.Version.Slug ?? string.Empty,
            Articles = (maxItems is > 0 ? sorted.Take(maxItems.Value) : sorted).ToList()
        };
    }

    private ArticleViewModel? Map(ArticleDTO? dto)
        => dto == null ? null : _mapper.Map<ArticleViewModel>(dto);

    // ─── admin ────────────────────────────────────────────────────────────────

    public override Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => GetIndexViewModelAsync(listFilter: null, ct);

    Task<object> IAdminCrudHandler.GetIndexViewModelAsync(IQueryCollection query, CancellationToken ct)
        => GetIndexViewModelAsync(ParseListFilter(query), ct);

    private async Task<object> GetIndexViewModelAsync(Guid? listFilter, CancellationToken ct)
    {
        var lists = await _listStore.GetAllCurrentDraftsAsync(ct);
        var listTitles = lists.ToDictionary(l => l.Version.Node.Id, l => l.Version.Title ?? string.Empty);

        var articles = listFilter.HasValue
            ? await _store.FindCurrentDraftsRelatedAsync(ListRelation, listFilter.Value, ct)
            : await _store.GetAllCurrentDraftsAsync(ct);

        var items = articles.Select(a =>
        {
            var item = _mapper.Map<ArticleItemViewModel>(a);
            item.ArticleListTitle = listTitles.GetValueOrDefault(item.ArticleListId, string.Empty);
            return item;
        }).ToList();

        return new ArticleIndexViewModel
        {
            Articles = items,
            ArticleLists = lists.Select(l => _mapper.Map<ArticleListItemViewModel>(l)).ToList(),
            SelectedListId = listFilter,
            SelectedListTitle = listFilter.HasValue ? listTitles.GetValueOrDefault(listFilter.Value) : null
        };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await LoadUpsertAsync(id, _mapper.Map<ArticleUpsertViewModel>, ct);

    public override object CreateEmptyUpsertViewModel() => new ArticleUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ArticleUpsertViewModel)model;

        // The list itself was checked by the relation binder (SaveUpsertAsync) before this runs.
        var excludeNodeId = vm.NodeId is { } nodeId && nodeId != Guid.Empty ? nodeId : (Guid?)null;
        if (!await IsSlugAvailableAsync(vm.Title, vm.Slug, excludeNodeId, ct))
            return new AdminSaveResult(false, "Another article already uses this slug.", nameof(ArticleUpsertViewModel.Slug));

        return await SaveDraftAsync(_mapper.Map<ArticleDTO>(vm), vm, "Unable to update article. It may have been removed.", ct);
    }

    /// <summary>
    /// Article slugs are global (the widget resolves <c>/{page}/{slug}</c> across every list), so a slug
    /// another article's current draft already uses is rejected. Compared in the stored (normalized) form.
    /// </summary>
    private async Task<bool> IsSlugAvailableAsync(string title, string? slug, Guid? excludeNodeId, CancellationToken ct)
    {
        var effectiveSlug = SlugNormalizer.Normalize(string.IsNullOrWhiteSpace(slug) ? title : slug);
        var clashes = await _store.FindCurrentDraftsAsync(a => a.Version.Slug == effectiveSlug, ct);
        return clashes.All(a => a.Version.Node.Id == excludeNodeId);
    }

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, _mapper.Map<ArticleUpsertViewModel>, ct);

    private static Guid? ParseListFilter(IQueryCollection query)
        => Guid.TryParse(query[ListQueryKey].FirstOrDefault(), out var id) && id != Guid.Empty
            ? id
            : null;
}
