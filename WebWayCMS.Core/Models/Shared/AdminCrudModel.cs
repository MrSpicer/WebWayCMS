using Microsoft.AspNetCore.Http;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

/// <summary>
/// Abstract base for content type models that also serve as their own IAdminCrudHandler.
/// Extends VersionedModel and provides sensible defaults for all versioning-related handler members.
/// </summary>
public abstract class AdminCrudModel<TDto> : VersionedModel<TDto>, IAdminCrudHandler
    where TDto : class, IContent
{
    public abstract string ContentType { get; }
    public abstract string DisplayName { get; }
    public virtual string[]? WriteRoles => null;

    public abstract Task<object> GetIndexViewModelAsync(CancellationToken ct = default);
    public abstract Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default);
    public abstract object CreateEmptyUpsertViewModel();
    public abstract Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default);
    public abstract Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    public abstract Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default);

    public virtual bool HasSecondaryApiList => false;

    public virtual Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public virtual IAdminRegistryHandler? RegistryHandler => null;
    public virtual IAdminCrudChildHandler? ChildHandler => null;

    public virtual bool SupportsVersionHistory => true;

    public virtual Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid masterId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(masterId, ct: ct);

    public virtual Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(null);

    public virtual Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);
}
