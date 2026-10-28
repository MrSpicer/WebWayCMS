namespace WebWayCMS.Services.Relations;

/// <summary>
/// Deletes a node together with what its relations say must go with it, as one transaction: its
/// containment descendants (recursively) and the owned targets of everything deleted. A reference from
/// outside that set blocks the delete (<c>Restrict</c>) or is removed from its source (<c>Detach</c>).
/// </summary>
public interface IContentDeleteService
{
    /// <summary>
    /// Deletes <paramref name="nodeId"/> through <paramref name="deleteSelf"/> (the owning model's own
    /// delete) after its descendants. <c>DeletedDescendants</c> counts the containment descendants deleted.
    /// </summary>
    Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> DeleteAsync(
        Guid nodeId, Func<Guid, CancellationToken, Task<bool>> deleteSelf, CancellationToken ct = default);
}
