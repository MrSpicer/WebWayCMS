using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A source that relates to a node: which relation, and on which of its live versions.</summary>
public sealed record RelationReferrer(
    Guid SourceNodeId,
    string ContentTypeKey,
    string Title,
    string Name,
    RelationKind Kind,
    bool OnCurrentDraft,
    bool OnPublished);
