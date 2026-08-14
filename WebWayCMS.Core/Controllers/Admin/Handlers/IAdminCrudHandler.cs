using Microsoft.AspNetCore.Http;

using WebWayCMS.Models.Shared;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Handles admin CRUD for a single top-level content type.
/// Registered as IAdminCrudHandler and resolved by IAdminHandlerRegistry via ContentType.
/// </summary>
public interface IAdminCrudHandler
{
    /// <summary>URL segment used to identify this content type, e.g. "contentblocks", "pages".</summary>
    string ContentType { get; }

    string DisplayName { get; }

    /// <summary>
    /// Roles allowed for write operations (Edit POST, Delete).
    /// null = Admin only. Provide ["Admin","Editor"] to also allow editors.
    /// </summary>
    string[]? WriteRoles { get; }

    /// <summary>Whether this content type can be explicitly published/unpublished.</summary>
    bool SupportsPublishing => true;

    /// <summary>Whether this content type can be previewed (renders its current draft).</summary>
    bool SupportsPreview => false;

    /// <summary>Roles allowed to publish/unpublish. Defaults to <see cref="WriteRoles"/>.</summary>
    string[]? PublishRoles => WriteRoles;

    /// <summary>Absolute Razor view path, e.g. "~/Views/AdminContentBlock/ContentBlocks.cshtml".</summary>
    string IndexViewPath { get; }

    /// <summary>Absolute Razor view path for the create/edit form.</summary>
    string UpsertViewPath { get; }

    Task<object> GetIndexViewModelAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the index view model, allowing handlers to filter based on query parameters.
    /// Default implementation falls through to the parameterless overload.
    /// </summary>
    Task<object> GetIndexViewModelAsync(IQueryCollection query, CancellationToken ct = default)
        => GetIndexViewModelAsync(ct);

    /// <summary>
    /// Returns the upsert view model, or null if the record was not found.
    /// id is null for create. query carries extra GET params (e.g. parentRoute for pages).
    /// </summary>
    Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default);

    object CreateEmptyUpsertViewModel();

    Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default)
        => Task.FromResult(new AdminSaveResult(false, "Publishing is not supported."));

    Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default)
        => Task.FromResult(new AdminSaveResult(false, "Unpublishing is not supported."));

    Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default)
        => Task.FromResult(new AdminSaveResult(false, "Restoring versions is not supported."));

    /// <summary>Returns [ { id, title } ] for entity picker dropdowns.</summary>
    Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default);

    /// <summary>Whether this handler exposes additional named API lists (e.g. "articlelists").</summary>
    bool HasSecondaryApiList { get; }

    /// <summary>Returns a secondary named list, keyed by an arbitrary string.</summary>
    Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default);

    /// <summary>Optional: exposes GET /wadmin/{contentType}/registry endpoints.</summary>
    IAdminRegistryHandler? RegistryHandler { get; }

    /// <summary>Optional: manages child entities (articles, zone items).</summary>
    IAdminCrudChildHandler? ChildHandler { get; }

    bool SupportsVersionHistory => false;

    Task<VersionHistoryViewModel?> GetVersionHistoryViewModelAsync(Guid nodeId, CancellationToken ct = default)
        => Task.FromResult<VersionHistoryViewModel?>(null);

    Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(null);

    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(false);
}
