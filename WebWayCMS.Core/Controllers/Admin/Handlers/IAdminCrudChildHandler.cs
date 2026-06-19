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

    Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default);

    bool SupportsVersionHistory => false;

    Task<VersionHistoryViewModel?> GetChildVersionHistoryViewModelAsync(string parentKey, Guid masterId, CancellationToken ct = default)
        => Task.FromResult<VersionHistoryViewModel?>(null);

    Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(null);

    Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(false);
}
