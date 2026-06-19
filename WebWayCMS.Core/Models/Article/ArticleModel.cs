using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Mapping;

namespace WebWayCMS.Models.Article;

public sealed class ArticleModel : VersionedModel<ArticleDTO>, IArticleModel
{
    private readonly IContentService<ArticleDTO> _postService;
    private readonly IMapper _mapper;

    protected override string VersionHistoryContentType => "articles";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => $"/admin/articles/{parentKey}/articles";
    protected override Task<List<ArticleDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct) => _postService.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _postService.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public ArticleModel(IContentService<ArticleDTO> postService, IMapper mapper)
    {
        _postService = postService ?? throw new ArgumentNullException(nameof(postService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ArticleViewModel?> GetPostViewModelAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _postService.GetByIdAsync(id, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleViewModel>(dto);
    }

    public async Task<ArticleViewModel?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var dto = await _postService.GetBySlugAsync(slug, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleViewModel>(dto);
    }

    public async Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null) return new ArticleUpsertViewModel();
        var dto = await _postService.GetByIdAsync(id.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleUpsertViewModel>(dto);
    }

    public async Task<ArticleUpsertViewModel?> GetUpsertViewModelAsync(Guid? id, Guid articleListId, CancellationToken ct = default)
    {
        if (id == null) return new ArticleUpsertViewModel { ArticleListId = articleListId };
        var dto = await _postService.GetByIdAsync(id.Value, ct);
        if (dto == null) return null;
        return _mapper.Map<ArticleUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ArticleUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<ArticleDTO>(model);

        if (model.Id == null || model.Id == Guid.Empty)
        {
            await _postService.CreateAsync(dto, ct);
            return (true, null);
        }

        var success = await _postService.UpdateAsync(dto, ct);
        if (!success) return (false, "Unable to update post. It may have been removed.");
        return (true, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await _postService.DeleteAsync(id, false, true, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, string parentKey, CancellationToken ct = default)
        => BuildVersionHistoryAsync(masterId, parentKey, "articles", ct);

    public async Task<ArticleUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _postService.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _postService.GetByMasterIdAsync(historical.ContentMeta.MasterId, ct);
        if (latest == null) return null;
        var vm = _mapper.Map<ArticleUpsertViewModel>(historical);
        vm.Id = latest.ContentMeta.Id;
        vm.Version = latest.ContentMeta.Version;
        return vm;
    }

    public Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);
}
