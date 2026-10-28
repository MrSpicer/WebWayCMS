namespace WebWayCMS.Services.Relations;

/// <summary>
/// Hard-deletes one node of a content type, and only that node (its relation descendants are the
/// <see cref="IContentDeleteService"/>'s job). Every <c>AdminCrudModel</c> is one; a content type with no
/// admin handler registers its own as <see cref="IContentDeleter"/>.
/// </summary>
public interface IContentDeleter
{
    /// <summary>The node content type key this deleter handles, e.g. <c>"articles"</c>.</summary>
    string ContentTypeKey { get; }

    Task<bool> DeleteSelfAsync(Guid nodeId, CancellationToken ct = default);
}
