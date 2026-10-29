using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Cross-type relation queries. "Live" means a source's current draft or published version: historical
/// versions that still point at a node are ignored (they resolve to nothing once it is gone).
/// </summary>
public interface IContentRelations
{
    /// <summary>
    /// The sources whose current draft or published version relates to <paramref name="targetNodeId"/>,
    /// one entry per (source, relation name), optionally filtered to <paramref name="kind"/>.
    /// </summary>
    Task<IReadOnlyList<RelationReferrer>> GetLiveReferrersAsync(Guid targetNodeId, RelationKind? kind = null, CancellationToken ct = default);

    /// <summary>The live containment children of <paramref name="parentNodeId"/>.</summary>
    Task<IReadOnlyList<RelationReferrer>> GetLiveChildrenAsync(Guid parentNodeId, CancellationToken ct = default);

    /// <summary>The live owned targets of <paramref name="sourceNodeId"/>'s current draft and published version.</summary>
    Task<IReadOnlyList<Guid>> GetLiveOwnedTargetsAsync(Guid sourceNodeId, CancellationToken ct = default);

    /// <summary>
    /// The target of <paramref name="sourceNodeId"/>'s current draft's relation <paramref name="name"/> (the
    /// first by ordinal), or null when the node has no current draft or no such relation.
    /// </summary>
    Task<Guid?> GetCurrentDraftTargetAsync(Guid sourceNodeId, string name, CancellationToken ct = default);

    /// <summary>The nodes among <paramref name="nodeIds"/> that have a current draft (not deleted), with their type and title.</summary>
    Task<IReadOnlyDictionary<Guid, RelationNode>> GetNodesAsync(IEnumerable<Guid> nodeIds, CancellationToken ct = default);
}

/// <summary>A source that relates to a node: which relation, and on which of its live versions.</summary>
public sealed record RelationReferrer(
    Guid SourceNodeId,
    string ContentTypeKey,
    string Title,
    string Name,
    RelationKind Kind,
    bool OnCurrentDraft,
    bool OnPublished);

/// <summary>A node's content type and current-draft title.</summary>
public sealed record RelationNode(Guid NodeId, string ContentTypeKey, string Title);
