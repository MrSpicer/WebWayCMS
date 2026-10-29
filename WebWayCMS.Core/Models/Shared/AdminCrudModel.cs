using Microsoft.AspNetCore.Http;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
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

    public virtual Task<IEnumerable<object>> GetApiListExcludingSubtreeAsync(Guid excludeSubtreeOf, CancellationToken ct = default)
        => GetApiListAsync(ct);

    public virtual bool HasSecondaryApiList => false;

    public virtual IReadOnlyList<string> SecondaryApiListKeys => [];

    public virtual Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    /// <summary>The children placed under <paramref name="parentNodeId"/> by this type's containment <paramref name="relationName"/>.</summary>
    public virtual async Task<IReadOnlyList<RelatedChild>> GetRelatedChildrenAsync(
        string relationName, Guid parentNodeId, CancellationToken ct = default)
    {
        if (DeclaredContainment(relationName) == null)
            return [];

        var children = await Store.FindCurrentDraftsRelatedAsync(relationName, parentNodeId, ct);
        return children
            .Select(c => new RelatedChild(c.Version.Node.Id, c.Version.Title, c.Version.State, c.Version.OrdinalOf(relationName)))
            .ToList();
    }

    /// <summary>
    /// Reorders an ordered containment's children: each child whose position changed is saved as a new
    /// draft with its new ordinal ("Position changed"), checked against the version it was read at.
    /// </summary>
    public virtual async Task<(bool Success, string? ErrorMessage)> ReorderRelatedAsync(
        string relationName, Guid parentNodeId, IReadOnlyList<Guid> orderedNodeIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        if (DeclaredContainment(relationName) is not { Attribute.Ordered: true })
            return (false, IAdminCrudHandler.ReorderNotSupportedMessage);

        var children = (await Store.FindCurrentDraftsRelatedAsync(relationName, parentNodeId, ct))
            .ToDictionary(c => c.Version.Node.Id);
        if (orderedNodeIds.Count != children.Count
            || orderedNodeIds.Distinct().Count() != children.Count
            || !orderedNodeIds.All(children.ContainsKey))
            return (false, ReorderSetMessage);

        using var scope = _changeSetScope.Begin(ChangeSetKind.Save, parentNodeId, null);
        using var note = _changeSetScope.UseChangeNote("Position changed");
        for (var position = 0; position < orderedNodeIds.Count; position++)
        {
            var child = children[orderedNodeIds[position]];
            if (child.Version.OrdinalOf(relationName) == position)
                continue;

            child.Version.SetRelation(relationName, parentNodeId, RelationKind.Containment,
                child.Version.QualifierOf(relationName) ?? string.Empty, position);
            var saved = await Store.SaveDraftAsync(child, child.Version.VersionNumber, ct);
            if (!saved.Success)
                return (false, saved.ErrorMessage);
        }
        return (true, null);
    }

    /// <summary>
    /// Moves a child by saving its upsert view model with the containment pointing at the new parent (so
    /// validation, the cycle guard and the mapper all apply), then, for an ordered relation, puts it at
    /// <paramref name="position"/> among its new siblings.
    /// </summary>
    public virtual async Task<(bool Success, string? ErrorMessage)> MoveRelatedAsync(
        string relationName, Guid childNodeId, Guid targetParentNodeId, int? position, int? expectedVersionNumber,
        CancellationToken ct = default)
    {
        var declared = DeclaredContainment(relationName);
        if (declared == null)
            return (false, IAdminCrudHandler.MoveNotSupportedMessage);

        var model = await GetUpsertViewModelAsync(childNodeId, EmptyQuery, ct);
        if (model == null)
            return (false, $"No {DisplayName.ToLowerInvariant()} found with id '{childNodeId}'.");
        if (expectedVersionNumber.HasValue
            && model is BaseContentViewModel { ExpectedVersionNumber: { } current }
            && current != expectedVersionNumber.Value)
            return (false, ContentStore<TDto>.StaleVersionMessage);

        RelationProperties.Write(model, RelationProperties.Read(model)
            .Where(v => v.Name != relationName)
            .Append(new RelationValue(relationName, targetParentNodeId, 0, string.Empty)));
        AdminSaveResult saved;
        using (_changeSetScope.UseChangeNote("Moved"))
            saved = await SaveUpsertAsync(model, ct);
        if (!saved.Success)
            return (false, saved.ErrorMessage);

        if (!declared.Attribute.Ordered || position == null)
            return (true, null);

        var siblings = (await GetRelatedChildrenAsync(relationName, targetParentNodeId, ct))
            .Select(c => c.Id)
            .Where(id => id != childNodeId)
            .ToList();
        siblings.Insert(Math.Clamp(position.Value, 0, siblings.Count), childNodeId);
        return await ReorderRelatedAsync(relationName, targetParentNodeId, siblings, ct);
    }

    private static readonly IQueryCollection EmptyQuery = new QueryCollection();

    /// <summary><see cref="ReorderRelatedAsync"/>'s error when the order is not the parent's children, each once.</summary>
    public const string ReorderSetMessage = "The new order must list every child exactly once.";

    /// <summary>This type's containment <paramref name="relationName"/>, if its upsert view model declares one.</summary>
    private RelationPropertyInfo? DeclaredContainment(string relationName)
        => RelationProperties.Describe(CreateEmptyUpsertViewModel().GetType())
            .FirstOrDefault(r => r.Name == relationName && r.Attribute.Kind == ContentRelationKind.Containment);

    public virtual IAdminRegistryHandler? RegistryHandler => null;

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

    /// <summary>The restore, refused with the stale-version message when the current draft moved past <paramref name="expectedVersionNumber"/>.</summary>
    public virtual async Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, int? expectedVersionNumber, CancellationToken ct = default)
    {
        if (expectedVersionNumber.HasValue)
        {
            var historical = await Store.GetVersionAsync(versionId, ct);
            if (historical == null)
                return new AdminSaveResult(false, IAdminCrudHandler.VersionNotFoundMessage);
            var current = await Store.GetCurrentDraftAsync(historical.Version.Node.Id, ct);
            if (current?.Version.VersionNumber != expectedVersionNumber.Value)
                return new AdminSaveResult(false, ContentStore<TDto>.StaleVersionMessage);
        }
        return await RestoreVersionAsync(versionId, ct);
    }

    public virtual Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeleteVersionCoreAsync(id, ct);

    public virtual async Task<(bool Success, string? ErrorMessage)> TryDeleteVersionAsync(Guid id, CancellationToken ct = default)
        => await DeleteVersionAsync(id, ct) ? (true, null) : (false, IAdminCrudHandler.VersionNotDeletedMessage);
}
