using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Security;

namespace WebWayCMS.Models.Article;

public sealed class ArticleModel : VersionedModel<ArticleDTO>, IArticleModel
{
    private readonly IContentStore<ArticleDTO> _store;
    private readonly IMapper _mapper;

    protected override string VersionHistoryContentType => "articles";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => $"/wadmin/articles/{parentKey}/articles";
    protected override Task<List<ArticleDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public ArticleModel(IContentStore<ArticleDTO> store, IMapper mapper)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ArticleViewModel?> GetPostViewModelAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _store.GetAsync(id, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleViewModel>(dto);
    }

    public async Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var dto = await _store.GetBySlugAsync(slug, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleViewModel>(dto);
    }

    public async Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? nodeId, CancellationToken ct = default)
    {
        if (nodeId == null) return new ArticleUpsertViewModel();
        var dto = await _store.GetCurrentDraftAsync(nodeId.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleUpsertViewModel>(dto);
    }

    public async Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? nodeId, Guid articleListNodeId, CancellationToken ct = default)
    {
        if (nodeId == null) return new ArticleUpsertViewModel { ArticleListId = articleListNodeId };
        var dto = await _store.GetCurrentDraftAsync(nodeId.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ArticleUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        RichTextSanitizer.Sanitize(model);

        var dto = _mapper.Map<ArticleDTO>(model);
        var result = await _store.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "Unable to update article. It may have been removed.");

        // Articles are child entities with no separate publish surface; they publish immediately.
        await _store.PublishAsync(result.NodeId, ct);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default)
    {
        return await _store.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, string parentKey, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, parentKey, "articles", ct);

    public async Task<ArticleUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _store.GetVersionAsync(historicalId, ct);
        if (historical == null) return null;
        var current = await _store.GetCurrentDraftAsync(historical.Version.Node.Id, ct);
        if (current == null) return null;
        var vm = _mapper.Map<ArticleUpsertViewModel>(historical);
        vm.ExpectedVersionNumber = current.Version.VersionNumber;
        return vm;
    }

    public Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);
}
