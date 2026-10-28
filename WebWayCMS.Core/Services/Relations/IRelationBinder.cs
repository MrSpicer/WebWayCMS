using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Services.Relations;

/// <summary>Validates an upsert view model's <c>[ContentRelation]</c> values at the save choke point.</summary>
public interface IRelationBinder
{
    /// <summary>
    /// A field error for the first relation property that is required but empty, or that names a node
    /// with no current draft of the declared content type; null when every relation is valid.
    /// <paramref name="displayName"/> is the source type's display name, used in the message.
    /// </summary>
    Task<AdminSaveResult?> ValidateAsync(object model, string displayName, CancellationToken ct = default);
}
