using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.ContentBlock;

public sealed class ContentBlockModel : AdminCrudModel<ContentBlockDTO>, IContentBlockModel
{
    private readonly IContentStore<ContentBlockDTO> _store;
    private readonly IMapper _mapper;

    protected override IContentStore<ContentBlockDTO> Store => _store;

    public override string ContentType => "contentblocks";
    public override string DisplayName => "Content Block";
    public override string IndexViewPath => "~/Views/AdminContentBlock/ContentBlocks.cshtml";
    public override string UpsertViewPath => "~/Views/AdminContentBlock/ContentBlockUpsert.cshtml";

    public ContentBlockModel(IContentStore<ContentBlockDTO> store, IMapper mapper, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
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

    public Task<ContentBlockUpsertViewModel?> GetUpsertModelAsync(Guid? nodeId, CancellationToken ct = default)
        => LoadUpsertAsync(nodeId, _mapper.Map<ContentBlockUpsertViewModel>, ct);

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, _mapper.Map<ContentBlockUpsertViewModel>, ct);

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetContentBlockIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetUpsertModelAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new ContentBlockUpsertViewModel();

    protected override Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentBlockUpsertViewModel)model;
        return SaveDraftAsync(_mapper.Map<ContentBlockDTO>(vm), vm, "An error occurred while saving the content block.", ct);
    }
}
