using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Article lists: a named grouping that articles belong to (the <see cref="ArticleModel.ListRelation"/>
/// containment). The list is the admin entry point for articles — each row links to the Articles page
/// filtered to it. Deleting a list deletes its articles in the same transaction (the relation engine's
/// containment cascade).
/// </summary>
public sealed class ArticleListModel : AdminCrudModel<ArticleListDTO>, IArticleListModel
{
    private readonly IContentStore<ArticleDTO> _articleStore;
    private readonly IContentStore<ArticleListDTO> _listStore;
    private readonly IMapper _mapper;

    protected override IContentStore<ArticleListDTO> Store => _listStore;

    public override string ContentType => "articlelists";
    public override string DisplayName => "Article List";
    public override string IndexViewPath => "~/Views/AdminArticle/Index.cshtml";
    public override string UpsertViewPath => "~/Views/AdminArticle/ArticleListUpsert.cshtml";

    public ArticleListModel(
        IContentStore<ArticleListDTO> listStore,
        IContentStore<ArticleDTO> articleStore,
        IMapper mapper,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _listStore = listStore ?? throw new ArgumentNullException(nameof(listStore));
        _articleStore = articleStore ?? throw new ArgumentNullException(nameof(articleStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ArticleListIndexViewModel> GetArticleListIndexAsync(CancellationToken ct = default)
    {
        var lists = await _listStore.GetAllCurrentDraftsAsync(ct);
        var counts = await _articleStore.CountCurrentDraftsByRelationAsync(ArticleModel.ListRelation, ct);

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

    public Task<ArticleListUpsertViewModel?> GetArticleListUpsertAsync(Guid? nodeId, CancellationToken ct = default)
        => LoadUpsertAsync(nodeId, _mapper.Map<ArticleListUpsertViewModel>, ct);

    public async Task<(bool Success, string? ErrorMessage)> SaveArticleListUpsertAsync(ArticleListUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var result = await SaveListDraftAsync(model, ct);
        return (result.Success, result.ErrorMessage);
    }

    private Task<AdminSaveResult> SaveListDraftAsync(ArticleListUpsertViewModel model, CancellationToken ct)
        => SaveDraftAsync(_mapper.Map<ArticleListDTO>(model), model, "Unable to update article list. It may have been removed.", ct);

    /// <summary>
    /// Deletes the list and every article in it, as one transaction (the relation engine's containment
    /// cascade); any failure rolls the whole delete back.
    /// </summary>
    public Task<bool> DeleteArticleListAsync(Guid nodeId, CancellationToken ct = default)
        => DeleteAsync(nodeId, ct);

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, _mapper.Map<ArticleListUpsertViewModel>, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetArticleListIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetArticleListUpsertAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ArticleListUpsertViewModel();

    protected override Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
        => SaveListDraftAsync((ArticleListUpsertViewModel)model, ct);

    // The list alone is deleted by the base DeleteSelfAsync; its articles go first in the containment cascade.
}
