using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.Image;

public sealed class ImageModel : AdminCrudModel<ImageDTO>, IImageModel
{
    private readonly IContentStore<ImageDTO> _store;
    private readonly IMapper _mapper;
    private readonly IMediaLibrary _mediaLibrary;

    protected override IContentStore<ImageDTO> Store => _store;

    protected override string VersionHistoryContentType => "images";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/images";
    protected override Task<List<ImageDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "images";
    public override string DisplayName => "Image";
    public override string IndexViewPath => "~/Views/AdminImage/Images.cshtml";
    public override string UpsertViewPath => "~/Views/AdminImage/ImageUpsert.cshtml";

    public ImageModel(IContentStore<ImageDTO> store, IMapper mapper, IMediaLibrary mediaLibrary, IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    }

    public async Task<ImageViewModel?> GetViewModelByNodeIdAsync(Guid nodeId, CancellationToken ct = default)
    {
        var dto = await _store.GetAsync(nodeId, ct);
        if (dto == null) return null;

        var vm = _mapper.Map<ImageViewModel>(dto);

        // Dimensions and content type live on the shared media catalog, not on the content row, so
        // that re-using one upload across many items cannot leave them disagreeing.
        var blob = await _mediaLibrary.GetMetadataAsync(dto.BlobHash, ct);
        if (blob != null)
        {
            vm.ContentType = blob.ContentType;
            vm.Width = blob.Width;
            vm.Height = blob.Height;
            vm.OriginalFileName = blob.OriginalFileName;
        }

        return vm;
    }

    public async Task<ImageIndexViewModel> GetImageIndexAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        var items = dtos.Select(d => _mapper.Map<ImageItemViewModel>(d)).ToList();
        return new ImageIndexViewModel { Images = items };
    }

    public async Task<ImageUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default)
    {
        if (nodeId == null || nodeId == Guid.Empty)
            return new ImageUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(nodeId.Value, ct);
        if (dto == null)
            return null;

        return _mapper.Map<ImageUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveUpsertAsync(ImageUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        // The hash must name a blob that actually exists: it arrives as an ordinary string field, so
        // an MCP client or a seed file can set it to anything.
        var blob = await _mediaLibrary.GetMetadataAsync(model.BlobHash, ct);
        if (blob == null)
            return (false, "That image file could not be found. Upload the image again.");

        var dto = _mapper.Map<ImageDTO>(model);
        var result = await _store.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "An error occurred while saving the image.");
        model.NodeId = result.NodeId;
        return (true, null);
    }

    public override async Task<bool> DeleteAsync(Guid nodeId, CancellationToken ct = default)
    {
        return await _store.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var vm = _mapper.Map<ImageUpsertViewModel>(loaded.Value.Historical);
        vm.ExpectedVersionNumber = loaded.Value.CurrentVersionNumber;
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetImageIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetUpsertModelAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ImageUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ImageUpsertViewModel)model;
        var result = await SaveUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true, NodeId: vm.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetImageIndexAsync(ct);
        return vm.Images.Select(i => (object)new { id = i.NodeId, title = i.Title });
    }
}
