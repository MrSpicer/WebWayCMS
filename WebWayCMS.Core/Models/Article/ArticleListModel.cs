using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Mapping;

namespace WebWayCMS.Models.Article;

public sealed class ArticleListModel : AdminCrudModel<ArticleListDTO>, IArticleListModel
{
    private readonly IContentService<ArticleDTO> _articleService;
    private readonly IContentService<ArticleListDTO> _articleListService;
    private readonly IMapper _mapper;
    private readonly ArticleChildHandler _childHandler;

    protected override string VersionHistoryContentType => "articles";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/articles";
    protected override Task<List<ArticleListDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct) => _articleListService.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _articleListService.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "articles";
    public override string DisplayName => "Article List";
    public override string IndexViewPath => "~/Views/AdminArticle/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/ArticleListUpsert.cshtml";
    public override bool HasSecondaryApiList => true;
    public override IAdminCrudChildHandler? ChildHandler => _childHandler;

    public ArticleListModel(
        IContentService<ArticleListDTO> articleListService,
        IContentService<ArticleDTO> articleService,
        IMapper mapper,
        IArticleModel articleModel)
    {
        _articleService = articleService ?? throw new ArgumentNullException(nameof(articleService));
        _articleListService = articleListService ?? throw new ArgumentNullException(nameof(articleListService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _childHandler = new ArticleChildHandler(this, articleModel ?? throw new ArgumentNullException(nameof(articleModel)));
    }

    // IArticleListModel.GetIndexViewModelAsync — explicit to avoid clash with IAdminCrudHandler.GetIndexViewModelAsync
    async Task<ArticleListViewModel> IArticleListModel.GetIndexViewModelAsync(CancellationToken ct)
    {
        var vm = new ArticleListViewModel();
        var articles = await _articleService.GetAllAsync(ct);
        vm.Articles = articles
            .Where(p => p.ContentMeta.IsPublished && p.ContentMeta.PublicationDate <= DateTime.UtcNow)
            .Select(p => _mapper.Map<ArticleViewModel>(p))
            .ToList();
        return vm;
    }

    public async Task<ArticleListIndexViewModel> GetArticleListIndexAsync(CancellationToken ct = default)
    {
        var lists = await _articleListService.GetAllAsync(ct);
        var articles = await _articleService.GetAllAsync(ct);

        var vm = new ArticleListIndexViewModel
        {
            ArticleLists = lists.Select(l =>
            {
                var item = _mapper.Map<ArticleListItemViewModel>(l);
                item.ArticleCount = articles.Count(p => p.ArticleListMasterId == l.ContentMeta.MasterId);
                return item;
            }).ToList()
        };
        return vm;
    }

    public async Task<ArticleListUpsertViewModel?> GetArticleListUpsertAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null) return new ArticleListUpsertViewModel();
        var dto = await _articleListService.GetByIdAsync(id.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleListUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveArticleListUpsertAsync(ArticleListUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<ArticleListDTO>(model);

        if (model.Id == null || model.Id == Guid.Empty)
        {
            await _articleListService.CreateAsync(dto, ct);
            return (true, null);
        }

        var success = await _articleListService.UpdateAsync(dto, ct);
        if (!success) return (false, "Unable to update article list. It may have been removed.");
        return (true, null);
    }

    public async Task<bool> DeleteArticleListAsync(Guid id, CancellationToken ct = default)
    {
        var list = await _articleListService.GetByIdAsync(id, ct);
        if (list == null) return false;
        var articles = await _articleService.GetAllAsync(ct);
        foreach (var p in articles.Where(p => p.ArticleListMasterId == list.ContentMeta.MasterId))
            await _articleService.DeleteAsync(p.ContentMeta.Id, false, true, ct);
        return await _articleListService.DeleteAsync(id, false, true, ct);
    }

    public async Task<ArticleListViewModel?> GetArticlesForListAsync(Guid articleListMasterId, CancellationToken ct = default)
    {
        var list = await _articleListService.GetByMasterIdAsync(articleListMasterId, ct);
        if (list == null) return null;

        var articles = await _articleService.GetAllAsync(ct);
        return new ArticleListViewModel
        {
            ArticleListId = list.ContentMeta.MasterId,
            ArticleListTitle = list.ContentMeta.Title,
            ArticleListSlug = list.ContentMeta.Slug,
            Articles = articles
                .Where(p => p.ArticleListMasterId == list.ContentMeta.MasterId && p.ContentMeta.IsPublished && p.ContentMeta.PublicationDate <= DateTime.UtcNow)
                .Select(p => _mapper.Map<ArticleViewModel>(p))
                .ToList()
        };
    }

    public async Task<ArticleListViewModel?> GetArticlesForListBySlugAsync(string slug, CancellationToken ct = default)
    {
        var list = await _articleListService.GetBySlugAsync(slug, ct);
        if (list == null) return null;

        return await GetArticlesForListAsync(list.ContentMeta.MasterId, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(masterId, ct: ct);

    public async Task<ArticleListUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _articleListService.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _articleListService.GetByMasterIdAsync(historical.ContentMeta.MasterId, ct);
        if (latest == null) return null;
        var vm = _mapper.Map<ArticleListUpsertViewModel>(historical);
        vm.Id = latest.ContentMeta.Id;
        vm.Version = latest.ContentMeta.Version;
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members (override AdminCrudModel<T> abstract members)
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetArticleListIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        var vm = await GetArticleListUpsertAsync(id, ct);
        if (vm == null && id != null) return null;
        return vm ?? new ArticleListUpsertViewModel();
    }

    public override object CreateEmptyUpsertViewModel() => new ArticleListUpsertViewModel();

    public override async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (ArticleListUpsertViewModel)model;
        var result = await SaveArticleListUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => DeleteArticleListAsync(id, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await ((IArticleListModel)this).GetIndexViewModelAsync(ct);
        return vm.Articles.Select(p => (object)new { id = p.Id, title = p.Title });
    }

    public override async Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
    {
        if (!string.Equals(key, "articlelists", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Empty<object>();

        var vm = await GetArticleListIndexAsync(ct);
        return vm.ArticleLists.Select(l => (object)new { id = l.Id, title = l.Title });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => await GetUpsertModelForRestoreAsync(historicalId, ct);
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

    public Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid masterId, CancellationToken ct = default)
        => _articleModel.GetVersionHistoryAsync(masterId, parentKey, ct);

    public async Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
        => await _articleModel.GetUpsertModelForRestoreAsync(historicalId, ct);

    public Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => _articleModel.DeleteVersionAsync(id, ct);
}
