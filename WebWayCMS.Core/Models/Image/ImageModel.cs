using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Image;

public sealed class ImageModel : AdminCrudModel<ImageDTO>, IImageModel
{
    private readonly IContentStore<ImageDTO> _store;
    private readonly IMapper _mapper;
    private readonly IMediaLibrary _mediaLibrary;

    protected override IContentStore<ImageDTO> Store => _store;

    public override string ContentType => "images";
    public override string DisplayName => "Image";
    public override string IndexViewPath => "~/Views/AdminImage/Images.cshtml";
    public override string UpsertViewPath => "~/Views/AdminImage/ImageUpsert.cshtml";

    public ImageModel(IContentStore<ImageDTO> store, IMapper mapper, IMediaLibrary mediaLibrary, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
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

    public Task<ImageUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default)
        => LoadUpsertAsync(nodeId, _mapper.Map<ImageUpsertViewModel>, ct);

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, _mapper.Map<ImageUpsertViewModel>, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetImageIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetUpsertModelAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ImageUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ImageUpsertViewModel)model;

        // The hash must name a blob that actually exists: it arrives as an ordinary string field, so
        // an MCP client or a seed file can set it to anything.
        var blob = await _mediaLibrary.GetMetadataAsync(vm.BlobHash, ct);
        if (blob == null)
            return new AdminSaveResult(false, "That image file could not be found. Upload the image again.");

        return await SaveDraftAsync(_mapper.Map<ImageDTO>(vm), vm, "An error occurred while saving the image.", ct);
    }
}
