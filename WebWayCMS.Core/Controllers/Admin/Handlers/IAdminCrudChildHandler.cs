using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Models.Shared;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Handles CRUD for child entities belonging to a parent content type.
/// Example: articles within an article list, items within a content zone.
/// </summary>
public interface IAdminCrudChildHandler
{
    /// <summary>URL segment identifying the child type, e.g. "articles" or "items".</summary>
    string ChildType { get; }

    string ChildDisplayName { get; }

    /// <summary>
    /// Roles allowed for write operations on child entities.
    /// null = Admin only (inherits class-level attribute).
    /// </summary>
    string[]? WriteRoles { get; }

    string ChildIndexViewPath { get; }
    string ChildUpsertViewPath { get; }

    /// <summary>
    /// Returns the view model for the child list page, or null if the parent was not found.
    /// parentKey is a slug (articles) or Guid string (zone items).
    /// </summary>
    Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default);

    /// <summary>Returns the upsert view model for a child entity, or null if not found.</summary>
    Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default);

    /// <summary>
    /// Sets any handler-specific ViewData needed by the child upsert view.
    /// Called by the controller before rendering the upsert view.
    /// May perform async operations (e.g. fetching parent title).
    /// </summary>
    Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default);

    object CreateEmptyChildUpsertViewModel();

    Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default);

    Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default);

    bool SupportsReorder { get; }

    Task<(bool Success, string? ErrorMessage)> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default);

    /// <summary>Whether a child can be moved to another parent (e.g. a zone item to another zone).</summary>
    bool SupportsMove => false;

    /// <summary>Moves a child under <paramref name="targetParentKey"/> at a 0-based position.</summary>
    Task<(bool Success, string? ErrorMessage)> MoveAsync(Guid childId, string targetParentKey, int position, CancellationToken ct = default)
        => Task.FromResult<(bool Success, string? ErrorMessage)>((false, "Move is not supported for this child type."));

    bool SupportsVersionHistory => false;

    Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid masterId, CancellationToken ct = default)
        => Task.FromResult<VersionHistoryViewModel?>(null);

    Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(null);

    Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    /// Restores a historical child version as the child's current version in one step (the admin
    /// history page's Restore button and MCP <c>restore_child_version</c>). The default saves the
    /// <see cref="GetChildRestoreVersionViewModelAsync"/> form back through
    /// <see cref="SaveChildUpsertAsync"/>, which carries its own expected version;
    /// <paramref name="expectedVersionNumber"/> is the child's current version as the caller saw it.
    /// </summary>
    async Task<AdminSaveResult> RestoreChildVersionAsync(string parentKey, Guid versionId, int? expectedVersionNumber, CancellationToken ct = default)
    {
        var vm = await GetChildRestoreVersionViewModelAsync(parentKey, versionId, ct);
        return vm == null
            ? new AdminSaveResult(false, VersionNotFoundMessage)
            : await SaveChildUpsertAsync(parentKey, vm, ct);
    }

    /// <summary><see cref="RestoreChildVersionAsync"/>'s error when the version does not exist.</summary>
    const string VersionNotFoundMessage = "Version not found.";

    /// <summary>
    /// <see cref="DeleteChildVersionAsync"/> with the reason a delete was refused (e.g. the version is
    /// part of page history), for the admin UI and MCP to show.
    /// </summary>
    async Task<(bool Success, string? ErrorMessage)> TryDeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => await DeleteChildVersionAsync(id, ct) ? (true, null) : (false, IAdminCrudHandler.VersionNotDeletedMessage);
}
