using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.ContentBlock;

public sealed class ContentBlockModel : AdminCrudModel<ContentBlockDTO>, IContentBlockModel
{
    private readonly IContentStore<ContentBlockDTO> _store;
    private readonly IMapper _mapper;

    protected override IContentStore<ContentBlockDTO> Store => _store;

    protected override string VersionHistoryContentType => "contentblocks";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/contentblocks";
    protected override Task<List<ContentBlockDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "contentblocks";
    public override string DisplayName => "Content Block";
    public override string IndexViewPath => "~/Views/AdminContentBlock/ContentBlocks.cshtml";
    public override string UpsertViewPath => "~/Views/AdminContentBlock/ContentBlockUpsert.cshtml";

    public ContentBlockModel(IContentStore<ContentBlockDTO> store, IMapper mapper, IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ContentBlockViewModel?> GetViewModelByNodeIdAsync(Guid nodeId, CancellationToken ct = default)
    {
        var dto = await _store.GetAsync(nodeId, ct);
        if (dto == null) return null;
        return _mapper.Map<ContentBlockViewModel>(dto);
    }

    public async Task<ContentBlockIndexViewModel> GetContentBlockIndexAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        var items = dtos.Select(d => _mapper.Map<ContentBlockItemViewModel>(d)).ToList();
        return new ContentBlockIndexViewModel { ContentBlocks = items };
    }

    public async Task<ContentBlockUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default)
    {
        if (nodeId == null || nodeId == Guid.Empty)
            return new ContentBlockUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(nodeId.Value, ct);
        if (dto == null)
            return null;

        return _mapper.Map<ContentBlockUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ContentBlockUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<ContentBlockDTO>(model);
        var result = await _store.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "An error occurred while saving the content block.");
        model.NodeId = result.NodeId;
        return (true, null);
    }

    public override async Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default)
    {
        return await _store.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetContentBlockIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetUpsertModelAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ContentBlockUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentBlockUpsertViewModel)model;
        var result = await SaveUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true, NodeId: vm.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetContentBlockIndexAsync(ct);
        return vm.ContentBlocks.Select(cb => (object)new { id = cb.NodeId, title = cb.Title });
    }
}
