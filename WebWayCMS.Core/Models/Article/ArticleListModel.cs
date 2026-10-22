using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Article lists: a named grouping that articles point at (<see cref="ArticleDTO.ArticleListNodeId"/>).
/// The list is the admin entry point for articles — each row links to the Articles page filtered to it.
/// Deleting a list deletes its articles in the same transaction.
/// </summary>
public sealed class ArticleListModel : AdminCrudModel<ArticleListDTO>, IArticleListModel
{
    private readonly IContentStore<ArticleDTO> _articleStore;
    private readonly IContentStore<ArticleListDTO> _listStore;
    private readonly ICmsTransaction _transaction;
    private readonly IMapper _mapper;

    protected override IContentStore<ArticleListDTO> Store => _listStore;

    protected override string VersionHistoryContentType => "articlelists";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/articlelists";
    protected override Task<List<ArticleListDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _listStore.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _listStore.DeleteVersionAsync(id, ct);

    public override string ContentType => "articlelists";
    public override string DisplayName => "Article List";
    public override string IndexViewPath => "~/Views/AdminArticle/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/ArticleListUpsert.cshtml";

    public ArticleListModel(
        IContentStore<ArticleListDTO> listStore,
        IContentStore<ArticleDTO> articleStore,
        ICmsTransaction transaction,
        IMapper mapper,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _listStore = listStore ?? throw new ArgumentNullException(nameof(listStore));
        _articleStore = articleStore ?? throw new ArgumentNullException(nameof(articleStore));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ArticleListIndexViewModel> GetArticleListIndexAsync(CancellationToken ct = default)
    {
        var lists = await _listStore.GetAllCurrentDraftsAsync(ct);
        var counts = await _articleStore.CountCurrentDraftsByAsync(a => a.ArticleListNodeId, ct);

        return new ArticleListIndexViewModel
        {
            ArticleLists = lists.Select(l =>
            {
                var item = _mapper.Map<ArticleListItemViewModel>(l);
                item.ArticleCount = counts.GetValueOrDefault(l.Version.Node.Id);
                return item;
            }).ToList()
        };
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

    /// <summary>
    /// Deletes the list and every article in it, as one transaction: the articles go first (their FK to
    /// the list is <c>Restrict</c>), and any failure rolls the whole delete back.
    /// </summary>
    public async Task<bool> DeleteArticleListAsync(Guid nodeId, CancellationToken ct = default)
    {
        var list = await _listStore.GetCurrentDraftAsync(nodeId, ct);
        if (list == null) return false;

        return await _transaction.RunAsync(async token =>
        {
            var articles = await _articleStore.FindCurrentDraftsAsync(a => a.ArticleListNodeId == nodeId, token);
            foreach (var article in articles)
            {
                if (!await _articleStore.DeleteAsync(article.Version.Node.Id, softDelete: false, token))
                    return false;
            }
            return await _listStore.DeleteAsync(nodeId, softDelete: false, token);
        }, ct);
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
        var lists = await _listStore.GetAllCurrentDraftsAsync(ct);
        return lists.Select(l => (object)new { id = l.Version.Node.Id, title = l.Version.Title ?? string.Empty });
    }
}
