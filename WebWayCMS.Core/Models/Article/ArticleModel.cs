using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Slugs;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Articles: a top-level content type (draft → publish like every other type). Each article belongs to
/// exactly one article list through the <see cref="ArticleDTO.ArticleListNodeId"/> FK, chosen with an
/// ordinary entity picker; the admin Articles page filters by list with <c>?list={nodeId}</c>.
/// </summary>
public sealed class ArticleModel : AdminCrudModel<ArticleDTO>, IArticleModel, IAdminCrudHandler
{
    /// <summary>The query-string key the admin index and create form read the list filter from.</summary>
    public const string ListQueryKey = "list";

    private readonly IContentStore<ArticleDTO> _store;
    private readonly IContentStore<ArticleListDTO> _listStore;
    private readonly IMapper _mapper;

    protected override IContentStore<ArticleDTO> Store => _store;

    protected override string VersionHistoryContentType => "articles";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/articles";
    protected override Task<List<ArticleDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "articles";
    public override string DisplayName => "Article";
    public override string[]? WriteRoles => ["Admin", "Editor"];
    public override string IndexViewPath => "~/Views/AdminArticle/Articles.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/Upsert.cshtml";

    public ArticleModel(
        IContentStore<ArticleDTO> store,
        IContentStore<ArticleListDTO> listStore,
        IMapper mapper,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _listStore = listStore ?? throw new ArgumentNullException(nameof(listStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    // ─── public reads (widget) ────────────────────────────────────────────────

    public async Task<ArticleViewModel?> GetViewModelAsync(Guid nodeId, CancellationToken ct = default)
        => await MapIfListVisibleAsync(await _store.GetAsync(nodeId, ct), ct);

    public async Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await MapIfListVisibleAsync(await _store.GetBySlugAsync(slug, ct), ct);

    public async Task<ArticleListViewModel?> GetListAsync(Guid articleListNodeId, int? maxItems, string? sortOrder, CancellationToken ct = default)
    {
        var list = await _listStore.GetAsync(articleListNodeId, ct);
        if (list == null) return null;

        var articles = await _store.FindAsync(a => a.ArticleListNodeId == articleListNodeId, ct);
        var sorted = ArticleSortOrder.Apply(articles.Select(a => _mapper.Map<ArticleViewModel>(a)), sortOrder);

        return new ArticleListViewModel
        {
            ArticleListId = articleListNodeId,
            ArticleListTitle = list.Version.Title ?? string.Empty,
            ArticleListSlug = list.Version.Slug ?? string.Empty,
            Articles = (maxItems is > 0 ? sorted.Take(maxItems.Value) : sorted).ToList()
        };
    }

    /// <summary>An article renders only while its list is visible in the same read context.</summary>
    private async Task<ArticleViewModel?> MapIfListVisibleAsync(ArticleDTO? dto, CancellationToken ct)
    {
        if (dto == null) return null;
        if (await _listStore.GetAsync(dto.ArticleListNodeId, ct) == null) return null;
        return _mapper.Map<ArticleViewModel>(dto);
    }

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
            ? await _store.FindCurrentDraftsAsync(a => a.ArticleListNodeId == listFilter.Value, ct)
            : await _store.GetAllCurrentDraftsAsync(ct);

        var items = articles.Select(a =>
        {
            var item = _mapper.Map<ArticleItemViewModel>(a);
            item.ArticleListTitle = listTitles.GetValueOrDefault(a.ArticleListNodeId, string.Empty);
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
    {
        if (id == null || id == Guid.Empty)
            return new ArticleUpsertViewModel { ArticleListId = ParseListFilter(query) ?? Guid.Empty };

        var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
        return dto == null ? null : _mapper.Map<ArticleUpsertViewModel>(dto);
    }

    public override object CreateEmptyUpsertViewModel() => new ArticleUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ArticleUpsertViewModel)model;

        // The list comes from the form (or MCP fields), so check it names a real list rather than let
        // an empty or foreign id surface as an FK violation.
        if (vm.ArticleListId == Guid.Empty || await _listStore.GetCurrentDraftAsync(vm.ArticleListId, ct) == null)
            return new AdminSaveResult(false, "Choose the article list this article belongs to.", nameof(ArticleUpsertViewModel.ArticleListId));

        var excludeNodeId = vm.NodeId is { } nodeId && nodeId != Guid.Empty ? nodeId : (Guid?)null;
        if (!await IsSlugAvailableAsync(vm.Title, vm.Slug, excludeNodeId, ct))
            return new AdminSaveResult(false, "Another article already uses this slug.", nameof(ArticleUpsertViewModel.Slug));

        var dto = _mapper.Map<ArticleDTO>(vm);
        var result = await _store.SaveDraftAsync(dto, vm.ExpectedVersionNumber, ct);
        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage ?? "Unable to update article. It may have been removed.");

        vm.NodeId = result.NodeId;
        return new AdminSaveResult(true, NodeId: result.NodeId);
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

    public override Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => _store.DeleteAsync(id, softDelete: false, ct);

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var vm = _mapper.Map<ArticleUpsertViewModel>(loaded.Value.Historical);
        vm.ExpectedVersionNumber = loaded.Value.CurrentVersionNumber;
        return vm;
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var articles = await _store.GetAllCurrentDraftsAsync(ct);
        return articles.Select(a => (object)new { id = a.Version.Node.Id, title = a.Version.Title ?? string.Empty });
    }

    private static Guid? ParseListFilter(IQueryCollection query)
        => Guid.TryParse(query[ListQueryKey].FirstOrDefault(), out var id) && id != Guid.Empty
            ? id
            : null;
}
