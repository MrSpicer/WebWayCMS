using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Minimal AdminCrudModel that does NOT override GetRestoreVersionViewModelAsync, so the base
/// virtual (returns null) is executed for coverage.
/// </summary>
internal sealed class MinimalCrudModel : AdminCrudModel<ContentBlockDTO>
{
    public MinimalCrudModel(IContentStore<ContentBlockDTO> store, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store;
    }

    private readonly IContentStore<ContentBlockDTO> _store;

    protected override IContentStore<ContentBlockDTO> Store => _store;

    public bool SaveUpsertCoreCalled { get; private set; }

    protected override string VersionHistoryContentType => "minimal-crud";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/minimal-crud";
    protected override Task<List<ContentBlockDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => Task.FromResult(new List<ContentBlockDTO>());
    public bool DeleteVersionResult { get; set; } = true;

    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => Task.FromResult(DeleteVersionResult);

    public override string ContentType => "minimal-crud";
    public override string DisplayName => "Minimal Crud";
    public override string IndexViewPath => "i";
    public override string UpsertViewPath => "u";

    public override Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");
    public override Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public override object CreateEmptyUpsertViewModel() => new object();

    protected override Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        SaveUpsertCoreCalled = true;
        return Task.FromResult(new AdminSaveResult(true));
    }

    protected override Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
}
