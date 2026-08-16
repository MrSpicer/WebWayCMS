using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Security;

namespace WebWayCMS.Models.Shared;

/// <summary>
/// Abstract base for content type models that also serve as their own IAdminCrudHandler.
/// Extends VersionedModel and provides sensible defaults for all versioning-related handler members.
/// </summary>
public abstract class AdminCrudModel<TDto> : VersionedModel<TDto>, IAdminCrudHandler
    where TDto : class, IVersionedContent
{
    private readonly IChangeSetScope _changeSetScope;

    protected AdminCrudModel(IChangeSetScope changeSetScope)
    {
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
    }

    /// <summary>The store backing this content type's reads and writes.</summary>
    protected abstract IContentStore<TDto> Store { get; }

    public abstract string ContentType { get; }
    public abstract string DisplayName { get; }
    public virtual string[]? WriteRoles => null;
    public virtual bool SupportsPublishing => true;
    public virtual bool SupportsPreview => false;
    public virtual string[]? PublishRoles => WriteRoles;

    public abstract string IndexViewPath { get; }
    public abstract string UpsertViewPath { get; }

    public abstract Task<object> GetIndexViewModelAsync(CancellationToken ct = default);
    public abstract Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default);
    public abstract object CreateEmptyUpsertViewModel();

    /// <summary>
    /// Sanitizes any rich-text fields on <paramref name="model"/> before persisting, then delegates to
    /// <see cref="SaveUpsertCoreAsync"/> within a change-set scope. This is the single save choke point
    /// for both the admin UI and the MCP tools, so every content type's rich-text content is sanitized
    /// on save and every write is grouped under one change set.
    /// </summary>
    public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        RichTextSanitizer.Sanitize(model);

        var validation = ModelValidator.Validate(model);
        if (validation != null)
            return validation;

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, null, null);
        return await SaveUpsertCoreAsync(model, ct);
    }

    /// <summary>Persists the (already sanitized) upsert view model. Implemented per content type.</summary>
    protected abstract Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default);

    public abstract Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    public abstract Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default);

    public virtual bool HasSecondaryApiList => false;

    public virtual IReadOnlyList<string> SecondaryApiListKeys => [];

    public virtual Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public virtual IAdminRegistryHandler? RegistryHandler => null;
    public virtual IAdminCrudChildHandler? ChildHandler => null;

    public virtual bool SupportsVersionHistory => true;

    public virtual Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public virtual Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(null);

    /// <summary>
    /// Loads a historical version together with the current draft's version number, so a restore-edit
    /// form always carries the <see cref="BaseContentViewModel.ExpectedVersionNumber"/> of the *current*
    /// draft rather than the historical row. Returns null when either is missing.
    /// </summary>
    protected async Task<(TDto Historical, int CurrentVersionNumber)?> LoadRestoreVersionAsync(
        Guid historicalId, CancellationToken ct = default)
    {
        var historical = await Store.GetVersionAsync(historicalId, ct);
        if (historical == null) return null;

        var current = await Store.GetCurrentDraftAsync(historical.Version.Node.Id, ct);
        if (current == null) return null;

        return (historical, current.Version.VersionNumber);
    }

    public virtual async Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await Store.PublishAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public virtual async Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await Store.UnpublishAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public virtual async Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var result = await Store.RestoreAsync(versionId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public virtual Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);
}
