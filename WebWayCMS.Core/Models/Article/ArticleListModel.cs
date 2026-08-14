using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

public sealed class ArticleListModel : AdminCrudModel<ArticleListDTO>, IArticleListModel
{
    private readonly IContentStore<ArticleDTO> _articleStore;
    private readonly IContentStore<ArticleListDTO> _listStore;
    private readonly IMapper _mapper;
    private readonly ArticleChildHandler _childHandler;

    protected override IContentStore<ArticleListDTO> Store => _listStore;

    protected override string VersionHistoryContentType => "articles";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/articles";
    protected override Task<List<ArticleListDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _listStore.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _listStore.DeleteVersionAsync(id, ct);

    public override string ContentType => "articles";
    public override string DisplayName => "Article List";
    public override string IndexViewPath => "~/Views/AdminArticle/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/ArticleListUpsert.cshtml";
    public override bool HasSecondaryApiList => true;
    public override IAdminCrudChildHandler? ChildHandler => _childHandler;

    public ArticleListModel(
        IContentStore<ArticleListDTO> listStore,
        IContentStore<ArticleDTO> articleStore,
        IMapper mapper,
        IArticleModel articleModel,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _articleStore = articleStore ?? throw new ArgumentNullException(nameof(articleStore));
        _listStore = listStore ?? throw new ArgumentNullException(nameof(listStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _childHandler = new ArticleChildHandler(this, articleModel ?? throw new ArgumentNullException(nameof(articleModel)));
    }

    // IArticleListModel.GetIndexViewModelAsync — explicit to avoid clash with IAdminCrudHandler.GetIndexViewModelAsync
    async Task<ArticleListViewModel> IArticleListModel.GetIndexViewModelAsync(CancellationToken ct)
    {
        var vm = new ArticleListViewModel();
        var articles = await _articleStore.GetAllAsync(ct);
        vm.Articles = articles
            .Select(p => _mapper.Map<ArticleViewModel>(p))
            .ToList();
        return vm;
    }

    public async Task<ArticleListIndexViewModel> GetArticleListIndexAsync(CancellationToken ct = default)
    {
        var lists = await _listStore.GetAllCurrentDraftsAsync(ct);
        var articles = await _articleStore.GetAllCurrentDraftsAsync(ct);

        var vm = new ArticleListIndexViewModel
        {
            ArticleLists = lists.Select(l =>
            {
                var item = _mapper.Map<ArticleListItemViewModel>(l);
                item.ArticleCount = articles.Count(p => p.ArticleListNodeId == l.Version.Node.Id);
                return item;
            }).ToList()
        };
        return vm;
    }

    public async Task<ArticleListUpsertViewModel?> GetArticleListUpsertAsync(Guid? nodeId, CancellationToken ct = default)
    {
        if (nodeId == null) return new ArticleListUpsertViewModel();
        var dto = await _listStore.GetCurrentDraftAsync(nodeId.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleListUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveArticleListUpsertAsync(ArticleListUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<ArticleListDTO>(model);
        var result = await _listStore.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "Unable to update article list. It may have been removed.");
        model.NodeId = result.NodeId;
        return (true, null);
    }

    public async Task<bool> DeleteArticleListAsync(Guid nodeId, CancellationToken ct = default)
    {
        var list = await _listStore.GetCurrentDraftAsync(nodeId, ct);
        if (list == null) return false;
        var articles = await _articleStore.GetAllCurrentDraftsAsync(ct);
        foreach (var article in articles.Where(p => p.ArticleListNodeId == nodeId))
            await _articleStore.DeleteAsync(article.Version.Node.Id, softDelete: false, ct);
        return await _listStore.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public async Task<ArticleListViewModel?> GetArticlesForListAsync(Guid articleListNodeId, bool includeDrafts = false, CancellationToken ct = default)
    {
        var list = includeDrafts
            ? await _listStore.GetCurrentDraftAsync(articleListNodeId, ct)
            : await _listStore.GetAsync(articleListNodeId, ct);
        if (list == null) return null;

        var articles = includeDrafts
            ? await _articleStore.GetAllCurrentDraftsAsync(ct)
            : await _articleStore.GetAllAsync(ct);

        return new ArticleListViewModel
        {
            ArticleListId = list.Version.Node.Id,
            ArticleListTitle = list.Version.Title ?? string.Empty,
            ArticleListSlug = list.Version.Slug ?? string.Empty,
            Articles = articles
                .Where(p => p.ArticleListNodeId == list.Version.Node.Id)
                .Select(p => _mapper.Map<ArticleViewModel>(p))
                .ToList()
        };
    }

    public async Task<ArticleListViewModel?> GetArticlesForListBySlugAsync(string slug, CancellationToken ct = default)
    {
        var list = await _listStore.GetCurrentDraftBySlugAsync(slug, ct);
        if (list == null) return null;

        return await GetArticlesForListAsync(list.Version.Node.Id, includeDrafts: true, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var vm = _mapper.Map<ArticleListUpsertViewModel>(loaded.Value.Historical);
        vm.ExpectedVersionNumber = loaded.Value.CurrentVersionNumber;
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetArticleListIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        var vm = await GetArticleListUpsertAsync(id, ct);
        if (vm == null && id != null) return null;
        return vm ?? new ArticleListUpsertViewModel();
    }

    public override object CreateEmptyUpsertViewModel() => new ArticleListUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ArticleListUpsertViewModel)model;
        var result = await SaveArticleListUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true, NodeId: vm.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => DeleteArticleListAsync(id, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await ((IArticleListModel)this).GetIndexViewModelAsync(ct);
        return vm.Articles.Select(p => (object)new { id = p.NodeId, title = p.Title });
    }

    public override async Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
    {
        if (!string.Equals(key, "articlelists", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Empty<object>();

        var vm = await GetArticleListIndexAsync(ct);
        return vm.ArticleLists.Select(l => (object)new { id = l.NodeId, title = l.Title });
    }
}

/// <summary>Manages articles within an article list (child entities).</summary>
internal sealed class ArticleChildHandler : IAdminCrudChildHandler
{
    private readonly IArticleListModel _listModel;
    private readonly IArticleModel _articleModel;

    public ArticleChildHandler(IArticleListModel listModel, IArticleModel articleModel)
    {
        _listModel = listModel;
        _articleModel = articleModel;
    }

    public string ChildType => "articles";
    public string ChildDisplayName => "Article";
    public string[]? WriteRoles => ["Admin", "Editor"];

    public string ChildIndexViewPath => "~/Views/AdminArticle/Articles.cshtml";
    public string ChildUpsertViewPath => "~/Views/AdminArticle/Upsert.cshtml";

    public async Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default)
        => await _listModel.GetArticlesForListBySlugAsync(parentKey, ct);

    public async Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default)
    {
        var list = await _listModel.GetArticlesForListBySlugAsync(parentKey, ct);
        if (list == null) return null;
        var vm = await _articleModel.GetUpsertViewModelAsync(id, list.ArticleListId, ct);
        if (vm == null && id != null) return null;
        return vm;
    }

    public async Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default)
    {
        viewData["ArticleListSlug"] = parentKey;
        var list = await _listModel.GetArticlesForListBySlugAsync(parentKey, ct);
        viewData["ArticleListTitle"] = list?.ArticleListTitle;
    }

    public object CreateEmptyChildUpsertViewModel() => new ArticleUpsertViewModel();

    public async Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default)
    {
        var vm = (ArticleUpsertViewModel)model;
        var result = await _articleModel.SaveUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default)
        => _articleModel.DeleteAsync(id, ct);

    public bool SupportsReorder => false;

    public Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default)
        => Task.FromResult(false);

    public bool SupportsVersionHistory => true;

    public Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid nodeId, CancellationToken ct = default)
        => _articleModel.GetVersionHistoryAsync(nodeId, parentKey, ct);

    public async Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
        => await _articleModel.GetUpsertModelForRestoreAsync(historicalId, ct);

    public Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => _articleModel.DeleteVersionAsync(id, ct);
}
