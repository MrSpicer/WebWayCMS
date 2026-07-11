using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Security;

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

    public abstract string IndexViewPath { get; }
    public abstract string UpsertViewPath { get; }

    public abstract Task<object> GetIndexViewModelAsync(CancellationToken ct = default);
    public abstract Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default);
    public abstract object CreateEmptyUpsertViewModel();

    /// <summary>
    /// Sanitizes any rich-text fields on <paramref name="model"/> before persisting, then delegates to
    /// <see cref="SaveUpsertCoreAsync"/>. This is the single save choke point for both the admin UI and
    /// the MCP tools, so every content type's rich-text content is sanitized on save.
    /// </summary>
    public Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        RichTextSanitizer.Sanitize(model);
        return SaveUpsertCoreAsync(model, ct);
    }

    /// <summary>Persists the (already sanitized) upsert view model. Implemented per content type.</summary>
    protected abstract Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default);

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