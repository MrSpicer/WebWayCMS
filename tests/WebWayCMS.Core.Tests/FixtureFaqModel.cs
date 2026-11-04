using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>A host-style model: nothing relation-specific beyond the constructor parameters.</summary>
public sealed class FixtureFaqModel : AdminCrudModel<FixtureFaqDTO>
{
    private readonly IContentStore<FixtureFaqDTO> _store;
    private readonly IMapper _mapper;

    public FixtureFaqModel(IContentStore<FixtureFaqDTO> store, IMapper mapper, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store;
        _mapper = mapper;
    }

    protected override IContentStore<FixtureFaqDTO> Store => _store;
    protected override string VersionHistoryContentType => "faqs";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/faqs";
    protected override Task<List<FixtureFaqDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "faqs";
    public override string DisplayName => "FAQ";
    public override string IndexViewPath => "i";
    public override string UpsertViewPath => "u";

    public override Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => id is { } nodeId && await _store.GetCurrentDraftAsync(nodeId, ct) is { } dto
            ? _mapper.Map<FixtureFaqUpsertViewModel>(dto)
            : null;

    public override object CreateEmptyUpsertViewModel() => new FixtureFaqUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (FixtureFaqUpsertViewModel)model;
        var result = await _store.SaveDraftAsync(_mapper.Map<FixtureFaqDTO>(vm), vm.ExpectedVersionNumber, ct);
        return new AdminSaveResult(result.Success, result.ErrorMessage, NodeId: result.NodeId);
    }

    protected override Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default) => _store.DeleteAsync(id, softDelete: false, ct);

    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
}
