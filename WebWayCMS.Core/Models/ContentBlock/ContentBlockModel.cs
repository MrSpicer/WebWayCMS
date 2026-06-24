using Microsoft.AspNetCore.Http;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Mapping;

namespace WebWayCMS.Models.ContentBlock;

public sealed class ContentBlockModel : AdminCrudModel<ContentBlockDTO>, IContentBlockModel
{
    private readonly IContentService<ContentBlockDTO> _service;
    private readonly IMapper _mapper;

    protected override string VersionHistoryContentType => "contentblocks";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/contentblocks";
    protected override Task<List<ContentBlockDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct) => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _service.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "contentblocks";
    public override string DisplayName => "Content Block";

    public ContentBlockModel(IContentService<ContentBlockDTO> service, IMapper mapper)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<ContentBlockViewModel?> GetViewModelByMasterIdAsync(Guid masterId, CancellationToken ct = default)
    {
        var dto = await _service.GetByMasterIdAsync(masterId, ct);
        if (dto == null) return null;
        return _mapper.Map<ContentBlockViewModel>(dto);
    }

    public async Task<ContentBlockIndexViewModel> GetContentBlockIndexAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        var items = dtos.Select(d => _mapper.Map<ContentBlockItemViewModel>(d)).ToList();
        return new ContentBlockIndexViewModel { ContentBlocks = items };
    }

    public async Task<ContentBlockUpsertViewModel?> GetUpsertModelAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
        {
            return new ContentBlockUpsertViewModel();
        }

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
        {
            return null;
        }

        return _mapper.Map<ContentBlockUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ContentBlockUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dto = _mapper.Map<ContentBlockDTO>(model);
        var ok = await _service.UpsertAsync(dto, ct);
        if (!ok) return (false, "An error occurred while saving the content block.");
        return (true, null);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await _service.DeleteAsync(id, false, true, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid masterId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(masterId, ct: ct);

    public async Task<ContentBlockUpsertViewModel?> GetUpsertModelForRestoreAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _service.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _service.GetByMasterIdAsync(historical.ContentMeta.MasterId, ct);
        if (latest == null) return null;
        var vm = _mapper.Map<ContentBlockUpsertViewModel>(historical);
        vm.Id = latest.ContentMeta.Id;
        vm.Version = latest.ContentMeta.Version;
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetContentBlockIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetUpsertModelAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ContentBlockUpsertViewModel();

    public override async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentBlockUpsertViewModel)model;
        var result = await SaveUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetContentBlockIndexAsync(ct);
        return vm.ContentBlocks.Select(cb => (object)new { id = cb.MasterId, title = cb.Title });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => await GetUpsertModelForRestoreAsync(historicalId, ct);
}
