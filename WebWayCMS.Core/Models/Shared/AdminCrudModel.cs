using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Security;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Shared;

/// <summary>
/// Abstract base for content type models that also serve as their own IAdminCrudHandler (and as the
/// <see cref="IContentDeleter"/> for their content type).
/// Extends VersionedModel and provides sensible defaults for all versioning-related handler members.
/// </summary>
public abstract class AdminCrudModel<TDto> : VersionedModel<TDto>, IAdminCrudHandler, IContentDeleter
    where TDto : class, IVersionedContent
{
    private readonly IChangeSetScope _changeSetScope;
    private readonly IRelationBinder _relationBinder;
    private readonly IContentDeleteService _deletes;

    protected AdminCrudModel(IChangeSetScope changeSetScope, IRelationBinder relationBinder, IContentDeleteService deletes)
    {
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _relationBinder = relationBinder ?? throw new ArgumentNullException(nameof(relationBinder));
        _deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
    }

    /// <summary>Ambient change-set scoping, for derived models that group several writes into one entry.</summary>
    protected IChangeSetScope ChangeSetScope => _changeSetScope;

    /// <summary>The relation validator, for derived models that save relations outside the upsert view model (config JSON).</summary>
    protected IRelationBinder RelationBinder => _relationBinder;

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
    /// Sanitizes any rich-text fields on <paramref name="model"/> before persisting, validates its
    /// required fields and its <c>[ContentRelation]</c> targets, then delegates to
    /// <see cref="SaveUpsertCoreAsync"/> within a change-set scope. This is the single save choke point
    /// for both the admin UI and the MCP tools, so every content type's rich-text content is sanitized
    /// on save and every write is grouped under one change set.
    /// </summary>
    public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        RichTextSanitizer.Sanitize(model);

        var validation = ModelValidator.Validate(model)
                      ?? await _relationBinder.ValidateAsync(model, DisplayName, ct);
        if (validation != null)
            return validation;

        using var _ = _changeSetScope.Begin(ChangeSetKind.Save, null, null);
        return await SaveUpsertCoreAsync(model, ct);
    }

    /// <summary>Persists the (already sanitized) upsert view model. Implemented per content type.</summary>
    protected abstract Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default);

    /// <summary>Deletes the item and everything its relations take with it (see <see cref="TryDeleteAsync"/>).</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => (await TryDeleteAsync(id, ct)).Success;

    /// <summary>
    /// Deletes the item through the relation engine: its containment descendants and owned targets go
    /// with it in one transaction, and a reference to it from elsewhere blocks the delete or is removed,
    /// per the referrer's declaration. The item itself is deleted by <see cref="DeleteSelfAsync"/>.
    /// </summary>
    public Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> TryDeleteAsync(Guid id, CancellationToken ct = default)
        => _deletes.DeleteAsync(id, DeleteSelfAsync, ct);

    /// <summary>Hard-deletes this one item (and anything only it owns outside the relation engine).</summary>
    protected abstract Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default);

    string IContentDeleter.ContentTypeKey => ContentType;

    Task<bool> IContentDeleter.DeleteSelfAsync(Guid nodeId, CancellationToken ct) => DeleteSelfAsync(nodeId, ct);
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

    public virtual async Task<(bool Success, string? ErrorMessage)> TryDeleteVersionAsync(Guid id, CancellationToken ct = default)
        => await DeleteVersionAsync(id, ct) ? (true, null) : (false, IAdminCrudHandler.VersionNotDeletedMessage);
}
